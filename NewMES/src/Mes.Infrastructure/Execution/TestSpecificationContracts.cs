namespace Mes.Infrastructure.Execution;

// 测试规范契约区分草稿定义与批准动作，确保制订者不能用一次请求绕过审批证据。
public sealed record TestSpecificationDraftRequest(
    string? Code,
    string? Version,
    string? MaterialCode,
    string? OperationCode,
    string? Applicability,
    IReadOnlyList<TestSpecificationItemDefinition>? Items);

public sealed record TestSpecificationItemDefinition(
    string? Code,
    string? Name,
    string? DataType,
    bool Required,
    string? Unit = null,
    int? DecimalPlaces = null,
    decimal? LowerLimit = null,
    decimal? UpperLimit = null,
    string? ExpectedText = null,
    bool? ExpectedBoolean = null);

public sealed record TestSpecificationApprovalRequest(string? ApprovalEvidenceReference);

public sealed record TestSpecificationVersionView(
    Guid SpecificationId,
    string Code,
    string Version,
    string MaterialCode,
    string OperationCode,
    string Applicability,
    string Status,
    string DefinitionHash,
    IReadOnlyList<TestSpecificationItemDefinition> Items,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ApprovedAtUtc,
    string? ApprovedByUsername,
    string? ApprovalEvidenceReference,
    string CorrelationId);

public sealed class TestSpecificationRejectedException(
    string code,
    string message,
    int httpStatusCode) : Exception(message)
{
    public string Code { get; } = code;

    public int HttpStatusCode { get; } = httpStatusCode;
}
