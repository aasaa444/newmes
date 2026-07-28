namespace Mes.Api.MasterData;

/// <summary>物料 — finished goods, key components, or bulk components.</summary>
public class Material
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsFinishedGood { get; set; }
    /// <summary>关键件 — must collect SN and bind into genealogy.</summary>
    public bool IsKeyComponent { get; set; }
    /// <summary>执行期属性: whether SN capture is required.</summary>
    public bool RequiresSerialNumber { get; set; }
    public bool IsActive { get; set; } = true;
}
