using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Mes.Api.Tests;

public class AuthSeamTests : IClassFixture<MesApiFactory>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public AuthSeamTests(MesApiFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Health_is_public_and_returns_ok()
    {
        var res = await _client.GetAsync("/health");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("Healthy");
    }

    [Fact]
    public async Task Protected_endpoint_without_token_returns_401()
    {
        var res = await _client.GetAsync("/api/me");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Planner_can_login_and_access_me()
    {
        var token = await LoginAsync("planner", "Planner@123");
        token.Should().NotBeNullOrWhiteSpace();

        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var me = await res.Content.ReadFromJsonAsync<MeDto>(JsonOpts);
        me.Should().NotBeNull();
        me!.UserName.Should().Be("planner");
        me.Role.Should().Be("Planner");
        me.DisplayName.Should().Contain("计划");
    }

    [Fact]
    public async Task Operator_cannot_access_planner_only_endpoint()
    {
        var token = await LoginAsync("operator", "Operator@123");
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/plan/ping");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Planner_can_access_planner_only_endpoint()
    {
        var token = await LoginAsync("planner", "Planner@123");
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/plan/ping");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_writes_business_audit()
    {
        var token = await LoginAsync("leader", "Leader@123");
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/audit");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await res.Content.ReadFromJsonAsync<List<AuditDto>>(JsonOpts);
        items.Should().NotBeNull();
        items!.Should().Contain(a => a.Action == "Login" && a.ActorUserName == "leader");
    }

    [Fact]
    public async Task Bad_password_returns_401()
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login", new { userName = "planner", password = "wrong" });
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<string> LoginAsync(string userName, string password)
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login", new { userName, password });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await res.Content.ReadFromJsonAsync<LoginResponse>(JsonOpts);
        return dto!.AccessToken;
    }

    private sealed record LoginResponse(
        string AccessToken,
        string UserName,
        string Role,
        string DisplayName,
        string? DefaultShell = null,
        string? DefaultPath = null);
    private sealed record MeDto(
        string UserName,
        string Role,
        string DisplayName,
        string? DefaultShell = null,
        string? DefaultPath = null);
    private sealed record AuditDto(string Action, string ActorUserName, string? SubjectType, string? SubjectId, DateTimeOffset OccurredAt);
}
