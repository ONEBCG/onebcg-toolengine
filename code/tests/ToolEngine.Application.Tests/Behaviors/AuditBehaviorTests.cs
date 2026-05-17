namespace ToolEngine.Application.Tests.Behaviors;

using FluentAssertions;
using NSubstitute;
using ToolEngine.Application.Behaviors;
using ToolEngine.Application.Tests.Helpers;
using ToolEngine.Core.Abstractions.Common;
using ToolEngine.Core.Abstractions.Persistence;
using ToolEngine.Core.Domain.Contracts;
using ToolEngine.Core.Domain.Entities;
using ToolEngine.Core.Domain.Enums;
using Xunit;

public sealed class AuditBehaviorTests
{
    private readonly IUnitOfWork                              _uow;
    private readonly IRepository<ToolInvocationRecord, Guid> _auditRepo;
    private readonly IRepository<ToolInvocationEvent, Guid>  _eventRepo;
    private readonly IDateTimeProvider                        _clock = new FakeClock();

    private ToolInvocationRecord? _capturedRecord;
    private readonly List<ToolInvocationEvent> _capturedEvents = new();

    public AuditBehaviorTests()
    {
        _uow       = Substitute.For<IUnitOfWork>();
        _auditRepo = Substitute.For<IRepository<ToolInvocationRecord, Guid>>();
        _eventRepo = Substitute.For<IRepository<ToolInvocationEvent, Guid>>();

        // Wire up capture + completion for every test
        _auditRepo
            .AddAsync(Arg.Do<ToolInvocationRecord>(r => _capturedRecord = r), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _eventRepo
            .AddAsync(Arg.Do<ToolInvocationEvent>(e => _capturedEvents.Add(e)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(0));
    }

    private AuditBehavior<
        ToolEngine.Application.Commands.ExecuteToolCommand<object, object>,
        ToolResponse<object>> CreateBehavior() =>
        new(_uow, _auditRepo, _eventRepo, _clock);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandlerSucceeds_RecordIsMarkedSucceeded()
    {
        var cmd      = new CommandBuilder().Build();
        var behavior = CreateBehavior();

        await behavior.Handle(
            cmd,
            FakeDelegates.SuccessResponse(cmd.CorrelationId),
            CancellationToken.None);

        _capturedRecord.Should().NotBeNull();
        _capturedRecord!.Status.Should().Be(ToolStatus.Succeeded);
    }

    [Fact]
    public async Task HandlerReturnsFailure_RecordIsMarkedFailed()
    {
        var cmd      = new CommandBuilder().Build();
        var behavior = CreateBehavior();

        await behavior.Handle(
            cmd,
            FakeDelegates.FailResponse(cmd.CorrelationId, "SOME_ERROR"),
            CancellationToken.None);

        _capturedRecord.Should().NotBeNull();
        _capturedRecord!.Status.Should().Be(ToolStatus.Failed);
    }

    [Fact]
    public async Task HandlerThrowsException_RecordMarkedFailed_ExceptionRethrown()
    {
        var cmd      = new CommandBuilder().Build();
        var behavior = CreateBehavior();

        var act = () => behavior.Handle(
            cmd,
            FakeDelegates.Fail<ToolResponse<object>>("boom"),
            CancellationToken.None);

        await act.Should().ThrowAsync<Exception>().WithMessage("boom");
        _capturedRecord.Should().NotBeNull();
        _capturedRecord!.Status.Should().Be(ToolStatus.Failed);
    }

    [Fact]
    public async Task SuspendedResponse_EmitsSuspendedEvent()
    {
        var pendingId = Guid.NewGuid();
        var cmd       = new CommandBuilder().Build();
        var behavior  = CreateBehavior();

        await behavior.Handle(
            cmd,
            _ => Task.FromResult(ToolResponse<object>.Suspended(cmd.CorrelationId, pendingId)),
            CancellationToken.None);

        _capturedEvents.Should().Contain(e => e.EventType == InvocationEventType.Suspended);
    }

    [Fact]
    public async Task AlwaysEmitsInvokedEvent()
    {
        var cmd      = new CommandBuilder().Build();
        var behavior = CreateBehavior();

        await behavior.Handle(
            cmd,
            FakeDelegates.SuccessResponse(cmd.CorrelationId),
            CancellationToken.None);

        _capturedEvents.Should().NotBeEmpty();
        _capturedEvents[0].EventType.Should().Be(InvocationEventType.Invoked);
    }

    [Fact]
    public async Task SuccessEmitsSucceededEvent()
    {
        var cmd      = new CommandBuilder().Build();
        var behavior = CreateBehavior();

        await behavior.Handle(
            cmd,
            FakeDelegates.SuccessResponse(cmd.CorrelationId),
            CancellationToken.None);

        _capturedEvents.Should().Contain(e => e.EventType == InvocationEventType.Succeeded);
    }

    [Fact]
    public async Task FailureEmitsFailedEvent_WithErrorCode()
    {
        var cmd      = new CommandBuilder().Build();
        var behavior = CreateBehavior();

        await behavior.Handle(
            cmd,
            FakeDelegates.FailResponse(cmd.CorrelationId, "MY_ERROR_CODE"),
            CancellationToken.None);

        var failedEvent = _capturedEvents.FirstOrDefault(e => e.EventType == InvocationEventType.Failed);
        failedEvent.Should().NotBeNull();
        failedEvent!.ErrorCode.Should().Be("MY_ERROR_CODE");
    }

    [Fact]
    public async Task Record_ReceivesCallerType_AiAgent_FromCommand()
    {
        var cmd      = new CommandBuilder().WithCallerType(CallerType.AiAgent).Build();
        var behavior = CreateBehavior();

        await behavior.Handle(
            cmd,
            FakeDelegates.SuccessResponse(cmd.CorrelationId),
            CancellationToken.None);

        _capturedRecord.Should().NotBeNull();
        _capturedRecord!.CallerType.Should().Be(CallerType.AiAgent);
    }

    [Fact]
    public async Task Record_ReceivesGovernanceMetadataJson_FromCommand()
    {
        const string govJson = @"{""policy"":""iso42001"",""version"":""1.0""}";
        var cmd      = new CommandBuilder().WithGovernanceMetadataJson(govJson).Build();
        var behavior = CreateBehavior();

        await behavior.Handle(
            cmd,
            FakeDelegates.SuccessResponse(cmd.CorrelationId),
            CancellationToken.None);

        _capturedRecord.Should().NotBeNull();
        _capturedRecord!.GovernanceMetadataJson.Should().NotBeNullOrWhiteSpace();
        _capturedRecord.GovernanceMetadataJson.Should().Be(govJson);
    }

    [Fact]
    public async Task Events_ReceiveCallerType_FromCommand()
    {
        var cmd      = new CommandBuilder().WithCallerType(CallerType.AiAgent).Build();
        var behavior = CreateBehavior();

        await behavior.Handle(
            cmd,
            FakeDelegates.SuccessResponse(cmd.CorrelationId),
            CancellationToken.None);

        _capturedEvents.Should().NotBeEmpty();
        _capturedEvents.Should().AllSatisfy(e => e.CallerType.Should().Be(CallerType.AiAgent));
    }

    [Fact]
    public async Task NonToolCommand_NoRecordCreated()
    {
        var behavior = new AuditBehavior<string, ToolResponse<object>>(_uow, _auditRepo, _eventRepo, _clock);
        var corrId   = Guid.NewGuid();

        await behavior.Handle(
            "not-a-tool-command",
            _ => Task.FromResult(ToolResponse<object>.Ok(corrId, new object())),
            CancellationToken.None);

        await _auditRepo.DidNotReceive()
            .AddAsync(Arg.Any<ToolInvocationRecord>(), Arg.Any<CancellationToken>());
    }
}
