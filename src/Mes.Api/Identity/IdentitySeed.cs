using Mes.Api.Data;

namespace Mes.Api.Identity;

public static class IdentitySeed
{
    public static void EnsureSeeded(MesDbContext db)
    {
        if (db.Users.Any())
        {
            return;
        }

        db.Users.AddRange(
            Create("planner", "计划员演示", "Planner@123", AppRoles.Planner),
            Create("operator", "操作工演示", "Operator@123", AppRoles.Operator),
            Create("leader", "班组长演示", "Leader@123", AppRoles.Leader)
        );
        db.SaveChanges();
    }

    private static UserAccount Create(string userName, string displayName, string password, string role) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            DisplayName = displayName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = role,
            IsActive = true
        };
}
