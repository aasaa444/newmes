using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Mes.Api.MasterData;

namespace Mes.Api.Tests;

/// <summary>票 05：不合格 → 隔离/返工/报废；放行；隔离不可合格过站；谱系与审计。</summary>
public class QualitySeamTests : IClassFixture<MesApiFactory>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public QualitySeamTests(MesApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Fail_isolate_blocks_pass_until_release()
    {
        var op = await LoginAsync("operator", "Operator@123");
        var planner = await LoginAsync("planner", "Planner@123");
        var (woId, onlineId, flashId, sn) = await SerialAtFlashAsync(op, planner);

        var fail = await PostOkAsync<SerialDto>("/api/station/fail", op, new
        {
            workStationId = flashId,
            serialNo = sn,
            disposition = "Isolate",
            reason = "烧录不良待判"
        });
        fail.Status.Should().Be("Isolated");

        var passBlocked = await SendAsync(HttpMethod.Post, "/api/station/pass", op, new
        {
            workStationId = flashId,
            serialNo = sn
        });
        passBlocked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await passBlocked.Content.ReadAsStringAsync()).Should().Contain("isolated");

        var list = await GetAsync<List<IsolatedDto>>("/api/quality/isolated", planner);
        list.Should().Contain(x => x.SerialNo == sn);

        var released = await PostOkAsync<SerialDto>("/api/quality/release", planner, new
        {
            serialNo = sn,
            reason = "复测合格放行",
            returnToSequence = (int?)null
        });
        released.Status.Should().Be("InProcess");

        // 放行后可在 FLASH 再过站
        var ok = await PostOkAsync<SerialDto>("/api/station/pass", op, new
        {
            workStationId = flashId,
            serialNo = sn
        });
        ok.CurrentStepCode.Should().Be("ASSEMBLY");

        var g = await GetAsync<GenealogyDto>($"/api/genealogy/{sn}", planner);
        g.Passes.Should().Contain(p => p.Result == "Fail");
        g.Passes.Should().Contain(p => p.Result == "Release");
        g.Passes.Should().Contain(p => p.Result == "Pass" && p.StepCode == "FLASH");
    }

    [Fact]
    public async Task Fail_rework_moves_current_step_back()
    {
        var op = await LoginAsync("operator", "Operator@123");
        var planner = await LoginAsync("planner", "Planner@123");
        var (_, _, flashId, sn) = await SerialAtFlashAsync(op, planner);

        // FLASH 失败，返工到 ONLINE (sequence 10)
        var rework = await PostOkAsync<SerialDto>("/api/station/fail", op, new
        {
            workStationId = flashId,
            serialNo = sn,
            disposition = "Rework",
            reason = "需重烧",
            reworkToSequence = 10
        });
        rework.Status.Should().Be("InProcess");
        rework.CurrentStepCode.Should().Be("ONLINE");

        var g = await GetAsync<GenealogyDto>($"/api/genealogy/{sn}", planner);
        g.Passes.Should().Contain(p => p.Result == "Fail");
        g.Passes.Should().Contain(p => p.Result == "Rework");
        // 历史不过覆盖
        g.Passes.Count(p => p.Result == "Pass" && p.StepCode == "ONLINE").Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Fail_scrap_increments_work_order_and_ends_serial()
    {
        var op = await LoginAsync("operator", "Operator@123");
        var planner = await LoginAsync("planner", "Planner@123");
        var (woId, onlineId, flashId, sn) = await SerialAtFlashAsync(op, planner);

        var before = await GetAsync<WoDetailDto>($"/api/work-orders/{woId}", planner);
        var scrapCountBefore = before.Header.ScrappedQty;
        var wipBefore = before.Header.InProcessSerialCount;

        var scrap = await PostOkAsync<SerialDto>("/api/station/fail", op, new
        {
            workStationId = flashId,
            serialNo = sn,
            disposition = "Scrap",
            reason = "不可修"
        });
        scrap.Status.Should().Be("Scrapped");
        scrap.CurrentStepCode.Should().BeNull();

        var after = await GetAsync<WoDetailDto>($"/api/work-orders/{woId}", planner);
        after.Header.ScrappedQty.Should().Be(scrapCountBefore + 1);
        after.Header.InProcessSerialCount.Should().Be(wipBefore - 1);

        var passBlocked = await SendAsync(HttpMethod.Post, "/api/station/pass", op, new
        {
            workStationId = onlineId,
            serialNo = sn
        });
        passBlocked.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var g = await GetAsync<GenealogyDto>($"/api/genealogy/{sn}", planner);
        g.Status.Should().Be("Scrapped");
        g.Passes.Should().Contain(p => p.Result == "Scrap" || p.Result == "Fail");
    }

    [Fact]
    public async Task Quality_actions_write_audit()
    {
        var op = await LoginAsync("operator", "Operator@123");
        var planner = await LoginAsync("planner", "Planner@123");
        var (_, _, flashId, sn) = await SerialAtFlashAsync(op, planner);

        await PostOkAsync<SerialDto>("/api/station/fail", op, new
        {
            workStationId = flashId,
            serialNo = sn,
            disposition = "Isolate",
            reason = "audit-iso"
        });
        await PostOkAsync<SerialDto>("/api/quality/release", planner, new
        {
            serialNo = sn,
            reason = "audit-release"
        });

        var audit = await GetAsync<List<AuditDto>>("/api/audit", planner);
        audit.Should().Contain(a => a.Action == "QualityIsolate");
        audit.Should().Contain(a => a.Action == "QualityRelease");
    }

    [Fact]
    public async Task Scrap_api_works_on_isolated_serial()
    {
        var op = await LoginAsync("operator", "Operator@123");
        var planner = await LoginAsync("planner", "Planner@123");
        var (woId, _, flashId, sn) = await SerialAtFlashAsync(op, planner);

        await PostOkAsync<SerialDto>("/api/station/fail", op, new
        {
            workStationId = flashId,
            serialNo = sn,
            disposition = "Isolate",
            reason = "hold"
        });

        var scrap = await PostOkAsync<SerialDto>("/api/quality/scrap", planner, new
        {
            serialNo = sn,
            reason = "评审报废"
        });
        scrap.Status.Should().Be("Scrapped");

        var list = await GetAsync<List<IsolatedDto>>("/api/quality/isolated", planner);
        list.Should().NotContain(x => x.SerialNo == sn);

        var wo = await GetAsync<WoDetailDto>($"/api/work-orders/{woId}", planner);
        wo.Header.ScrappedQty.Should().BeGreaterThan(0);
    }

    /// <summary>上线并停在 FLASH 当前工序（ONLINE 已过）。</summary>
    private async Task<(Guid WoId, Guid OnlineId, Guid FlashId, string SerialNo)> SerialAtFlashAsync(
        string op, string planner)
    {
        var mats = await GetAsync<List<MatDto>>("/api/materials", planner);
        var fgId = mats.First(m => m.Code == MasterDataSeed.FinishedCode).Id;
        var wo = await PostOkAsync<WoDto>("/api/work-orders", planner, new
        {
            finishedMaterialId = fgId,
            plannedQty = 5
        });
        await PostOkAsync<WoDto>($"/api/work-orders/{wo.Id}/release", planner, new { });
        await PostOkAsync<object>($"/api/work-orders/{wo.Id}/issue", planner, new { });

        var stations = await GetAsync<List<StationDto>>("/api/stations/active", op);
        var onlineId = stations.First(s => s.StepCode == "ONLINE").Id;
        var flashId = stations.First(s => s.StepCode == "FLASH").Id;

        var sn = $"SN-Q5-{Guid.NewGuid():N}"[..18].ToUpperInvariant();
        await PostOkAsync<SerialDto>("/api/station/pass", op, new
        {
            workStationId = onlineId,
            serialNo = sn,
            workOrderId = wo.Id
        });
        return (wo.Id, onlineId, flashId, sn);
    }

    private async Task<string> LoginAsync(string user, string pass)
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login", new { userName = user, password = pass });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<LoginDto>(JsonOpts))!.AccessToken;
    }

    private async Task<T> GetAsync<T>(string path, string token)
    {
        var res = await SendAsync(HttpMethod.Get, path, token, null);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await res.Content.ReadFromJsonAsync<T>(JsonOpts))!;
    }

    private async Task<T> PostOkAsync<T>(string path, string token, object body)
    {
        var res = await SendAsync(HttpMethod.Post, path, token, body);
        res.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var text = await res.Content.ReadAsStringAsync();
        if (typeof(T) == typeof(object) || string.IsNullOrWhiteSpace(text) || text == "{}")
            return default!;
        return JsonSerializer.Deserialize<T>(text, JsonOpts)!;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, object? body)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body);
        return await _client.SendAsync(req);
    }

    private sealed record LoginDto(string AccessToken);
    private sealed record MatDto(Guid Id, string Code);
    private sealed record StationDto(Guid Id, string Code, string StepCode);
    private sealed record SerialDto(string SerialNo, string Status, string? CurrentStepCode);
    private sealed record WoDto(Guid Id, string Status, int InProcessSerialCount, decimal ScrappedQty);
    private sealed record WoDetailDto(WoDto Header);
    private sealed record IsolatedDto(string SerialNo, string WorkOrderNo);
    private sealed record PassDto(string StepCode, string Result);
    private sealed record GenealogyDto(string SerialNo, string Status, List<PassDto> Passes);
    private sealed record AuditDto(string Action);
}
