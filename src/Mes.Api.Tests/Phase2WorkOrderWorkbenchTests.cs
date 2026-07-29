using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Mes.Api.MasterData;

namespace Mes.Api.Tests;

/// <summary>票 04：工单工作台 — 列表筛选与能力感知。</summary>
public class Phase2WorkOrderWorkbenchTests : IClassFixture<MesApiFactory>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public Phase2WorkOrderWorkbenchTests(MesApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Filter_by_status_returns_matching_bucket()
    {
        var token = await LoginAsync("planner", "Planner@123");

        // 准备：1 张草稿 + 1 张已下达
        var woDraft = await CreateOrderAsync(token, planned: 1);
        var woReleased = await CreateOrderAsync(token, planned: 1);
        await PostJsonAsync<WoDto>($"/api/work-orders/{woReleased.Id}/release", token, new { });

        var drafts = await GetJsonAsync<List<WoSummary>>("/api/work-orders?status=Draft", token);
        drafts.Should().Contain(w => w.Id == woDraft.Id);

        var released = await GetJsonAsync<List<WoSummary>>("/api/work-orders?status=Released", token);
        released.Should().Contain(w => w.Id == woReleased.Id);
    }

    [Fact]
    public async Task Filter_by_material_code_works()
    {
        var token = await LoginAsync("planner", "Planner@123");
        var wo = await CreateOrderAsync(token, planned: 1);

        var withFg = await GetJsonAsync<List<WoSummary>>("/api/work-orders?materialCode=FG-ROUTER", token);
        withFg.Should().Contain(w => w.Id == wo.Id);

        var noMatch = await GetJsonAsync<List<WoSummary>>("/api/work-orders?materialCode=NEVER-EXISTS", token);
        noMatch.Should().NotContain(w => w.Id == wo.Id);
    }

    [Fact]
    public async Task Filter_by_text_matches_orderNo_and_material_code()
    {
        var token = await LoginAsync("planner", "Planner@123");
        var wo = await CreateOrderAsync(token, planned: 1);

        // q 匹配 orderNo 子串
        var byOrderNo = await GetJsonAsync<List<WoSummary>>($"/api/work-orders?q={wo.OrderNo[..6]}", token);
        byOrderNo.Should().Contain(w => w.Id == wo.Id);

        // q 匹配 material code
        var byMat = await GetJsonAsync<List<WoSummary>>("/api/work-orders?q=router", token);
        byMat.Should().Contain(w => w.Id == wo.Id);
    }

    [Fact]
    public async Task Filter_materialIssued_toggles_visibility()
    {
        var token = await LoginAsync("planner", "Planner@123");
        var wo1 = await CreateOrderAsync(token, planned: 1);
        var wo2 = await CreateOrderAsync(token, planned: 1);
        await PostJsonAsync<WoDto>($"/api/work-orders/{wo1.Id}/release", token, new { });
        await PostJsonAsync<object>($"/api/work-orders/{wo1.Id}/issue", token, new { });

        var issued = await GetJsonAsync<List<WoSummary>>("/api/work-orders?materialIssued=true", token);
        issued.Should().Contain(w => w.Id == wo1.Id);
        issued.Should().NotContain(w => w.Id == wo2.Id);

        var notIssued = await GetJsonAsync<List<WoSummary>>("/api/work-orders?materialIssued=false", token);
        notIssued.Should().Contain(w => w.Id == wo2.Id);
    }

    [Fact]
    public async Task Owner_can_list_but_cannot_create_work_order()
    {
        var ownerToken = await LoginAsync("owner", "Owner@123");

        // 只读 OK
        var listRes = await SendAsync(HttpMethod.Get, "/api/work-orders", ownerToken, null);
        listRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 写拒绝
        var mats = await GetJsonAsync<List<MatDto>>("/api/materials", ownerToken);
        var fg = mats.First(m => m.Code == MasterDataSeed.FinishedCode).Id;
        var writeRes = await SendAsync(HttpMethod.Post, "/api/work-orders", ownerToken, new
        {
            finishedMaterialId = fg,
            plannedQty = 1
        });
        writeRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Leader_can_list_but_cannot_release_or_issue()
    {
        var leaderToken = await LoginAsync("leader", "Leader@123");
        var plannerToken = await LoginAsync("planner", "Planner@123");

        var wo = await CreateOrderAsync(plannerToken, planned: 1);

        var writeRes = await SendAsync(HttpMethod.Post, $"/api/work-orders/{wo.Id}/release", leaderToken, new { });
        writeRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var readRes = await SendAsync(HttpMethod.Get, "/api/work-orders", leaderToken, null);
        readRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ----- helpers -----
    private async Task<string> LoginAsync(string u, string p)
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login", new { userName = u, password = p });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<LoginDto>(JsonOpts))!.AccessToken;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod m, string path, string token, object? body)
    {
        using var req = new HttpRequestMessage(m, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body);
        return await _client.SendAsync(req);
    }

    private async Task<T> GetJsonAsync<T>(string path, string token)
    {
        var res = await SendAsync(HttpMethod.Get, path, token, null);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await res.Content.ReadFromJsonAsync<T>(JsonOpts))!;
    }

    private async Task<T> PostJsonAsync<T>(string path, string token, object body)
    {
        var res = await SendAsync(HttpMethod.Post, path, token, body);
        res.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        return JsonSerializer.Deserialize<T>(await res.Content.ReadAsStringAsync(), JsonOpts)!;
    }

    private async Task<WoDto> CreateOrderAsync(string token, int planned)
    {
        var mats = await GetJsonAsync<List<MatDto>>("/api/materials", token);
        var fg = mats.First(m => m.Code == MasterDataSeed.FinishedCode).Id;
        return await PostJsonAsync<WoDto>("/api/work-orders", token, new
        {
            finishedMaterialId = fg,
            plannedQty = planned
        });
    }

    private sealed record LoginDto(string AccessToken);
    private sealed record MatDto(Guid Id, string Code);
    private sealed record WoDto(Guid Id, string OrderNo, string FinishedMaterialCode);
    private sealed record WoSummary(Guid Id, string OrderNo, string FinishedMaterialCode);
}
