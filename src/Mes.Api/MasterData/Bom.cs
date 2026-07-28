namespace Mes.Api.MasterData;

/// <summary>
/// 单层 BOM 头：一个成品物料对应一套组件清单（第一期不递归半成品）。
/// </summary>
public class Bom
{
    public Guid Id { get; set; }
    public Guid FinishedMaterialId { get; set; }
    public Material? FinishedMaterial { get; set; }

    /// <summary>BOM 版本号，如 A；工单日后可冻结某一版。</summary>
    public string Version { get; set; } = "A";

    public bool IsActive { get; set; } = true;
    public List<BomLine> Lines { get; set; } = [];
}

/// <summary>BOM 行：每做成 1 个成品需要 Component 的 QuantityPer。</summary>
public class BomLine
{
    public Guid Id { get; set; }
    public Guid BomId { get; set; }
    public Bom? Bom { get; set; }
    public Guid ComponentMaterialId { get; set; }
    public Material? ComponentMaterial { get; set; }

    /// <summary>单位用量（相对 1 件成品）。</summary>
    public decimal QuantityPer { get; set; }
}
