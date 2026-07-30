namespace Mes.Domain.MasterData;

// 物料要求的追溯粒度，决定执行时必须采集序列号、批次，还是只记录数量。
public enum TraceabilityMode
{
    None,
    Lot,
    Serial,
}
