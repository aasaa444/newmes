using Mes.Api.Data;

namespace Mes.Api.Identity;

/// <summary>
/// 演示账号种子。幂等：按用户名补齐缺失账号（含二期经营者）。
/// 密码仅用于本地/演示，生产必须改密或改种子策略。
/// </summary>
public static class IdentitySeed
{
    public static void EnsureSeeded(MesDbContext db)
    {
        EnsureUser(db, "planner", "计划员演示", "Planner@123", AppRoles.Planner, canViewOpsOverview: true);
        EnsureUser(db, "operator", "操作工演示", "Operator@123", AppRoles.Operator, canViewOpsOverview: false);
        EnsureUser(db, "leader", "班组长演示", "Leader@123", AppRoles.Leader, canViewOpsOverview: true);
        EnsureUser(db, "owner", "经营者演示", "Owner@123", AppRoles.Owner, canViewOpsOverview: true);
        db.SaveChanges();
    }

    private static void EnsureUser(
        MesDbContext db,
        string userName,
        string displayName,
        string password,
        string role,
        bool canViewOpsOverview)
    {
        var existing = db.Users.FirstOrDefault(u => u.UserName == userName);
        if (existing is null)
        {
            db.Users.Add(new UserAccount
            {
                Id = Guid.NewGuid(),
                UserName = userName,
                DisplayName = displayName,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                Role = role,
                IsActive = true,
                CanViewOpsOverview = canViewOpsOverview || role == AppRoles.Owner
            });
            return;
        }

        // 已有库升级：补能力位与经营者角色不覆盖密码
        if (role == AppRoles.Owner)
        {
            existing.CanViewOpsOverview = true;
        }
        else if (userName is "planner" or "leader")
        {
            // 演示账号默认给总览能力（若从未设置过也安全）
            existing.CanViewOpsOverview = true;
        }
    }
}
