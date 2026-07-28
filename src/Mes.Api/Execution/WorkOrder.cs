using Mes.Api.MasterData;

namespace Mes.Api.Execution;

/// <summary>
/// 生产工单：计划做多少件某成品（一工单多件，不与单个 SN 一一对应）。
/// 下达时冻结工艺路线 Id/版本与 BOM 快照引用，避免在制中途改模板。
/// </summary>
public class WorkOrder
{
    public Guid Id { get; set; }

    /// <summary>工单号，业务可读，如 WO-20260728-0001。</summary>
    public string OrderNo { get; set; } = string.Empty;

    public Guid FinishedMaterialId { get; set; }
    public Material? FinishedMaterial { get; set; }

    /// <summary>计划数量 N。</summary>
    public decimal PlannedQty { get; set; }

    /// <summary>合格完工数（票 06 累加；本票保持 0）。</summary>
    public decimal CompletedQty { get; set; }

    /// <summary>报废数（票 05 累加）。</summary>
    public decimal ScrappedQty { get; set; }

    public WorkOrderStatus Status { get; set; } = WorkOrderStatus.Draft;

    /// <summary>下达时选定的工艺路线；草稿可空，下达后必填并冻结。</summary>
    public Guid? ProcessRouteId { get; set; }
    public ProcessRoute? ProcessRoute { get; set; }

    /// <summary>冻结的路线版本字符串（冗余，便于列表展示）。</summary>
    public string? FrozenRouteVersion { get; set; }

    /// <summary>下达时选用的 BOM；领料按此展开。</summary>
    public Guid? BomId { get; set; }
    public Bom? Bom { get; set; }

    public string? FrozenBomVersion { get; set; }

    /// <summary>
    /// 在制 SN 个数。首站过站 +1；报废 -1；取消时必须为 0。
    /// </summary>
    public int InProcessSerialCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }

    public List<WorkOrderIssueLine> IssueLines { get; set; } = [];
}
