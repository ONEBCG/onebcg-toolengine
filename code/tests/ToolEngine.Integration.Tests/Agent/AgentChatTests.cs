namespace ToolEngine.Integration.Tests.Agent;

using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ToolEngine.Core.Abstractions.Common;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Core.Domain.Schema;
using ToolEngine.Integration.Tests.Infrastructure;
using ToolEngine.Llm.Abstractions;
using ToolEngine.Llm.Conversion;
using ToolEngine.Llm.Models;
using ToolEngine.Llm.Options;
using ToolEngine.Llm.Session;
using ToolEngine.Tools.Abstractions.Metadata;
using ToolEngine.Tools.Registry;
using Xunit;

public sealed class AgentChatTests : IntegrationTestBase
{
    private readonly ILlmProvider _stubProvider;

    public AgentChatTests()
    {
        _stubProvider = Substitute.For<ILlmProvider>();
        _stubProvider.ProviderName.Returns("stub");
    }

    // NOTE: AgentChatCommand requires AgentOrchestrator which requires IProviderRouter.
    // In integration tests, we wire a stub ILlmProvider through a stub IProviderRouter.
    // The key invariant we test is that tool invocations go through the MediatR pipeline
    // and that CallerType = AiAgent is set on the resulting ToolInvocationRecord.

