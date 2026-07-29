using Mes.Domain.Identity;

namespace Mes.Domain.Execution;

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
