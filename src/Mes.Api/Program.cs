using System.Security.Claims;
using System.Text;
using Mes.Api.Data;
using Mes.Api.Execution;
using Mes.Api.Identity;
using Mes.Api.Integration;
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
builder.Services.AddScoped<StationPassService>(); // 过站、防跳站、关键件绑定、谱系
builder.Services.AddScoped<QualityService>(); // 不合格/隔离/返工/报废/放行
builder.Services.AddScoped<ErpWritebackSimulator>(); // ERP 出站模拟
builder.Services.AddScoped<CompletionService>(); // 完工入库 / 关单
builder.Services.AddScoped<OpsOverviewService>(); // 经营总览（票 03）

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
    options.AddPolicy("PlannerOnly", p => p.RequireRole(AppRoles.Planner)); // 主数据/工单写：仅计划员
    // 过站写：不含经营者（Owner 只读经营）
    options.AddPolicy("StationRoles", p => p.RequireRole(AppRoles.StationWriters));
    options.AddPolicy("AnyBusinessRole", p => p.RequireRole(AppRoles.All)); // 含经营者可读
    options.AddPolicy("OpsOverviewRead", p => p.RequireAssertion(ctx =>
    {
        if (ctx.User.IsInRole(AppRoles.Owner) || ctx.User.IsInRole(AppRoles.Planner) || ctx.User.IsInRole(AppRoles.Leader))
            return true;
        return ctx.User.FindFirst(JwtTokenService.ClaimCanViewOpsOverview)?.Value == "true";
    }));
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

// 关机原因可见：若进程「自己停」，日志里应出现 ApplicationStopping / Stopped
// （被任务管理器/Stop-Process 强杀时可能来不及打日志）
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
var lifeLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Mes.Lifetime");
lifetime.ApplicationStarted.Register(() =>
    lifeLog.LogInformation("MES API 已启动，监听中。关闭原因会写 ApplicationStopping 日志。"));
lifetime.ApplicationStopping.Register(() =>
    lifeLog.LogWarning("MES API 正在关闭 (ApplicationStopping) — 常见原因: Ctrl+C、dotnet 宿主结束、或进程被外部终止。"));
lifetime.ApplicationStopped.Register(() =>
    lifeLog.LogWarning("MES API 已停止 (ApplicationStopped)。"));

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

    var landing = RoleAccess.ForUser(user);
    return Results.Ok(new LoginResponse(
        accessToken,
        user.UserName,
        user.Role,
        user.DisplayName,
        landing.DefaultShell,
        landing.DefaultPath,
        landing.CanViewOpsOverview,
        landing.CanAccessManagementShell,
        landing.CanAccessStationShell,
        landing.CanWriteExecution,
        landing.CanWriteStation,
        landing.CanWriteMasterData));
})
.AllowAnonymous();

app.MapGet("/api/me", async (ClaimsPrincipal principal, MesDbContext db) =>
{
    var userName = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name) ?? "";
    var account = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserName == userName && u.IsActive);
    if (account is null)
    {
        return Results.Unauthorized();
    }

    var landing = RoleAccess.ForUser(account);
    return Results.Ok(new MeResponse(
        account.UserName,
        account.Role,
        account.DisplayName,
        landing.DefaultShell,
        landing.DefaultPath,
        landing.CanViewOpsOverview,
        landing.CanAccessManagementShell,
        landing.CanAccessStationShell,
        landing.CanWriteExecution,
        landing.CanWriteStation,
        landing.CanWriteMasterData));
})
.RequireAuthorization("AnyBusinessRole");

// 二期票 02：管理端侧栏菜单（按角色裁剪）
app.MapGet("/api/nav/management", async (ClaimsPrincipal principal, MesDbContext db) =>
{
    var userName = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name) ?? "";
    var account = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserName == userName && u.IsActive);
    if (account is null)
    {
        return Results.Unauthorized();
    }

    var landing = RoleAccess.ForUser(account);
    var menus = ManagementNav.ForUser(account);
    return Results.Ok(new
    {
        shell = landing.DefaultShell,
        menus
    });
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

// 票 04：过站台、SN、防跳站、谱系
app.MapStationPassEndpoints();

// 票 06：完工入库、成品仓、关单、ERP 出站
app.MapCompletionEndpoints();

// 票 03：经营总览五块 + 下钻
app.MapOpsOverviewEndpoints();

app.Run();

// WebApplicationFactory<Program> 需要可见的 Program 类型
public partial class Program;

public record LoginRequest(string UserName, string Password);

/// <summary>登录成功：令牌 + 二期落地/能力声明。</summary>
public record LoginResponse(
    string AccessToken,
    string UserName,
    string Role,
    string DisplayName,
    string DefaultShell,
    string DefaultPath,
    bool CanViewOpsOverview,
    bool CanAccessManagementShell,
    bool CanAccessStationShell,
    bool CanWriteExecution,
    bool CanWriteStation,
    bool CanWriteMasterData);

/// <summary>当前用户：与登录响应同构的能力声明（不含令牌）。</summary>
public record MeResponse(
    string UserName,
    string Role,
    string DisplayName,
    string DefaultShell,
    string DefaultPath,
    bool CanViewOpsOverview,
    bool CanAccessManagementShell,
    bool CanAccessStationShell,
    bool CanWriteExecution,
    bool CanWriteStation,
    bool CanWriteMasterData);

public record AuditResponse(string Action, string ActorUserName, string? SubjectType, string? SubjectId, DateTimeOffset OccurredAt);
