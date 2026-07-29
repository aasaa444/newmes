namespace Mes.Domain.MasterData;

public sealed class Material
{
    public Guid Id { get; init; }

    public required string Code { get; init; }

    public required string Name { get; init; }

    // Null after an upgrade means the legacy base unit cannot be proven.
    public string? BaseUnit { get; init; }

    public TraceabilityMode TraceabilityMode { get; init; }

    public bool IsActive { get; set; }
}
