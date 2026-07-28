using Mes.Api.MasterData;

namespace Mes.Api.Execution;

/// <summary>
/// 线边库存账：产线旁可用数量（与成品仓、在制分账）。
/// 收料增加、领料减少。第一期不做多库位 WMS。
/// </summary>
public class LineSideInventory
{
    public Guid Id { get; set; }
    public Guid MaterialId { get; set; }
    public Material? Material { get; set; }

    /// <summary>当前可用数量。</summary>
    public decimal QuantityOnHand { get; set; }
}
