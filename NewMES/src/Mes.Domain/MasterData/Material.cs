namespace Mes.Domain.MasterData;

public sealed class Material
{
    public Guid Id { get; init; }

    public required string Code { get; init; }

    public required string Name { get; init; }

    public TraceabilityMode TraceabilityMode { get; init; }

    public bool IsActive { get; set; }
}
