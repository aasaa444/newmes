using Mes.Api.MasterData;

namespace Mes.Api.Execution;

/// <summary>
/// 过站履历：某 SN 在某工序的一次登记结果（谱系的时间轴节点）。
/// 不合格/返工历史不删除（票 05）；本票以 Pass 为主。
/// </summary>
public class SerialPassRecord
{
    public Guid Id { get; set; }
    public Guid ProductSerialId { get; set; }
    public ProductSerial? ProductSerial { get; set; }

    public Guid ProcessStepId { get; set; }
    public ProcessStep? ProcessStep { get; set; }

    public Guid? WorkStationId { get; set; }
    public WorkStation? WorkStation { get; set; }

    /// <summary>Pass / Fail（票 05 扩展）。</summary>
    public string Result { get; set; } = "Pass";

    public string? OperatorUserName { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? Remark { get; set; }
}
