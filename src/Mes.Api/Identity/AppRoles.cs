namespace Mes.Api.Identity;

/// <summary>
/// 业务角色（见 CONTEXT.md：计划员 / 操作工 / 班组长）。
/// 字符串值写入 JWT 的 role claim，并与 Authorization Policy 一致。
/// 第一期默认「单主角色」：一个账号一个 Role。
/// </summary>
public static class AppRoles
{
    /// <summary>计划员 — 主数据、工单、领料等办公室写操作。</summary>
    public const string Planner = "Planner";

    /// <summary>操作工 — 过站台执行过站 / 关键件绑定。</summary>
    public const string Operator = "Operator";

    /// <summary>班组长 — 进度与异常；第一期以查看 + 有限动作为主。</summary>
    public const string Leader = "Leader";

    public static readonly string[] All = [Planner, Operator, Leader];
}
