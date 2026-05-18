namespace ToolEngine.Llm.Tests;

using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using ToolEngine.Core.Abstractions.Common;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Core.Domain.Schema;
using ToolEngine.Llm.Abstractions;
using ToolEngine.Llm.Conversion;
using ToolEngine.Llm.Models;
using ToolEngine.Llm.Options;
using ToolEngine.Llm.Routing;
using ToolEngine.Llm.Guards;
using ToolEngine.Llm.Session;
using ToolEngine.Tools.Abstractions.Metadata;
using ToolEngine.Tools.Registry;
using Xunit;

public sealed class AgentOrchestratorTests
{
    private static readonly JsonElement EmptyJson   = JsonDocument.Parse("{}").RootElement;
    private static readonly LlmUsage    SmallUsage  = new(50, 25);

    private static ToolDescriptor MakeDescriptor(string ns = "math", string name = "calculate")
    {
        var schema   = ToolSchema.For<object>("Does math.");
        var metadata = new ToolMetadata(ns, name, "v1", "Does math.", ToolType.Logic, schema, ToolSchema.Empty);
        return new ToolDescriptor(metadata, typeof(object));
    }

    // Creates an AgentSessionStore backed by a cache stub that always returns
    // null on Get (fresh session every run) and silently accepts Set/Remove.
    private static AgentSessionStore MakeSessionStore()
    {
        var cache = Substitute.For<ICacheProvider>();
        cache.GetStringAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns((string?)null);
        cache.SetStringAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
             .Returns(Task.CompletedTask);
        cache.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
             .Returns(Task.CompletedTask);
        return new AgentSessionStore(cache, NullLogger<AgentSessionStore>.Instance);
    }

    private static AgentOrchestrator BuildOrchestrator(
        ILlmProvider provider,
        IMediator    mediator,
        int          maxIterations       = 5,
        int          maxTokensPerSession = 100_000)
    {
        var sessionStore = MakeSessionStore();
        var registry     = Substitute.For<IToolRegistry>();
        registry.ListAll(Arg.Any<string?>()).Returns([MakeDescriptor()]);

        var providerRouter = Substitute.For<IProviderRouter>();
        providerRouter.Select(Arg.Any<string?>(), Arg.Any<ToolDescriptor?>())
                      .Returns((provider, new ProviderOptions()));

        var opts = Options.Create(new LlmOptions
        {
            Budget = new BudgetOptions { MaxIterations = maxIterations, MaxTokensPerSession = maxTokensPerSession }
        });

        return new AgentOrchestrator(
            sessionStore, providerRouter, registry, new ToolSchemaConverter(),
            new ToolGuardFilter(opts, NullLogger<ToolGuardFilter>.Instance),
            new AgentScopeEnforcer(),
            new AgentScopeClassifier(opts, NullLogger<AgentScopeClassifier>.Instance),
            mediator, opts, NullLogger<AgentOrchestrator>.Instance);
    }

