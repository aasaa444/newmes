namespace Mes.Api.Identity;

/// <summary>
/// 管理端一级五菜单定义与按角色裁剪（CONTEXT 管理端一级菜单）。
/// </summary>
public static class ManagementNav
{
    public static IReadOnlyList<NavMenuItem> ForUser(UserAccount user)
    {
        var landing = RoleAccess.ForUser(user);
        if (!landing.CanAccessManagementShell)
        {
            return [];
        }

        var role = user.Role;
        var items = new List<NavMenuItem>();

        // ① 经营总览 — Owner 必有；Planner/Leader 需可看总览
        if (landing.CanViewOpsOverview)
        {
            items.Add(new NavMenuItem("overview", "经营总览", "/plan", null));
        }

        // ② 生产执行
        items.Add(new NavMenuItem(
            "production",
            "生产执行",
            "/plan/work-orders",
            [
                new NavMenuItem("work-orders", "工单工作台", "/plan/work-orders", null),
                new NavMenuItem("wip", "在制工作台", "/plan/wip", null),
            ]));

        // ③ 质量异常
        items.Add(new NavMenuItem("quality", "质量异常", "/plan/quality", null));

        // ④ 物料库存
        items.Add(new NavMenuItem("inventory", "物料库存", "/plan/inventory", null));

        // ⑤ 系统 — 经营者可读审计/出站；主数据写仍仅计划员（菜单可进只读页）
        var systemChildren = new List<NavMenuItem>
        {
            new("master-data", "主数据", "/plan/master-data", null),
            new("audit", "业务审计", "/plan/audit", null),
            new("erp-outbox", "ERP 出站", "/plan/integration", null),
        };
        if (role is AppRoles.Planner or AppRoles.Owner)
        {
            systemChildren.Insert(1, new NavMenuItem("users", "用户与角色", "/plan/users", null));
        }

        items.Add(new NavMenuItem("system", "系统", "/plan/master-data", systemChildren));

        return items;
    }
}

public sealed record NavMenuItem(
    string Key,
    string Title,
    string Path,
    IReadOnlyList<NavMenuItem>? Children);
