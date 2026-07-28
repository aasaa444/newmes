namespace Mes.Api.MasterData;

/// <summary>
/// 物料主数据（CONTEXT：物料 + 执行期属性）。
/// 成品 / 关键件 / 辅料都用同一实体，用布尔标记区分行为。
/// </summary>
public class Material
{
    public Guid Id { get; set; }

    /// <summary>业务编码，唯一，如 FG-ROUTER、PCB-MAIN。</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>是否成品（可挂 BOM / 工艺路线、产出 SN）。</summary>
    public bool IsFinishedGood { get; set; }

    /// <summary>关键件：过站须采集 SN 并进入谱系。</summary>
    public bool IsKeyComponent { get; set; }

    /// <summary>是否要求序列号（成品或关键件通常为 true）。</summary>
    public bool RequiresSerialNumber { get; set; }

    /// <summary>软删：false 表示停用，不物理删除以免破坏历史引用。</summary>
    public bool IsActive { get; set; } = true;
}