    [Fact]
    public async Task EndTurn_ReturnsSuccess_WithReply()
    {
        var provider = Substitute.For<ILlmProvider>();
        provider.CompleteAsync(
                Arg.Any<IReadOnlyList<LlmMessage>>(),
                Arg.Any<IReadOnlyList<LlmToolDefinition>>(),
                Arg.Any<ProviderOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(new LlmResponse(StopReason.EndTurn, "The answer is 42.", null, SmallUsage));

        var mediator = Substitute.For<IMediator>();
        var orch     = BuildOrchestrator(provider, mediator);

        var result = await orch.RunAsync(Guid.NewGuid(), "acme", "user1", "what is 6 x 7?", null, null, default);

        result.Success.Should().BeTrue();
        result.Reply.Should().Be("The answer is 42.");
    }

    [Fact]
    public async Task ToolUse_Then_EndTurn_ReturnsTool_AndReply()
    {
        var provider = Substitute.For<ILlmProvider>();
        var callId   = "call_001";
        var toolArgs = JsonDocument.Parse("{\"a\":6,\"b\":7,\"operator\":\"multiply\"}").RootElement;

        provider.CompleteAsync(
                Arg.Any<IReadOnlyList<LlmMessage>>(),
                Arg.Any<IReadOnlyList<LlmToolDefinition>>(),
                Arg.Any<ProviderOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(
                new LlmResponse(StopReason.ToolUse,  null, new LlmToolCall(callId, "math__calculate", toolArgs), SmallUsage),
                new LlmResponse(StopReason.EndTurn, "6 × 7 = 42.", null, SmallUsage));

        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<IRequest<ToolResponse<JsonElement>>>(), Arg.Any<CancellationToken>())
                .Returns(ToolResponse<JsonElement>.Ok(Guid.NewGuid(), JsonDocument.Parse("{\"result\":42}").RootElement));

        var orch   = BuildOrchestrator(provider, mediator);
        var result = await orch.RunAsync(Guid.NewGuid(), "acme", "user1", "what is 6 x 7?", null, null, default);

        result.Success.Should().BeTrue();
        result.ToolInvoked.Should().Be("math.calculate");
        result.Reply.Should().Be("6 × 7 = 42.");
    }

    [Fact]
    public async Task MaxIterations_Returns_MaxIterationsExceeded()
    {
        // Provider always returns ToolUse — never EndTurn → should hit iteration limit.
        var provider = Substitute.For<ILlmProvider>();
        var callId   = "call_loop";
        var toolArgs = JsonDocument.Parse("{}").RootElement;

        provider.CompleteAsync(
                Arg.Any<IReadOnlyList<LlmMessage>>(),
                Arg.Any<IReadOnlyList<LlmToolDefinition>>(),
                Arg.Any<ProviderOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(new LlmResponse(StopReason.ToolUse, null, new LlmToolCall(callId, "math__calculate", toolArgs), SmallUsage));

        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<IRequest<ToolResponse<JsonElement>>>(), Arg.Any<CancellationToken>())
                .Returns(ToolResponse<JsonElement>.Ok(Guid.NewGuid(), EmptyJson));

        var orch   = BuildOrchestrator(provider, mediator, maxIterations: 3);
        var result = await orch.RunAsync(Guid.NewGuid(), "acme", "user1", "loop forever", null, null, default);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("iterations");
    }

    [Fact]
    public async Task BudgetExceeded_Returns_BudgetError()
    {
        // Provider returns a lot of tokens — exceeds the 1-token session budget on the second
        // iteration's pre-flight check (budget gate runs BEFORE the LLM call each iteration).
        var provider = Substitute.For<ILlmProvider>();
        provider.CompleteAsync(
                Arg.Any<IReadOnlyList<LlmMessage>>(),
                Arg.Any<IReadOnlyList<LlmToolDefinition>>(),
                Arg.Any<ProviderOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(
                new LlmResponse(StopReason.ToolUse, null, new LlmToolCall("c1", "math__calculate", EmptyJson), new LlmUsage(50_001, 0)),
                new LlmResponse(StopReason.EndTurn, "done", null, SmallUsage));

        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<IRequest<ToolResponse<JsonElement>>>(), Arg.Any<CancellationToken>())
                .Returns(ToolResponse<JsonElement>.Ok(Guid.NewGuid(), EmptyJson));

        // maxTokensPerSession = 1 → after first ToolUse records 50_001 tokens the gate
        // fires on iteration 2, returning BudgetExceeded.
        var orch   = BuildOrchestrator(provider, mediator, maxTokensPerSession: 1);
        var result = await orch.RunAsync(Guid.NewGuid(), "acme", "user1", "text", null, null, default);

        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("budget");
    }

    [Fact]
    public async Task ProviderError_Returns_Failure()
    {
        var provider = Substitute.For<ILlmProvider>();
        provider.CompleteAsync(
                Arg.Any<IReadOnlyList<LlmMessage>>(),
                Arg.Any<IReadOnlyList<LlmToolDefinition>>(),
                Arg.Any<ProviderOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(new LlmResponse(StopReason.Error, null, null, LlmUsage.Zero, "API key missing"));

        var mediator = Substitute.For<IMediator>();
        var orch     = BuildOrchestrator(provider, mediator);
        var result   = await orch.RunAsync(Guid.NewGuid(), "acme", "user1", "text", null, null, default);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("API key missing");
    }

    [Fact]
    public async Task CallerType_IsAlways_AiAgent_OnToolInvocations()
    {
        var provider = Substitute.For<ILlmProvider>();
        var callId   = "call_007";
        var toolArgs = JsonDocument.Parse("{}").RootElement;

        provider.CompleteAsync(
                Arg.Any<IReadOnlyList<LlmMessage>>(),
                Arg.Any<IReadOnlyList<LlmToolDefinition>>(),
                Arg.Any<ProviderOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(
                new LlmResponse(StopReason.ToolUse,  null, new LlmToolCall(callId, "math__calculate", toolArgs), SmallUsage),
                new LlmResponse(StopReason.EndTurn,  "done", null, SmallUsage));

        ToolEngine.Application.Commands.ExecuteToolCommand<JsonElement, JsonElement>? capturedCmd = null;

        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<IRequest<ToolResponse<JsonElement>>>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    capturedCmd = ci.Arg<ToolEngine.Application.Commands.ExecuteToolCommand<JsonElement, JsonElement>>();
                    return ToolResponse<JsonElement>.Ok(Guid.NewGuid(), EmptyJson);
                });

        var orch = BuildOrchestrator(provider, mediator);
        await orch.RunAsync(Guid.NewGuid(), "acme", "user1", "test", null, null, default);

        capturedCmd.Should().NotBeNull();
        capturedCmd!.CallerType.Should().Be(CallerType.AiAgent);
    }

    [Fact]
    public async Task GovernanceMetadataJson_ContainsProviderAndSession()
    {
        var provider = Substitute.For<ILlmProvider>();
        provider.ProviderName.Returns("anthropic");
        var callId   = "call_gov";
        var toolArgs = JsonDocument.Parse("{}").RootElement;

        provider.CompleteAsync(
                Arg.Any<IReadOnlyList<LlmMessage>>(),
                Arg.Any<IReadOnlyList<LlmToolDefinition>>(),
                Arg.Any<ProviderOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(
                new LlmResponse(StopReason.ToolUse,  null, new LlmToolCall(callId, "math__calculate", toolArgs), SmallUsage),
                new LlmResponse(StopReason.EndTurn,  "done", null, SmallUsage));

        ToolEngine.Application.Commands.ExecuteToolCommand<JsonElement, JsonElement>? capturedCmd = null;
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<IRequest<ToolResponse<JsonElement>>>(), Arg.Any<CancellationToken>())
                .Returns(ci =>
                {
                    capturedCmd = ci.Arg<ToolEngine.Application.Commands.ExecuteToolCommand<JsonElement, JsonElement>>();
                    return ToolResponse<JsonElement>.Ok(Guid.NewGuid(), EmptyJson);
                });

        var orch = BuildOrchestrator(provider, mediator);
        await orch.RunAsync(Guid.NewGuid(), "acme", "user1", "test", null, null, default);

        capturedCmd.Should().NotBeNull();
        capturedCmd!.GovernanceMetadataJson.Should().NotBeNull();
        capturedCmd.GovernanceMetadataJson.Should().Contain("sessionId");
    }
}