    [Fact]
    public async Task AgentChat_EndTurn_ReturnsSuccessResponse()
    {
        await SeedTenantAsync("acme");

        _stubProvider.CompleteAsync(
                Arg.Any<IReadOnlyList<LlmMessage>>(),
                Arg.Any<IReadOnlyList<LlmToolDefinition>>(),
                Arg.Any<ProviderOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(new LlmResponse(StopReason.EndTurn, "The answer is 42.", null, new LlmUsage(50, 25)));

        var orchestrator = BuildOrchestrator();
        var result = await orchestrator.RunAsync(
            Guid.NewGuid(), "acme", "user1", "What is 6 x 7?",
            sessionId: null, tenantProviderOverride: null, default);

        result.Success.Should().BeTrue();
        result.Reply.Should().Be("The answer is 42.");
        result.Usage.TotalTokens.Should().Be(75);
    }

    [Fact]
    public async Task AgentChat_ToolInvocation_SetsCallerType_AiAgent()
    {
        await SeedTenantAsync("acme");

        var toolArgs = JsonDocument.Parse("{\"a\":6,\"b\":7,\"operator\":\"multiply\"}").RootElement;

        _stubProvider.CompleteAsync(
                Arg.Any<IReadOnlyList<LlmMessage>>(),
                Arg.Any<IReadOnlyList<LlmToolDefinition>>(),
                Arg.Any<ProviderOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(
                new LlmResponse(StopReason.ToolUse, null, new LlmToolCall("call_1", "math__calculate", toolArgs), new LlmUsage(50, 10)),
                new LlmResponse(StopReason.EndTurn, "Result: 42", null, new LlmUsage(30, 15)));

        var orchestrator = BuildOrchestrator();
        var result = await orchestrator.RunAsync(
            Guid.NewGuid(), "acme", "user1", "What is 6 x 7?",
            sessionId: null, tenantProviderOverride: null, default);

        result.Success.Should().BeTrue();
        result.ToolInvoked.Should().Be("math.calculate");

        // Verify the invocation record in DB has CallerType = AiAgent (H4 compliance).
        var record = Db.Set<ToolEngine.Core.Domain.Entities.ToolInvocationRecord>()
                       .OrderByDescending(r => r.InvokedAt)
                       .FirstOrDefault();
        record.Should().NotBeNull();
        record!.CallerType.Should().Be(CallerType.AiAgent);
    }

    [Fact]
    public async Task AgentChat_GovernanceMetadataJson_IsPersisted()
    {
        await SeedTenantAsync("acme");

        var toolArgs = JsonDocument.Parse("{\"a\":2,\"b\":3,\"operator\":\"add\"}").RootElement;

        _stubProvider.CompleteAsync(
                Arg.Any<IReadOnlyList<LlmMessage>>(),
                Arg.Any<IReadOnlyList<LlmToolDefinition>>(),
                Arg.Any<ProviderOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(
                new LlmResponse(StopReason.ToolUse, null, new LlmToolCall("call_2", "math__calculate", toolArgs), new LlmUsage(50, 10)),
                new LlmResponse(StopReason.EndTurn, "Result: 5", null, new LlmUsage(30, 15)));

        var orchestrator = BuildOrchestrator();
        await orchestrator.RunAsync(
            Guid.NewGuid(), "acme", "user1", "What is 2 + 3?",
            sessionId: null, tenantProviderOverride: null, default);

        var record = Db.Set<ToolEngine.Core.Domain.Entities.ToolInvocationRecord>()
                       .OrderByDescending(r => r.InvokedAt)
                       .FirstOrDefault();
        record.Should().NotBeNull();
        record!.GovernanceMetadataJson.Should().NotBeNull();
        record.GovernanceMetadataJson.Should().Contain("sessionId");
    }

    [Fact]
    public async Task AgentChat_SessionId_IsReturnedInResponse()
    {
        await SeedTenantAsync("acme");

        _stubProvider.CompleteAsync(
                Arg.Any<IReadOnlyList<LlmMessage>>(),
                Arg.Any<IReadOnlyList<LlmToolDefinition>>(),
                Arg.Any<ProviderOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(new LlmResponse(StopReason.EndTurn, "Hello!", null, new LlmUsage(10, 5)));

        var orchestrator = BuildOrchestrator();
        var result = await orchestrator.RunAsync(
            Guid.NewGuid(), "acme", "user1", "Hello",
            sessionId: null, tenantProviderOverride: null, default);

        result.SessionId.Should().NotBeNullOrEmpty();
    }

    // Builds an AgentOrchestrator using the real DI container (MediatR + behaviors)
    // combined with stub provider and stub provider router.
    // IToolRegistry is stubbed inline — IToolRegistry is not registered by
    // IntegrationTestBase (only production hosts call AddToolRegistry()).
    // AgentSessionStore is constructed directly to avoid a DI registration requirement
    // on IntegrationTestBase.
    private AgentOrchestrator BuildOrchestrator()
    {
        var cacheProvider = Services.GetRequiredService<ICacheProvider>();
        var sessionStore  = new AgentSessionStore(cacheProvider, NullLogger<AgentSessionStore>.Instance);

        // Stub IToolRegistry with a math.calculate descriptor so the orchestrator
        // can build LLM tool definitions without requiring the real registry in DI.
        var mathSchema = new ToolSchema(
            TypeName:     "CalculateInput",
            Description:  "Performs arithmetic calculations (add, subtract, multiply, divide).",
            WhenToUse:    "When the user asks for a numeric calculation.",
            WhenNotToUse: "When the input is non-numeric or the operation is not arithmetic.",
            Parameters:
            [
                new ToolParameter("a",        "number", "First operand"),
                new ToolParameter("b",        "number", "Second operand"),
                new ToolParameter("operator", "string", "Operation: add | subtract | multiply | divide")
            ],
            Examples: []);

        var calcDescriptor = new ToolDescriptor(
            new ToolMetadata(
                Namespace:    "math",
                Name:         "calculate",
                Version:      "1.0",
                Description:  "Arithmetic calculator",
                Type:         ToolType.Logic,
                InputSchema:  mathSchema,
                OutputSchema: ToolSchema.Empty),
            HandlerType: typeof(object));  // handler type not used by orchestrator

        var registry = Substitute.For<IToolRegistry>();
        registry.ListAll(Arg.Any<string?>()).Returns([calcDescriptor]);

        var converter     = new ToolSchemaConverter();
        var mediator      = Services.GetRequiredService<IMediator>();

        var providerRouter = Substitute.For<IProviderRouter>();
        providerRouter.Select(Arg.Any<string?>(), Arg.Any<ToolDescriptor?>())
                      .Returns((_stubProvider, new ProviderOptions { Model = "stub-model" }));

        var opts = Microsoft.Extensions.Options.Options.Create(new LlmOptions
        {
            Budget = new BudgetOptions { MaxIterations = 10, MaxTokensPerSession = 32_768 }
        });

        return new AgentOrchestrator(
            sessionStore, providerRouter, registry, converter, mediator, opts,
            NullLogger<AgentOrchestrator>.Instance);
    }
}
