namespace Mes.Domain.Execution;

public sealed class ControlledIdentifier
{
    public Guid Id { get; init; }

    public Guid ProductIdentityId { get; init; }

    public ProductIdentity? ProductIdentity { get; init; }

    public ControlledIdentifierType Type { get; init; }

    public required string Value { get; init; }

    public IdentitySourceType SourceType { get; init; }

    public required string SourceSystem { get; init; }

    public required string SourceReference { get; init; }

    public bool IsDemo { get; init; }
}
