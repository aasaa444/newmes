namespace Mes.Api.Execution;

/// <summary>
/// 生产工单状态（CONTEXT 工单状态第一期）。
/// Draft → Released → InProcess → Completed → Closed；
/// Released 且无在制 SN 时可 Cancelled。第一期不做 Pause。
/// InProcess 由票 04 首站过站推进；本票创建后最多到 Released。
/// </summary>
public enum WorkOrderStatus
{
    Draft = 0,
    Released = 1,
    InProcess = 2,
    Completed = 3,
    Closed = 4,
    Cancelled = 5
}
