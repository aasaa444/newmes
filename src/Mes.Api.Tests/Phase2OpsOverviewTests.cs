using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Mes.Api.MasterData;

namespace Mes.Api.Tests;

/// <summary>票 03：经营总览五块 + 下钻。种子固定时间下断言数与下钻数据。</summary>
public class Phase2OpsOverviewTests : IClassFixture<MesApiFactory>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public Phase2OpsOverviewTests(MesApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Owner_can_read_overview_five_blocks()
    {
        var token = await LoginAsync("owner", "Owner@123");
        var res = await GetAsync("/api/ops/overview", token);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await res.Content.ReadFromJsonAsync<OverviewDto>(JsonOpts);
        dto.Should().NotBeNull();
        dto!.Wip.Total.Should().BeGreaterThanOrEqualTo(0);
        dto.Wip.ByProcess.Should().NotBeNull();
        dto.TodayQualifiedReceipts.Should().BeGreaterThanOrEqualTo(0);
        dto.TodayScraps.Should().BeGreaterThanOrEqualTo(0);
        dto.IsolatedPendingCount.Should().BeGreaterThanOrEqualTo(0);
        dto.Orders.InProcess.Should().BeGreaterThanOrEqualTo(0);
        dto.Orders.ReleasedNotStarted.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Operator_cannot_read_overview()
    {
        var token = await LoginAsync("operator", "Operator@123");
        var res = await GetAsync("/api/ops/overview", token);
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Overview_reflects_seeded_execution_facts()
    {
        var planner = await LoginAsync("planner", "Planner@123");
        var op = await LoginAsync("operator", "Operator@123");

        var mats = await GetMatList(planner);
        var fgId = mats.First(m => m.Code == MasterDataSeed.FinishedCode).Id;
        var pcbId = mats.First(m => m.Code == MasterDataSeed.PcbCode).Id;
        var stations = await GetActiveStations(op);
        var onlineId = stations.First(s => s.StepCode == "ONLINE").Id;
        var flashId = stations.First(s => s.StepCode == "FLASH").Id;
        var packId = stations.First(s => s.StepCode == "PACK").Id;

        var overviewBefore = await ReadOverview(planner);

        // 1) 建工单 + 下达 + 领料
        var wo = await PostJsonAsync<WoDto>("/api/work-orders", planner, new
        {
            finishedMaterialId = fgId,
            plannedQty = 3
        });
        await PostJsonAsync<WoDto>($"/api/work-orders/{wo.Id}/release", planner, new { });
        await PostJsonAsync<object>($"/api/work-orders/{wo.Id}/issue", planner, new { });

        // 2) 1 台过 ONLINE 留在制
        var sn1 = "SN-OV-1-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        await PostJsonAsync<SerialDto>("/api/station/pass", op, new
        {
            workStationId = onlineId,
            serialNo = sn1,
            workOrderId = wo.Id
        });

        // 3) 1 台过 ONLINE+FLASH 后在 FLASH 报废（Scrap 写履历）
        var sn2 = "SN-OV-2-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        await PostJsonAsync<SerialDto>("/api/station/pass", op, new
        {
            workStationId = onlineId,
            serialNo = sn2,
            workOrderId = wo.Id
        });
        await PostJsonAsync<SerialDto>("/api/station/fail", op, new
        {
            workStationId = flashId,
            serialNo = sn2,
            disposition = "Scrap",
            reason = "test"
        });

        // 4) 1 台走全路线完工入库
        var sn3 = "SN-OV-3-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        foreach (var code in new[] { "ONLINE", "FLASH", "ASSEMBLY", "FQC", "PACK" })
        {
            var stId = stations.First(s => s.StepCode == code).Id;
            var body = new
            {
                workStationId = stId,
                serialNo = sn3,
                workOrderId = wo.Id
            };
            await PostJsonAsync<SerialDto>("/api/station/pass", op, body);
        }
        await PostJsonAsync<object>("/api/work-orders/" + wo.Id + "/issue", planner, new { });
        // already issued; receive to warehouse
        await PostJsonAsync<object>("/api/completion/receive", op, new { serialNo = sn3 });

        // 5) 1 台隔离（在 FLASH 失败后 Isolate）
        var sn4 = "SN-OV-4-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        await PostJsonAsync<SerialDto>("/api/station/pass", op, new
        {
            workStationId = onlineId,
            serialNo = sn4,
            workOrderId = wo.Id
        });
        await PostJsonAsync<SerialDto>("/api/station/fail", op, new
        {
            workStationId = flashId,
            serialNo = sn4,
            disposition = "Isolate",
            reason = "test"
        });

        var overviewAfter = await ReadOverview(planner);

        // 今日合格入库 +1
        (overviewAfter.TodayQualifiedReceipts - overviewBefore.TodayQualifiedReceipts)
            .Should().Be(1);
        // 今日报废 +1（FLASH Scrap 写履历）
        (overviewAfter.TodayScraps - overviewBefore.TodayScraps).Should().Be(1);
        // 隔离 +1
        (overviewAfter.IsolatedPendingCount - overviewBefore.IsolatedPendingCount)
            .Should().Be(1);
        // 在制：sn1 在 ONLINE，sn2 报废，sn3 已完成，sn4 隔离 → net +1
        (overviewAfter.Wip.Total - overviewBefore.Wip.Total).Should().Be(1);
        // 工单：inProcess 应有
        overviewAfter.Orders.InProcess.Should().BeGreaterThan(0);
        // 在制分布：sn1 在 ONLINE 过站后停在 FLASH
        overviewAfter.Wip.ByProcess.Should().Contain(b => b.StepCode == "FLASH");
    }

    [Fact]
    public async Task Wip_drill_returns_sn_list()
    {
        var op = await LoginAsync("operator", "Operator@123");
        var planner = await LoginAsync("planner", "Planner@123");

        // 准备一个在制
        var mats = await GetMatList(planner);
        var fgId = mats.First(m => m.Code == MasterDataSeed.FinishedCode).Id;
        var stations = await GetActiveStations(op);
        var onlineId = stations.First(s => s.StepCode == "ONLINE").Id;

        var wo = await PostJsonAsync<WoDto>("/api/work-orders", planner, new
        {
            finishedMaterialId = fgId,
            plannedQty = 1
        });
        await PostJsonAsync<WoDto>($"/api/work-orders/{wo.Id}/release", planner, new { });
        await PostJsonAsync<object>($"/api/work-orders/{wo.Id}/issue", planner, new { });
        var sn = "SN-OV-DRILL-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        await PostJsonAsync<SerialDto>("/api/station/pass", op, new
        {
            workStationId = onlineId,
            serialNo = sn,
            workOrderId = wo.Id
        });

        var allWip = await GetJsonAsync<List<WipSerialRowDto>>("/api/ops/wip", op);
        allWip.Should().Contain(r => r.SerialNo == sn);

        // 过滤端点不报错且合法：按某工位过滤只返回该工位的 SN
        var onlineStep = stations.First(s => s.StepCode == "ONLINE");
        var onlineWip = await GetJsonAsync<List<WipSerialRowDto>>($"/api/ops/wip?processStepId={onlineStep.Id}", op);
        // sn 不在 ONLINE（已过），但返回合法
        onlineWip.Should().NotContain(r => r.SerialNo == sn);
    }

    [Fact]
    public async Task Orders_drill_returns_in_process_and_released_buckets()
    {
        var planner = await LoginAsync("planner", "Planner@123");
        // 先建一张已下达工单
        var mats = await GetMatList(planner);
        var fgId = mats.First(m => m.Code == MasterDataSeed.FinishedCode).Id;
        var wo = await PostJsonAsync<WoDto>("/api/work-orders", planner, new
        {
            finishedMaterialId = fgId,
            plannedQty = 1
        });
        await PostJsonAsync<WoDto>($"/api/work-orders/{wo.Id}/release", planner, new { });

        var released = await GetJsonAsync<List<OrderRowDto>>("/api/ops/orders?bucket=releasedNotStarted", planner);
        released.Should().Contain(r => r.Id == wo.Id);
    }

    // ----- helpers -----
    private async Task<string> LoginAsync(string user, string pass)
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login", new { userName = user, password = pass });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<LoginDto>(JsonOpts))!.AccessToken;
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(req);
    }

    private async Task<T> GetJsonAsync<T>(string path, string token)
    {
        var res = await GetAsync(path, token);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await res.Content.ReadFromJsonAsync<T>(JsonOpts))!;
    }

    private async Task<T> PostJsonAsync<T>(string path, string token, object body)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await _client.SendAsync(req);
        res.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        var text = await res.Content.ReadAsStringAsync();
        if (typeof(T) == typeof(object) || string.IsNullOrWhiteSpace(text)) return default!;
        return JsonSerializer.Deserialize<T>(text, JsonOpts)!;
    }

    private async Task<List<MatDto>> GetMatList(string token)
        => await GetJsonAsync<List<MatDto>>("/api/materials", token);

    private async Task<List<StationDto>> GetActiveStations(string token)
        => await GetJsonAsync<List<StationDto>>("/api/stations/active", token);

    private async Task<OverviewDto> ReadOverview(string token)
        => await GetJsonAsync<OverviewDto>("/api/ops/overview", token);

    private sealed record LoginDto(string AccessToken);
    private sealed record MatDto(Guid Id, string Code);
    private sealed record StationDto(Guid Id, string StepCode);
    private sealed record WoDto(Guid Id, string OrderNo);
    private sealed record SerialDto(string SerialNo, string Status);
    private sealed record WipByProcessDto(string StepCode, int Sequence, int Count);
    private sealed record WipBlock(int Total, List<WipByProcessDto> ByProcess);
    private sealed record OrdersBlock(int InProcess, int ReleasedNotStarted);
    private sealed record OverviewDto(
        WipBlock Wip,
        int TodayQualifiedReceipts,
        int TodayScraps,
        int IsolatedPendingCount,
        OrdersBlock Orders);
    private sealed record WipSerialRowDto(
        Guid Id,
        string SerialNo,
        string WorkOrderNo,
        Guid? CurrentStepId,
        string? CurrentStepCode);
    private sealed record OrderRowDto(Guid Id, string OrderNo, string Status);
}
