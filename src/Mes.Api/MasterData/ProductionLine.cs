namespace Mes.Api.MasterData;

public class ProductionLine
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public List<WorkStation> Stations { get; set; } = [];
}

/// <summary>工位 — physical/logical station bound to exactly one 工序 (ProcessStep).</summary>
public class WorkStation
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid ProductionLineId { get; set; }
    public ProductionLine? ProductionLine { get; set; }
    public Guid BoundProcessStepId { get; set; }
    public ProcessStep? BoundProcessStep { get; set; }
    public bool IsActive { get; set; } = true;
}
