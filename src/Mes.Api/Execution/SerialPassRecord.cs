using Mes.Api.MasterData;

namespace Mes.Api.Execution;

/// <summary>
/// 过站/质量履历：谱系时间轴节点。Fail/Rework/Release/Scrap 只追加不覆盖删除。
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

    /// <summary>Pass | Fail | Rework | Isolate(记在 Fail+状态) | Release | Scrap。</summary>
    public string Result { get; set; } = "Pass";

    public string? OperatorUserName { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? Remark { get; set; }
}
