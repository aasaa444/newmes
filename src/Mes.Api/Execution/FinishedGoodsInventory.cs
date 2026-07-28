using Mes.Api.MasterData;

namespace Mes.Api.Execution;

/// <summary>
/// 成品库存账（与线边、在制分账）。仅合格完工入库增加；隔离/报废不得入此账。
/// 第一期按物料汇总数量；SN 级通过入库记录/谱系查询。
/// </summary>
public class FinishedGoodsInventory
{
    public Guid Id { get; set; }
    public Guid MaterialId { get; set; }
    public Material? Material { get; set; }
    public decimal QuantityOnHand { get; set; }
}
