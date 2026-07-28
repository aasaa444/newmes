using System.Security.Claims;
using System.Text;
using Mes.Api.Data;
using Mes.Api.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<AuditService>();

var connectionString = builder.Configuration.GetConnectionString("MesDb")
    ?? "Server=(localdb)\\MSSQLLocalDB;Database=MesDb;Trusted_Connection=True;TrustServerCertificate=True";

// WebApplicationFactory (Testing) replaces MesDbContext registration with SQLite.
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<MesDbContext>(options => options.UseSqlServer(connectionString));
}

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
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.Name
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("PlannerOnly", p => p.RequireRole(AppRoles.Planner));
    options.AddPolicy("StationRoles", p => p.RequireRole(AppRoles.Operator, AppRoles.Leader, AppRoles.Planner));
    options.AddPolicy("AnyBusinessRole", p => p.RequireRole(AppRoles.All));
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());
});

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<MesDbContext>("database");

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
    db.Database.EnsureCreated();
    IdentitySeed.EnsureSeeded(db);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");

app.MapPost("/api/auth/login", async (LoginRequest req, MesDbContext db, JwtTokenService tokens, AuditService audit) =>
{
    if (string.IsNullOrWhiteSpace(req.UserName) || string.IsNullOrWhiteSpace(req.Password))
    {
        return Results.Unauthorized();
    }

    var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == req.UserName && u.IsActive);
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

app.MapGet("/api/plan/ping", () => Results.Ok(new { ok = true, area = "plan" }))
    .RequireAuthorization("PlannerOnly");

app.MapGet("/api/station/ping", () => Results.Ok(new { ok = true, area = "station" }))
    .RequireAuthorization("StationRoles");

app.MapGet("/api/audit", async (MesDbContext db) =>
{
    // Materialize then order — SQLite cannot ORDER BY DateTimeOffset in SQL.
    var raw = await db.AuditEntries.AsNoTracking().ToListAsync();
    var items = raw
        .OrderByDescending(a => a.OccurredAt)
        .Take(100)
        .Select(a => new AuditResponse(a.Action, a.ActorUserName, a.SubjectType, a.SubjectId, a.OccurredAt))
        .ToList();
    return Results.Ok(items);
})
.RequireAuthorization("AnyBusinessRole");

app.Run();

public partial class Program;

public record LoginRequest(string UserName, string Password);
public record LoginResponse(string AccessToken, string UserName, string Role, string DisplayName);
public record MeResponse(string UserName, string Role, string DisplayName);
public record AuditResponse(string Action, string ActorUserName, string? SubjectType, string? SubjectId, DateTimeOffset OccurredAt);
