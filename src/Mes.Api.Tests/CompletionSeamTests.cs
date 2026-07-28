using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Mes.Api.MasterData;

namespace Mes.Api.Tests;

/// <summary>票 06：完工入库、成品账、隔离拦截、ERP 出站、关单。</summary>
public class CompletionSeamTests : IClassFixture<MesApiFactory>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public CompletionSeamTests(MesApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Complete_route_then_receive_increases_finished_goods_and_erp_receipt()
    {
        var op = await LoginAsync("operator", "Operator@123");
        var planner = await LoginAsync("planner", "Planner@123");
        var (woId, sn) = await FullRouteSerialAsync(op, planner, plannedQty: 1);

        var beforeFg = await GetAsync<List<FgDto>>("/api/inventory/finished-goods", planner);
        var fgBefore = beforeFg.FirstOrDefault(x => x.MaterialCode == MasterDataSeed.FinishedCode)?.QuantityOnHand ?? 0;

        var done = await PostOkAsync<SerialDto>("/api/completion/receive", op, new { serialNo = sn });
        done.Status.Should().Be("Completed");

        var afterFg = await GetAsync<List<FgDto>>("/api/inventory/finished-goods", planner);
        afterFg.Should().Contain(x => x.MaterialCode == MasterDataSeed.FinishedCode && x.QuantityOnHand == fgBefore + 1);

        var wo = await GetAsync<WoDetailDto>($"/api/work-orders/{woId}", planner);
        wo.Header.CompletedQty.Should().Be(1);
        wo.Header.Status.Should().Be("Completed");
        wo.Header.InProcessSerialCount.Should().Be(0);

        var outbox = await GetAsync<List<OutboxDto>>("/api/erp/outbox", planner);
        outbox.Should().Contain(m => m.MessageType == "ProductionReceipt" && m.BusinessKey!.Contains(sn));
        outbox.Should().Contain(m => m.MessageType == "MaterialIssue");
        outbox.First(m => m.MessageType == "ProductionReceipt").PayloadJson.Should().Contain("MO_Receipt");
    }

    [Fact]
    public async Task Isolated_serial_cannot_receive_to_finished_goods()
    {
        var op = await LoginAsync("operator", "Operator@123");
        var planner = await LoginAsync("planner", "Planner@123");
        var (_, onlineId, flashId, sn) = await SerialAtFlashAsync(op, planner);

        await PostOkAsync<SerialDto>("/api/station/fail", op, new
        {
            workStationId = flashId,
            serialNo = sn,
            disposition = "Isolate",
            reason = "hold for complete test"
        });

        // 即使强行走完路线也不该——隔离态直接拦入库
        var res = await SendAsync(HttpMethod.Post, "/api/completion/receive", op, new { serialNo = sn });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.Content.ReadAsStringAsync()).Should().Contain("isolated");
    }

    [Fact]
    public async Task Scrapped_serial_cannot_receive()
    {
        var op = await LoginAsync("operator", "Operator@123");
        var planner = await LoginAsync("planner", "Planner@123");
        var (_, _, flashId, sn) = await SerialAtFlashAsync(op, planner);
        await PostOkAsync<SerialDto>("/api/station/fail", op, new
        {
            workStationId = flashId,
            serialNo = sn,
            disposition = "Scrap",
            reason = "ng"
        });
        var res = await SendAsync(HttpMethod.Post, "/api/completion/receive", op, new { serialNo = sn });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Close_work_order_then_issue_is_rejected()
    {
        var op = await LoginAsync("operator", "Operator@123");
        var planner = await LoginAsync("planner", "Planner@123");
        var (woId, sn) = await FullRouteSerialAsync(op, planner, plannedQty: 1);
        await PostOkAsync<SerialDto>("/api/completion/receive", op, new { serialNo = sn });

        var closed = await PostOkAsync<CloseDto>($"/api/work-orders/{woId}/close", planner, new { });
        closed.Status.Should().Be("Closed");

        var outbox = await GetAsync<List<OutboxDto>>("/api/erp/outbox", planner);
        outbox.Should().Contain(m => m.MessageType == "WorkOrderClose");

        var issueRes = await SendAsync(HttpMethod.Post, $"/api/work-orders/{woId}/issue", planner, new { });
        issueRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await issueRes.Content.ReadAsStringAsync()).Should().Contain("closed");
    }

    [Fact]
    public async Task Complete_and_close_write_audit()
    {
        var op = await LoginAsync("operator", "Operator@123");
        var planner = await LoginAsync("planner", "Planner@123");
        var (woId, sn) = await FullRouteSerialAsync(op, planner, plannedQty: 1);
        await PostOkAsync<SerialDto>("/api/completion/receive", op, new { serialNo = sn });
        await PostOkAsync<CloseDto>($"/api/work-orders/{woId}/close", planner, new { });

        var audit = await GetAsync<List<AuditDto>>("/api/audit", planner);
        audit.Should().Contain(a => a.Action == "FinishedGoodsReceived");
        audit.Should().Contain(a => a.Action == "WorkOrderClosed");
    }

    private async Task<(Guid WoId, string SerialNo)> FullRouteSerialAsync(string op, string planner, int plannedQty)
    {
        var mats = await GetAsync<List<MatDto>>("/api/materials", planner);
        var fgId = mats.First(m => m.Code == MasterDataSeed.FinishedCode).Id;
        var wo = await PostOkAsync<WoDto>("/api/work-orders", planner, new
        {
            finishedMaterialId = fgId,
            plannedQty
        });
        await PostOkAsync<WoDto>($"/api/work-orders/{wo.Id}/release", planner, new { });
        await PostOkAsync<object>($"/api/work-orders/{wo.Id}/issue", planner, new { });

        var stations = await GetAsync<List<StationDto>>("/api/stations/active", op);
        string? sn = null;
        foreach (var stepCode in new[] { "ONLINE", "FLASH", "ASSEMBLY", "FQC", "PACK" })
        {
            var stId = stations.First(s => s.StepCode == stepCode).Id;
            var body = sn is null
                ? (object)new { workStationId = stId, serialNo = $"SN-C6-{Guid.NewGuid():N}"[..16].ToUpperInvariant(), workOrderId = wo.Id }
                : new { workStationId = stId, serialNo = sn, workOrderId = (Guid?)null };
            var result = await PostOkAsync<SerialDto>("/api/station/pass", op, body);
            sn = result.SerialNo;
        }

        return (wo.Id, sn!);
    }

    private async Task<(Guid WoId, Guid OnlineId, Guid FlashId, string SerialNo)> SerialAtFlashAsync(string op, string planner)
    {
        var mats = await GetAsync<List<MatDto>>("/api/materials", planner);
        var fgId = mats.First(m => m.Code == MasterDataSeed.FinishedCode).Id;
        var wo = await PostOkAsync<WoDto>("/api/work-orders", planner, new
        {
            finishedMaterialId = fgId,
            plannedQty = 3
        });
        await PostOkAsync<WoDto>($"/api/work-orders/{wo.Id}/release", planner, new { });
        await PostOkAsync<object>($"/api/work-orders/{wo.Id}/issue", planner, new { });
        var stations = await GetAsync<List<StationDto>>("/api/stations/active", op);
        var onlineId = stations.First(s => s.StepCode == "ONLINE").Id;
        var flashId = stations.First(s => s.StepCode == "FLASH").Id;
        var sn = $"SN-C6F-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
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
        if (typeof(T) == typeof(object) || string.IsNullOrWhiteSpace(text)) return default!;
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
    private sealed record StationDto(Guid Id, string StepCode);
    private sealed record SerialDto(string SerialNo, string Status);
    private sealed record WoDto(Guid Id, string Status, decimal CompletedQty, decimal ScrappedQty, int InProcessSerialCount);
    private sealed record WoDetailDto(WoDto Header);
    private sealed record FgDto(string MaterialCode, decimal QuantityOnHand);
    private sealed record OutboxDto(string MessageType, string? BusinessKey, string PayloadJson);
    private sealed record CloseDto(string Status);
    private sealed record AuditDto(string Action);
}
