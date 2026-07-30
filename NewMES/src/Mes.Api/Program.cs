using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mes.Api.Identity;
using Mes.Api.Execution;
using Mes.Api.Integration;
using Mes.Api.Observability;
using Mes.Api.Readiness;
using Mes.Api.Materials;
using Mes.Infrastructure.Persistence;
using Mes.Infrastructure.Security;
using Mes.Infrastructure.IdentityAccess;
using Mes.Infrastructure.Execution;
using Mes.Infrastructure.Integration;
using Mes.Infrastructure.Materials;
using Mes.Domain.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var secretsDirectory = Environment.GetEnvironmentVariable("MES_SECRETS_DIRECTORY")
    ?? "/run/secrets";
builder.Configuration.AddKeyPerFile(secretsDirectory, optional: true);

if (builder.Environment.IsProduction())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        options.UseUtcTimestamp = true;
    });
}

var connectionString = builder.Configuration.GetConnectionString("MesDatabase");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:MesDatabase is required; the API never creates an implicit database.");
}
var signingKey = builder.Configuration["Security:JwtSigningKey"];
if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
{
    throw new InvalidOperationException(
        "Security:JwtSigningKey with at least 32 characters is required.");
}

builder.Services.AddDbContext<MesDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));
builder.Services.AddScoped<DatabaseCompatibilityChecker>();
builder.Services.AddScoped<IRuntimeDatabasePrivilegeProbe, SqlServerRuntimePrivilegeProbe>();
builder.Services.AddSingleton(_ => new ProductionSecurityContextProvider(
    builder.Configuration,
    builder.Environment,
    secretsDirectory));
builder.Services.AddScoped<CorrelationContextAccessor>();
builder.Services.AddScoped<CurrentIdentityAccessor>();
builder.Services.AddScoped<IdentityAccessService>();
builder.Services.AddScoped<LocalAccountAuthenticator>();
builder.Services.AddScoped<ProductionOrderIngressService>();
builder.Services.AddScoped<ProductionOrderWorkbenchQueryService>();
builder.Services.AddScoped<ExecutionTemplateService>();
builder.Services.AddScoped<ProductionOrderLifecycleService>();
builder.Services.AddScoped<MaterialTransactionService>();
builder.Services.AddScoped<MaterialWorkbenchQueryService>();
builder.Services.AddScoped<ProductIdentityService>();
builder.Services.AddScoped<AssemblyMaterialService>();
builder.Services.AddScoped<FirmwareConfigurationService>();
builder.Services.AddScoped<TestSpecificationService>();
builder.Services.AddScoped<TestRunService>();
builder.Services.AddScoped<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<JwtTokenIssuer>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Authentication:Issuer"] ?? "NewMES",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Authentication:Audience"] ?? "NewMES.Web",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // The API is reachable only on the private Compose network; Nginx is the trust boundary.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<DatabaseCompatibilityHealthCheck>("database", tags: ["ready"])
    .AddCheck<ProductionSecurityHealthCheck>("security", tags: ["ready"]);
builder.Services.AddHostedService<DatabaseCompatibilityStartupReporter>();
builder.Services.AddHostedService<ProductionSecurityStartupReporter>();

var configuredCorsOrigins = builder.Configuration
    .GetSection("Security:AllowedCorsOrigins")
    .GetChildren()
    .Select(child => child.Value)
    .Where(value => !string.IsNullOrWhiteSpace(value))
    .Cast<string>()
    .ToArray();
var effectiveCorsOrigins = configuredCorsOrigins
    .Where(origin => ProductionCorsPolicy.IsAllowedOrigin(
        origin,
        builder.Environment.IsProduction()))
    .ToArray();
if (effectiveCorsOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddPolicy(
        "ConfiguredOrigins",
        policy => policy
            .WithOrigins(effectiveCorsOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()));
}

var app = builder.Build();

app.UseForwardedHeaders();
if (app.Environment.IsProduction())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseMiddleware<CurrentIdentityMiddleware>();
app.UseAuthorization();
if (effectiveCorsOrigins.Length > 0)
{
    app.UseCors("ConfiguredOrigins");
}

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live"),
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    message = entry.Value.Description,
                    data = entry.Value.Data,
                }),
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    },
});
app.MapGet("/api/system/info", (CorrelationContextAccessor correlation) =>
    Results.Ok(new
    {
        service = "NewMES.Api",
        status = "running",
        correlationId = correlation.CorrelationId,
    }));
app.MapIdentityEndpoints();
app.MapProductionOrderEndpoints(app.Environment.IsDevelopment());
app.MapExecutionTemplateEndpoints();
app.MapProductionOrderLifecycleEndpoints();
app.MapMaterialTransactionEndpoints();
app.MapProductIdentityEndpoints();
app.MapAssemblyMaterialEndpoints();
app.MapFirmwareConfigurationEndpoints();
app.MapTestSpecificationEndpoints();
app.MapTestRunEndpoints();

app.Run();

public partial class Program;
