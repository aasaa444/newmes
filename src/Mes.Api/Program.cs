using System.Security.Claims;
using System.Text;
using Mes.Api.Data;
using Mes.Api.Execution;
using Mes.Api.Identity;
using Mes.Api.MasterData;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// =============================================================================
// MES API 入口（票 01 脚手架 + 票 02 主数据 + 票 03 工单/领料）
// 管道顺序：CORS → Authentication → Authorization → 端点
// 测试环境（Testing）由 MesApiFactory 换成 SQLite，不会走下面的 SQL Server 注册。
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

// ----- 选项与领域服务 -----
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<AuditService>(); // 业务审计：谁在何时对何对象做了什么
builder.Services.AddScoped<WorkOrderService>(); // 工单状态机、齐套、领料

var connectionString = builder.Configuration.GetConnectionString("MesDb")
    ?? "Server=(localdb)\\MSSQLLocalDB;Database=MesDb;Trusted_Connection=True;TrustServerCertificate=True";

// WebApplicationFactory 会 RemoveAll 后再注册 SQLite；此处仅非 Testing 挂 SQL Server
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<MesDbContext>(options => options.UseSqlServer(connectionString));
}

// ----- JWT 本地登录（第一期不做 AD/钉钉）-----
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            // 与 JwtTokenService 写入的 ClaimTypes.Role / Name 对齐，Authorize 才能认角色
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.Name
        };
    });

// 策略名在端点 RequireAuthorization("...") 中引用
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PlannerOnly", p => p.RequireRole(AppRoles.Planner));       // 计划员：主数据/工单写
    options.AddPolicy("StationRoles", p => p.RequireRole(AppRoles.Operator, AppRoles.Leader, AppRoles.Planner));
    options.AddPolicy("AnyBusinessRole", p => p.RequireRole(AppRoles.All));       // 三角色均可
});

builder.Services.AddCors(options =>
{
    // 开发期 Vue(Vite) 跨域调 API；试点可收紧来源
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());
});

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<MesDbContext>("database");

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// ----- 启动时建库 + 种子（Testing 由 Factory 自己 EnsureCreated/Seed）-----
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseBootstrap");
    // 解决：旧 MesDb 只有 Users 时 EnsureCreated 不补表 → Invalid object name 'Materials'
    DatabaseBootstrap.Initialize(db, app.Environment, logger);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseAuthentication(); // 解析 Bearer，填充 HttpContext.User
app.UseAuthorization();

app.MapHealthChecks("/health"); // 探针，可匿名（默认）

// ----- 认证 -----
app.MapPost("/api/auth/login", async (LoginRequest req, MesDbContext db, JwtTokenService tokens, AuditService audit) =>
{
    if (string.IsNullOrWhiteSpace(req.UserName) || string.IsNullOrWhiteSpace(req.Password))
    {
        return Results.Unauthorized();
    }

    var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == req.UserName && u.IsActive);
    // BCrypt 校验；库中存的是哈希不是明文
    if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
    {
        return Results.Unauthorized();
    }

    var accessToken = tokens.CreateToken(user);
    await audit.WriteAsync("Login", user.UserName, "User", user.Id.ToString(), "Local password login");

    return Results.Ok(new LoginResponse(accessToken, user.UserName, user.Role, user.DisplayName));
})
.AllowAnonymous();

app.MapGet("/api/me", (ClaimsPrincipal user) =>
{
    var userName = user.Identity?.Name ?? user.FindFirstValue(ClaimTypes.Name) ?? "";
    var role = user.FindFirstValue(ClaimTypes.Role) ?? "";
    var display = user.FindFirstValue("display_name") ?? userName;
    return Results.Ok(new MeResponse(userName, role, display));
})
.RequireAuthorization("AnyBusinessRole");

// 脚手架探测：仅计划员 / 工位相关角色
app.MapGet("/api/plan/ping", () => Results.Ok(new { ok = true, area = "plan" }))
    .RequireAuthorization("PlannerOnly");

app.MapGet("/api/station/ping", () => Results.Ok(new { ok = true, area = "station" }))
    .RequireAuthorization("StationRoles");

// 业务审计列表（与产品「谱系」分开：这里记人的操作）
app.MapGet("/api/audit", async (MesDbContext db) =>
{
    // 先 ToList 再排序：SQLite 测试库不能在 SQL 里 ORDER BY DateTimeOffset
    var raw = await db.AuditEntries.AsNoTracking().ToListAsync();
    var items = raw
        .OrderByDescending(a => a.OccurredAt)
        .Take(100)
        .Select(a => new AuditResponse(a.Action, a.ActorUserName, a.SubjectType, a.SubjectId, a.OccurredAt))
        .ToList();
    return Results.Ok(items);
})
.RequireAuthorization("AnyBusinessRole");

// 票 02：物料 / BOM / 工艺路线 / 产线 / 工位
app.MapMasterDataEndpoints();

// 票 03：生产工单、齐套、领料、线边库存
app.MapWorkOrderEndpoints();

app.Run();

// WebApplicationFactory<Program> 需要可见的 Program 类型
public partial class Program;

public record LoginRequest(string UserName, string Password);
public record LoginResponse(string AccessToken, string UserName, string Role, string DisplayName);
public record MeResponse(string UserName, string Role, string DisplayName);
public record AuditResponse(string Action, string ActorUserName, string? SubjectType, string? SubjectId, DateTimeOffset OccurredAt);
