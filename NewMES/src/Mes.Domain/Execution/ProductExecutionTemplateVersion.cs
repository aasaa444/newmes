using Mes.Domain.Identity;
using Mes.Domain.MasterData;

namespace Mes.Domain.Execution;

public sealed class ProductExecutionTemplateVersion
{
    public Guid Id { get; init; }

    public Guid MaterialId { get; init; }

    public Material? Material { get; init; }

    public required string Version { get; init; }

    public required string Applicability { get; init; }

    public required string DefinitionJson { get; init; }

    public required string DefinitionHash { get; init; }

    public required string DefinitionHashAlgorithm { get; init; }

    public bool IsApproved { get; init; }

    public DateTimeOffset PublishedAtUtc { get; init; }

    public Guid PublishedByUserId { get; init; }

    public UserAccount? PublishedByUser { get; init; }
}
