using Mes.Domain.Identity;

namespace Mes.Domain.Execution;

// 订单下达时冻结的自包含执行依据，确保后续模板变化不会改变在制品应执行的规则。
public sealed class ProductionOrderExecutionSnapshot
{
    public Guid Id { get; init; }

    public Guid ProductionOrderId { get; init; }

    public ProductionOrder? ProductionOrder { get; init; }

    public Guid SourceTemplateId { get; init; }

    public ProductExecutionTemplateVersion? SourceTemplate { get; init; }

    public required string SnapshotVersion { get; init; }

    public required string DefinitionJson { get; init; }

    public required string DefinitionHash { get; init; }

    public required string DefinitionHashAlgorithm { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public Guid CreatedByUserId { get; init; }

    public UserAccount? CreatedByUser { get; init; }
}
