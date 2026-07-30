namespace Mes.Infrastructure.Execution;

// 本文件定义工艺执行模板的发布契约。模板把产品、BOM、工艺路线、追溯、固件和测试要求汇总为可版本化的执行依据。
public sealed record ExecutionTemplatePublishRequest(
    string MaterialCode,
    string ProductVersion,
    string Version,
    string Applicability,
    BomDefinition Bom,
    RouteDefinition Route,
    TraceabilityPolicyDefinition TraceabilityPolicy,
    IdentityPolicyDefinition IdentityPolicy,
    IReadOnlyList<FirmwareRequirementDefinition> FirmwareRequirements,
    IReadOnlyList<TestSpecificationReferenceDefinition> TestSpecifications,
    CompletionGateDefinition CompletionGate);

public sealed record BomDefinition(
    string Version,
    IReadOnlyList<BomComponentDefinition> Components);

public sealed record BomComponentDefinition(
    string MaterialCode,
    decimal QuantityPer,
    string Unit,
    string TraceabilityMode,
    string? AssemblyOperationCode = null,
    string? ConsumptionRule = null);

public sealed record RouteDefinition(
    string Version,
    IReadOnlyList<RouteOperationDefinition> Operations);

public sealed record RouteOperationDefinition(int Sequence, string Code, string Name);

public sealed record TraceabilityPolicyDefinition(string Version, string FinishedProductMode);

public sealed record IdentityPolicyDefinition(
    string Version,
    string FinishedSerialSource,
    IReadOnlyList<string> RequiredIdentifiers,
    string AllocationTiming = "BeforeStartWip");

public sealed record FirmwareRequirementDefinition(
    string Code,
    string Version,
    bool Required,
    string EvidenceReference,
    string? OperationCode = null,
    string? ConfigurationPackage = null,
    string? ChecksumAlgorithm = null,
    string? ExpectedChecksum = null);

public sealed record TestSpecificationReferenceDefinition(
    string Code,
    string Version,
    bool Required,
    string EvidenceReference,
    string? OperationCode = null,
    string? MaterialCode = null,
    string? DefinitionHash = null,
    string? DefinitionHashAlgorithm = null,
    IReadOnlyList<TestSpecificationItemDefinition>? Items = null);

public sealed record CompletionGateDefinition(
    string Version,
    IReadOnlyList<string> Requirements);

public sealed record ExecutionTemplateDefinition(
    ProductDefinition Product,
    string Applicability,
    BomDefinition Bom,
    RouteDefinition Route,
    TraceabilityPolicyDefinition TraceabilityPolicy,
    IdentityPolicyDefinition IdentityPolicy,
    IReadOnlyList<FirmwareRequirementDefinition> FirmwareRequirements,
    IReadOnlyList<TestSpecificationReferenceDefinition> TestSpecifications,
    CompletionGateDefinition CompletionGate);

public sealed record ProductDefinition(
    string MaterialCode,
    string MaterialName,
    string SourceVersion,
    string TraceabilityMode);

public sealed record ExecutionTemplatePublishResult(
    Guid TemplateId,
    string MaterialCode,
    string Version,
    string Status,
    string DefinitionHash);

public sealed class ExecutionTemplateRejectedException(
    string code,
    string message,
    int httpStatusCode) : Exception(message)
{
    public string Code { get; } = code;

    public int HttpStatusCode { get; } = httpStatusCode;
}
