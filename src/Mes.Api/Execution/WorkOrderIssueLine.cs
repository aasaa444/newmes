using Mes.Api.MasterData;

namespace Mes.Api.Execution;

/// <summary>
/// 工单用料台账行：一次领料确认后按物料汇总（可多次领料累加数量）。
/// 非关键件：领料即已耗（ConsumedQty = IssuedQty）。
/// 关键件：领料进入待耗（PendingQty），绑定 SN 后转已耗（票 04）。
/// </summary>
public class WorkOrderIssueLine
{
    public Guid Id { get; set; }
    public Guid WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    public Guid MaterialId { get; set; }
    public Material? Material { get; set; }

    /// <summary>已从线边发到本工单的累计数量。</summary>
    public decimal IssuedQty { get; set; }

    /// <summary>待耗：关键件已领未绑 SN 的数量。</summary>
    public decimal PendingQty { get; set; }

    /// <summary>已耗：非关键件领料即计入；关键件绑定后计入。</summary>
    public decimal ConsumedQty { get; set; }
}
