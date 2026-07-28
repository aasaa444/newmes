namespace Mes.Api.Execution;

/// <summary>
/// 成品序列号（在制个体）。首站过站时创建或扫入并挂到生产工单。
/// 当前工序 = CurrentProcessStepId；与工位绑定工序一致才可过站（防跳站）。
/// </summary>
public class ProductSerial
{
    public Guid Id { get; set; }

    /// <summary>业务 SN 字符串，全局唯一（未关闭工单范围内不可重复挂单）。</summary>
    public string SerialNo { get; set; } = string.Empty;

    public Guid WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    /// <summary>当前应执行的工序；首站通过后指向下一序，末站通过后可空表示路线完成待入库。</summary>
    public Guid? CurrentProcessStepId { get; set; }

    public ProcessStepStatus Status { get; set; } = ProcessStepStatus.InProcess;

    public DateTimeOffset CreatedAt { get; set; }

    public List<SerialPassRecord> PassRecords { get; set; } = [];
    public List<ComponentBinding> ComponentBindings { get; set; } = [];
}

/// <summary>
/// 个体状态：InProcess 可过站；Isolated 须放行；Scrapped 终态；
/// RouteCompleted 待入库（票 06），隔离品不得当合格入库。
/// </summary>
public enum ProcessStepStatus
{
    InProcess = 0,
    RouteCompleted = 1,
    Scrapped = 2,
    Isolated = 3
}
