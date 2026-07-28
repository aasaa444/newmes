using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Mes.Api.Tests;

/// <summary>二期票 02：管理端导航按角色裁剪（API 高缝）。</summary>
public class Phase2NavShellTests : IClassFixture<MesApiFactory>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public Phase2NavShellTests(MesApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Owner_nav_has_five_primary_menus_in_order()
    {
        var token = await LoginAsync("owner", "Owner@123");
        var nav = await GetNavAsync(token);
        nav.Shell.Should().Be("management");
        nav.Menus.Select(m => m.Key).Should().Equal(
            "overview", "production", "quality", "inventory", "system");
        nav.Menus.Select(m => m.Title).Should().Equal(
            "经营总览", "生产执行", "质量异常", "物料库存", "系统");
    }

    [Fact]
    public async Task Operator_nav_is_empty_management_menus()
    {
        var token = await LoginAsync("operator", "Operator@123");
        var nav = await GetNavAsync(token);
        nav.Shell.Should().Be("station");
        nav.Menus.Should().BeEmpty();
    }

    [Fact]
    public async Task Planner_nav_includes_overview_and_production()
    {
        var token = await LoginAsync("planner", "Planner@123");
        var nav = await GetNavAsync(token);
        nav.Menus.Should().Contain(m => m.Key == "overview");
        nav.Menus.Should().Contain(m => m.Key == "production");
        var prod = nav.Menus.First(m => m.Key == "production");
        prod.Children!.Select(c => c.Key).Should().Contain(["work-orders", "wip"]);
    }

    [Fact]
    public async Task Unauthorized_nav_returns_401()
    {
        var res = await _client.GetAsync("/api/nav/management");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<string> LoginAsync(string user, string pass)
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login", new { userName = user, password = pass });
        res.EnsureSuccessStatusCode();
        var dto = await res.Content.ReadFromJsonAsync<LoginDto>(JsonOpts);
        return dto!.AccessToken;
    }

    private async Task<NavDto> GetNavAsync(string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/nav/management");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await res.Content.ReadFromJsonAsync<NavDto>(JsonOpts))!;
    }

    private sealed record LoginDto(string AccessToken);
    private sealed record NavItemDto(string Key, string Title, string Path, List<NavItemDto>? Children);
    private sealed record NavDto(string Shell, List<NavItemDto> Menus);
}
