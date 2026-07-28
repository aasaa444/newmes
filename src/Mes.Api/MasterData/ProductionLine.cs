namespace Mes.Api.MasterData;

/// <summary>产线：工位的逻辑分组（如组装一线），不做线平衡排产。</summary>
public class ProductionLine
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public List<WorkStation> Stations { get; set; } = [];
}

/// <summary>
/// 工位：过站台选定的执行点。
/// 第一期每个工位只绑定一道工序（BoundProcessStepId），用于防跳站。
/// 注意：工位 ≠ 工序（地点 vs 步骤定义）。
/// </summary>
public class WorkStation
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid ProductionLineId { get; set; }
    public ProductionLine? ProductionLine { get; set; }

    /// <summary>本工位允许过站的唯一工序。</summary>
    public Guid BoundProcessStepId { get; set; }
    public ProcessStep? BoundProcessStep { get; set; }

    public bool IsActive { get; set; } = true;
}
