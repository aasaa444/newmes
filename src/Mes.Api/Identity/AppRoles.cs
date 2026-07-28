namespace Mes.Api.Identity;

/// <summary>
/// Business roles from CONTEXT.md: 计划员 / 操作工 / 班组长.
/// </summary>
public static class AppRoles
{
    public const string Planner = "Planner";
    public const string Operator = "Operator";
    public const string Leader = "Leader";

    public static readonly string[] All = [Planner, Operator, Leader];
}
