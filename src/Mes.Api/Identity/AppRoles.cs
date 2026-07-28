namespace Mes.Api.Identity;

/// <summary>
/// 业务角色（CONTEXT：计划员 / 操作工 / 班组长 / 经营者）。
/// 字符串值写入 JWT 的 role claim，并与 Authorization Policy 一致。
/// 默认「单主角色」：一个账号一个 Role。
/// </summary>
public static class AppRoles
{
    /// <summary>计划员 — 主数据、工单、领料等办公室写操作。</summary>
    public const string Planner = "Planner";

    /// <summary>操作工 — 过站台执行过站 / 关键件绑定。</summary>
    public const string Operator = "Operator";

    /// <summary>班组长 — 进度与异常；可放行等现场管理动作。</summary>
    public const string Leader = "Leader";

    /// <summary>经营者 — 经营总览与下钻只读；不写执行/主数据/过站。</summary>
    public const string Owner = "Owner";

    /// <summary>所有可登录业务角色（含经营者）。</summary>
    public static readonly string[] All = [Planner, Operator, Leader, Owner];

    /// <summary>可进过站端写操作的角色（不含经营者）。</summary>
    public static readonly string[] StationWriters = [Operator, Leader, Planner];
}
