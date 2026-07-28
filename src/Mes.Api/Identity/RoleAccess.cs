namespace Mes.Api.Identity;

/// <summary>
/// 二期：角色 → 默认壳/落地页/能力（CONTEXT 登录默认落地、经营者只读）。
/// </summary>
public static class RoleAccess
{
    public const string ShellManagement = "management";
    public const string ShellStation = "station";

    /// <summary>前端路由提示（二期壳就绪前管理端暂用 /plan*）。</summary>
    public const string PathOpsOverview = "/plan"; // 票 03 再落到总览专用路由
    public const string PathWorkOrders = "/plan/work-orders";
    public const string PathWip = "/plan/work-orders"; // 票 05 再落到在制专用路由；暂与工单同区
    public const string PathStation = "/station";

    public static LandingProfile ForUser(UserAccount user)
    {
        var role = user.Role;
        var canViewOverview = user.CanViewOpsOverview
            || role is AppRoles.Owner or AppRoles.Planner or AppRoles.Leader;

        // 经营者强制可看总览；操作工默认否（除非显式授权）
        if (role == AppRoles.Operator)
        {
            canViewOverview = user.CanViewOpsOverview;
        }

        if (role == AppRoles.Owner)
        {
            canViewOverview = true;
        }

        var (shell, path) = role switch
        {
            AppRoles.Owner => (ShellManagement, PathOpsOverview),
            AppRoles.Planner => (ShellManagement, PathWorkOrders),
            AppRoles.Leader => (ShellManagement, PathWip),
            AppRoles.Operator => (ShellStation, PathStation),
            _ => (ShellManagement, PathWorkOrders)
        };

        // 操作工默认仅过站端；显式授权可看总览时才进管理端
        var canAccessManagement = role != AppRoles.Operator || user.CanViewOpsOverview;
        var canAccessStation = role is AppRoles.Operator or AppRoles.Leader or AppRoles.Planner;
        // 经营者默认不过站（只读经营）
        if (role == AppRoles.Owner)
        {
            canAccessStation = false;
        }

        return new LandingProfile(
            DefaultShell: shell,
            DefaultPath: path,
            CanViewOpsOverview: canViewOverview,
            CanAccessManagementShell: canAccessManagement,
            CanAccessStationShell: canAccessStation,
            CanWriteExecution: role is AppRoles.Planner,
            CanWriteStation: role is AppRoles.Operator or AppRoles.Leader or AppRoles.Planner,
            CanWriteMasterData: role is AppRoles.Planner);
    }
}

public sealed record LandingProfile(
    string DefaultShell,
    string DefaultPath,
    bool CanViewOpsOverview,
    bool CanAccessManagementShell,
    bool CanAccessStationShell,
    bool CanWriteExecution,
    bool CanWriteStation,
    bool CanWriteMasterData);
