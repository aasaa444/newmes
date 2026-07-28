namespace Mes.Api.MasterData;

/// <summary>线性工艺路线 template for a finished material.</summary>
public class ProcessRoute
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid FinishedMaterialId { get; set; }
    public Material? FinishedMaterial { get; set; }
    public string Version { get; set; } = "1";
    public bool IsActive { get; set; } = true;
    public List<ProcessStep> Steps { get; set; } = [];
}

/// <summary>工序 — ordered step on a process route.</summary>
public class ProcessStep
{
    public Guid Id { get; set; }
    public Guid ProcessRouteId { get; set; }
    public ProcessRoute? ProcessRoute { get; set; }
    public int Sequence { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsQualityStep { get; set; }
    /// <summary>On fail, rework back to this sequence (optional).</summary>
    public int? ReworkToSequence { get; set; }
}
