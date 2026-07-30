namespace Mes.Domain.MasterData;

// MES 使用的物料主数据投影；编码和基础单位来自权威主数据，不能由车间执行过程猜测。
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
