namespace Mes.Api.Integration;

/// <summary>
/// ERP 回写出站消息（模拟器用友/金蝶风格报文，不连真 ERP）。
/// 计划端可查询，面试与试点演示集成边界。
/// </summary>
public class ErpOutboxMessage
{
    public Guid Id { get; set; }

    /// <summary>业务类型：MaterialIssue | ProductionReceipt | WorkOrderClose</summary>
    public string MessageType { get; set; } = string.Empty;

    /// <summary>关联工单号等业务键。</summary>
    public string? BusinessKey { get; set; }

    /// <summary>JSON 报文正文（用友/金蝶风格字段）。</summary>
    public string PayloadJson { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>模拟状态：Pending | SimulatedSent</summary>
    public string Status { get; set; } = "SimulatedSent";
}
