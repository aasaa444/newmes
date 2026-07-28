namespace Mes.Api.MasterData;

/// <summary>单层 BOM header for one finished material.</summary>
public class Bom
{
    public Guid Id { get; set; }
    public Guid FinishedMaterialId { get; set; }
    public Material? FinishedMaterial { get; set; }
    public string Version { get; set; } = "A";
    public bool IsActive { get; set; } = true;
    public List<BomLine> Lines { get; set; } = [];
}

public class BomLine
{
    public Guid Id { get; set; }
    public Guid BomId { get; set; }
    public Bom? Bom { get; set; }
    public Guid ComponentMaterialId { get; set; }
    public Material? ComponentMaterial { get; set; }
    /// <summary>Quantity per one finished unit.</summary>
    public decimal QuantityPer { get; set; }
}
