namespace Mes.Infrastructure.Execution;

// 本文件定义测试执行输入、逐项测量结果、工位视图和谱系视图，不承载状态变更逻辑。
public sealed record TestRunRequest(
    string? SourceSystem,
    string? IdempotencyKey,
    string? SpecificationCode,
    string? DeviceId,
    string? DeviceVersion,
    string? FixtureId,
    string? FixtureVersion,
    string? RawReportReference,
    Guid? RetryOfTestRunId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    string? Location,
    IReadOnlyList<TestMeasurementRequest>? Measurements);

public sealed record TestMeasurementRequest(
    string? ItemCode,
    string? RawValue,
    string? Unit,
    string? DiagnosticCode = null,
    string? DiagnosticMessage = null);

public sealed record TestMeasurementView(
    Guid MeasurementId,
    string ItemCode,
    string ItemName,
    string DataType,
    bool Required,
    string RawValue,
    string? Unit,
    int? DecimalPlaces,
    decimal? LowerLimit,
    decimal? UpperLimit,
    string? ExpectedText,
    bool? ExpectedBoolean,
    decimal? NumericValue,
    bool? BooleanValue,
    string Result,
    string? DiagnosticCode,
    string? DiagnosticMessage,
    string? ReportedDiagnosticCode,
    string? ReportedDiagnosticMessage);

public sealed record TestRunView(
    Guid TestRunId,
    Guid ProductIdentityId,
    string FinishedSerialNumber,
    Guid ProductionOrderId,
    Guid ExecutionSnapshotId,
    string SpecificationCode,
    string SpecificationVersion,
    string SpecificationDefinitionHash,
    string OperationCode,
    string DeviceId,
    string DeviceVersion,
    string FixtureId,
    string FixtureVersion,
    string RawReportReference,
    string Result,
    string? DiagnosticCode,
    string? DiagnosticMessage,
    Guid? RetryOfTestRunId,
    Guid ManufacturingEventId,
    Guid ActorUserId,
    string ActorUsername,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    DateTimeOffset RecordedAtUtc,
    string Location,
    string CorrelationId,
    bool OperationCompleted,
    string? NextOperationCode,
    bool IsReplay,
    IReadOnlyList<TestMeasurementView> Measurements);

public sealed record TestSpecificationWorkstationView(
    string Code,
    string Version,
    string OperationCode,
    bool Required,
    string EvidenceReference,
    string DefinitionHash,
    string Status,
    IReadOnlyList<TestSpecificationItemDefinition> Items,
    IReadOnlyList<TestRunView> Runs);

public sealed record TestWorkstationView(
    Guid ProductIdentityId,
    string FinishedSerialNumber,
    string? CurrentOperationCode,
    IReadOnlyList<TestSpecificationWorkstationView> Specifications);

public sealed record TestGenealogyView(
    Guid ProductIdentityId,
    string FinishedSerialNumber,
    Guid ProductionOrderId,
    IReadOnlyList<TestRunView> Runs);

public sealed class TestRunRejectedException(
    string code,
    string message,
    int httpStatusCode) : Exception(message)
{
    public string Code { get; } = code;

    public int HttpStatusCode { get; } = httpStatusCode;
}
