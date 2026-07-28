using Mes.Api.MasterData;

namespace Mes.Api.Execution;

/// <summary>
/// 关键件 SN 绑定到成品 SN（谱系组装关系）。
/// 绑定时从工单用料 Pending 转为 Consumed。
/// </summary>
public class ComponentBinding
{
    public Guid Id { get; set; }
    public Guid ProductSerialId { get; set; }
    public ProductSerial? ProductSerial { get; set; }

    public Guid ComponentMaterialId { get; set; }
    public Material? ComponentMaterial { get; set; }

    /// <summary>关键件个体序列号。</summary>
    public string ComponentSerialNo { get; set; } = string.Empty;

    public Guid? ProcessStepId { get; set; }
    public DateTimeOffset BoundAt { get; set; }
    public string? OperatorUserName { get; set; }
}
