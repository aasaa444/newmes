namespace Mes.Api.MasterData;

/// <summary>
/// 工艺路线：某成品上的有序工序模板。
/// 工单下达时应冻结 Version，避免在制中途改模板（见 CONTEXT）。
/// </summary>
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

/// <summary>
/// 工序：路线上的一步；过站、质检规则挂在这里。
/// Sequence 决定线性顺序；ReworkToSequence 预留给不合格回跳。
/// </summary>
public class ProcessStep
{
    public Guid Id { get; set; }
    public Guid ProcessRouteId { get; set; }
    public ProcessRoute? ProcessRoute { get; set; }

    /// <summary>顺序号，如 10、20、30（留空隙便于插入）。</summary>
    public int Sequence { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>是否质检类工序（终检等）。</summary>
    public bool IsQualityStep { get; set; }

    /// <summary>失败时建议返工到的工序 Sequence；空表示未配置。</summary>
    public int? ReworkToSequence { get; set; }
}
