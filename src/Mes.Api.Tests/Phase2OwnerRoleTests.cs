using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Mes.Api.Tests;

/// <summary>二期票 01：经营者角色、登录落地声明、写拒绝。</summary>
public class Phase2OwnerRoleTests : IClassFixture<MesApiFactory>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public Phase2OwnerRoleTests(MesApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Owner_can_login_with_management_overview_landing()
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login", new { userName = "owner", password = "Owner@123" });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await res.Content.ReadFromJsonAsync<LoginDto>(JsonOpts);
        dto.Should().NotBeNull();
        dto!.Role.Should().Be("Owner");
        dto.DefaultShell.Should().Be("management");
        dto.DefaultPath.Should().Be("/plan");
        dto.CanViewOpsOverview.Should().BeTrue();
        dto.CanAccessManagementShell.Should().BeTrue();
        dto.CanAccessStationShell.Should().BeFalse();
        dto.CanWriteExecution.Should().BeFalse();
        dto.CanWriteStation.Should().BeFalse();
        dto.CanWriteMasterData.Should().BeFalse();
    }

    [Fact]
    public async Task Planner_login_lands_on_work_orders_path()
    {
        var dto = await LoginFullAsync("planner", "Planner@123");
        dto.Role.Should().Be("Planner");
        dto.DefaultShell.Should().Be("management");
        dto.DefaultPath.Should().Be("/plan/work-orders");
        dto.CanWriteExecution.Should().BeTrue();
        dto.CanWriteMasterData.Should().BeTrue();
        dto.CanViewOpsOverview.Should().BeTrue();
    }

    [Fact]
    public async Task Operator_login_lands_on_station()
    {
        var dto = await LoginFullAsync("operator", "Operator@123");
        dto.DefaultShell.Should().Be("station");
        dto.DefaultPath.Should().Be("/station");
        dto.CanAccessStationShell.Should().BeTrue();
        dto.CanWriteStation.Should().BeTrue();
        dto.CanWriteExecution.Should().BeFalse();
    }

    [Fact]
    public async Task Leader_login_lands_on_management_wip_area()
    {
        var dto = await LoginFullAsync("leader", "Leader@123");
        dto.DefaultShell.Should().Be("management");
        dto.CanWriteStation.Should().BeTrue();
        dto.CanWriteExecution.Should().BeFalse();
        dto.CanViewOpsOverview.Should().BeTrue();
    }

    [Fact]
    public async Task Me_returns_same_capabilities_as_login()
    {
        var login = await LoginFullAsync("owner", "Owner@123");
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = await res.Content.ReadFromJsonAsync<MeDto>(JsonOpts);
        me!.Role.Should().Be(login.Role);
        me.DefaultPath.Should().Be(login.DefaultPath);
        me.CanWriteExecution.Should().Be(login.CanWriteExecution);
        me.CanViewOpsOverview.Should().BeTrue();
    }

    [Fact]
    public async Task Owner_forbidden_on_planner_write_and_station_write()
    {
        var token = (await LoginFullAsync("owner", "Owner@123")).AccessToken;

        using var planPing = new HttpRequestMessage(HttpMethod.Get, "/api/plan/ping");
        planPing.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await _client.SendAsync(planPing)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var stationPing = new HttpRequestMessage(HttpMethod.Get, "/api/station/ping");
        stationPing.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await _client.SendAsync(stationPing)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var createMat = new HttpRequestMessage(HttpMethod.Post, "/api/materials")
        {
            Content = JsonContent.Create(new
            {
                code = "OWN-X",
                name = "不应创建",
                isFinishedGood = false,
                isKeyComponent = false,
                requiresSerialNumber = false
            })
        };
        createMat.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await _client.SendAsync(createMat)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var createWo = new HttpRequestMessage(HttpMethod.Post, "/api/work-orders")
        {
            Content = JsonContent.Create(new
            {
                finishedMaterialId = Guid.NewGuid(),
                plannedQty = 1
            })
        };
        createWo.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await _client.SendAsync(createWo)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Owner_can_read_work_orders_and_audit()
    {
        var token = (await LoginFullAsync("owner", "Owner@123")).AccessToken;
        using var wo = new HttpRequestMessage(HttpMethod.Get, "/api/work-orders");
        wo.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await _client.SendAsync(wo)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var audit = new HttpRequestMessage(HttpMethod.Get, "/api/audit");
        audit.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await _client.SendAsync(audit)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Planner_still_can_write_master_data()
    {
        var token = (await LoginFullAsync("planner", "Planner@123")).AccessToken;
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/plan/ping");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await _client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<LoginDto> LoginFullAsync(string userName, string password)
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login", new { userName, password });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await res.Content.ReadFromJsonAsync<LoginDto>(JsonOpts))!;
    }

    private sealed record LoginDto(
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

    private sealed record MeDto(
        string UserName,
        string Role,
        string DisplayName,
        string DefaultShell,
        string DefaultPath,
        bool CanViewOpsOverview,
        bool CanWriteExecution);
}
