namespace Mes.Infrastructure.Execution;

// 本文件定义固件配置执行、工位展示和产品谱系查询契约；版本和摘要用于证明实际烧录内容。
public sealed record FirmwareExecutionRequest(
    string? SourceSystem,
    string? IdempotencyKey,
    string? RequirementCode,
    string? ActualVersion,
    string? ConfigurationPackage,
    string? ChecksumAlgorithm,
    string? ChecksumValue,
    string? ToolId,
    string? ToolVersion,
    string? Result,
    string? DiagnosticCode,
    string? DiagnosticMessage,
    Guid? RetryOfExecutionId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndedAtUtc,
    string? Location);

public sealed record FirmwareExecutionResultView(
    Guid ExecutionId,
    Guid ProductIdentityId,
    string FinishedSerialNumber,
    string RequirementCode,
    string RequiredVersion,
    string ActualVersion,
    string RequiredConfigurationPackage,
    string ActualConfigurationPackage,
    string RequiredChecksumAlgorithm,
    string ActualChecksumAlgorithm,
    string ExpectedChecksum,
    string ActualChecksum,
    string ToolId,
    string ToolVersion,
    string Result,
    string? DiagnosticCode,
    string? DiagnosticMessage,
    string? ReportedDiagnosticCode,
    string? ReportedDiagnosticMessage,
    Guid? RetryOfExecutionId,
    Guid ProductionOrderId,
    Guid ExecutionSnapshotId,
    string OperationCode,
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
    bool IsReplay);

public sealed record FirmwareRequirementView(
    string Code,
    string OperationCode,
    string RequiredVersion,
    string RequiredConfigurationPackage,
    string ChecksumAlgorithm,
    string ExpectedChecksum,
    bool Required,
    string Status,
    IReadOnlyList<FirmwareExecutionResultView> Executions);

public sealed record FirmwareWorkstationResult(
    Guid ProductIdentityId,
    string FinishedSerialNumber,
    string? CurrentOperationCode,
    IReadOnlyList<FirmwareRequirementView> Requirements);

public sealed record FirmwareGenealogyResult(
    Guid ProductIdentityId,
    string FinishedSerialNumber,
    Guid ProductionOrderId,
    IReadOnlyList<FirmwareExecutionResultView> Executions);

public sealed class FirmwareConfigurationRejectedException(
    string code,
    string message,
    int httpStatusCode) : Exception(message)
{
    public string Code { get; } = code;

    public int HttpStatusCode { get; } = httpStatusCode;
}
