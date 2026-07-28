using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Mes.Api.MasterData;

namespace Mes.Api.Tests;

/// <summary>
/// 票 07：第一期完成定义高缝验收。
/// 单测内串联黄金路径 + 异常支线 + 三角色 + ERP/审计，证据即测试绿。
/// </summary>
public class Phase1E2eSeamTests : IClassFixture<MesApiFactory>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public Phase1E2eSeamTests(MesApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Phase1_golden_path_bind_complete_close_with_erp_and_audit()
    {
        var planner = await LoginAsync("planner", "Planner@123");
        var op = await LoginAsync("operator", "Operator@123");
        var leader = await LoginAsync("leader", "Leader@123");

        // 三角色均可读主数据/工单
        (await SendAsync(HttpMethod.Get, "/api/materials", planner, null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, "/api/materials", op, null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, "/api/work-orders", leader, null)).StatusCode.Should().Be(HttpStatusCode.OK);

        // 操作工不可写主数据
        (await SendAsync(HttpMethod.Post, "/api/materials", op, new
        {
            code = "E2E-X",
            name = "nope",
            isFinishedGood = false,
            isKeyComponent = false,
            requiresSerialNumber = false
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var mats = await GetAsync<List<MatDto>>("/api/materials", planner);
        var fgId = mats.First(m => m.Code == MasterDataSeed.FinishedCode).Id;
        var pcbId = mats.First(m => m.Code == MasterDataSeed.PcbCode).Id;

        var wo = await PostOkAsync<WoDto>("/api/work-orders", planner, new
        {
            finishedMaterialId = fgId,
            plannedQty = 1
        });
        await PostOkAsync<WoDto>($"/api/work-orders/{wo.Id}/release", planner, new { });
        await PostOkAsync<object>($"/api/work-orders/{wo.Id}/issue", planner, new { });

        var stations = await GetAsync<List<StationDto>>("/api/stations/active", op);
        var sn = $"SN-E2E-G-{Guid.NewGuid():N}"[..18].ToUpperInvariant();

        // 防跳站：未上线就去 FLASH
        var flashId = stations.First(s => s.StepCode == "FLASH").Id;
        var skip = await SendAsync(HttpMethod.Post, "/api/station/pass", op, new
        {
            workStationId = flashId,
            serialNo = sn,
            workOrderId = wo.Id
        });
        // 新 SN 必须在首站；若当新开则会因非首站失败
        skip.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        foreach (var stepCode in new[] { "ONLINE", "FLASH", "ASSEMBLY", "FQC", "PACK" })
        {
            var stId = stations.First(s => s.StepCode == stepCode).Id;
            object body = stepCode == "ONLINE"
                ? new { workStationId = stId, serialNo = sn, workOrderId = wo.Id }
                : new { workStationId = stId, serialNo = sn, workOrderId = (Guid?)null };
            await PostOkAsync<SerialDto>("/api/station/pass", op, body);

            if (stepCode == "ONLINE")
            {
                await PostOkAsync<object>("/api/station/bind-component", op, new
                {
                    productSerialNo = sn,
                    componentMaterialId = pcbId,
                    componentSerialNo = $"PCB-E2E-{Guid.NewGuid():N}"[..16].ToUpperInvariant(),
                    workStationId = stId
                });
            }
        }

        await PostOkAsync<SerialDto>("/api/completion/receive", op, new { serialNo = sn });
        await PostOkAsync<CloseDto>($"/api/work-orders/{wo.Id}/close", planner, new { });

        var g = await GetAsync<GenealogyDto>($"/api/genealogy/{sn}", leader);
        g.Passes.Count(p => p.Result == "Pass").Should().BeGreaterThanOrEqualTo(5);
        g.Bindings.Should().NotBeEmpty();
        g.Status.Should().Be("Completed");

        var outbox = await GetAsync<List<OutboxDto>>("/api/erp/outbox", planner);
        outbox.Should().Contain(m => m.MessageType == "MaterialIssue");
        outbox.Should().Contain(m => m.MessageType == "ProductionReceipt");
        outbox.Should().Contain(m => m.MessageType == "WorkOrderClose");

        var audit = await GetAsync<List<AuditDto>>("/api/audit", planner);
        audit.Should().Contain(a => a.Action == "WorkOrderReleased");
        audit.Should().Contain(a => a.Action == "StationPass");
        audit.Should().Contain(a => a.Action == "ComponentBound");
        audit.Should().Contain(a => a.Action == "FinishedGoodsReceived");
        audit.Should().Contain(a => a.Action == "WorkOrderClosed");

        var fg = await GetAsync<List<FgDto>>("/api/inventory/finished-goods", planner);
        fg.Should().Contain(x => x.MaterialCode == MasterDataSeed.FinishedCode && x.QuantityOnHand >= 1);
    }

    [Fact]
    public async Task Phase1_exception_isolate_release_and_scrap_branch()
    {
        var planner = await LoginAsync("planner", "Planner@123");
        var op = await LoginAsync("operator", "Operator@123");

        var mats = await GetAsync<List<MatDto>>("/api/materials", planner);
        var fgId = mats.First(m => m.Code == MasterDataSeed.FinishedCode).Id;
        var wo = await PostOkAsync<WoDto>("/api/work-orders", planner, new
        {
            finishedMaterialId = fgId,
            plannedQty = 2
        });
        await PostOkAsync<WoDto>($"/api/work-orders/{wo.Id}/release", planner, new { });
        await PostOkAsync<object>($"/api/work-orders/{wo.Id}/issue", planner, new { });

        var stations = await GetAsync<List<StationDto>>("/api/stations/active", op);
        var onlineId = stations.First(s => s.StepCode == "ONLINE").Id;
        var flashId = stations.First(s => s.StepCode == "FLASH").Id;

        // 支线 A：隔离 → 放行 → 继续过站 → 入库
        var snA = $"SN-E2E-A-{Guid.NewGuid():N}"[..18].ToUpperInvariant();
        await PostOkAsync<SerialDto>("/api/station/pass", op, new
        {
            workStationId = onlineId,
            serialNo = snA,
            workOrderId = wo.Id
        });
        await PostOkAsync<SerialDto>("/api/station/fail", op, new
        {
            workStationId = flashId,
            serialNo = snA,
            disposition = "Isolate",
            reason = "e2e hold"
        });
        (await SendAsync(HttpMethod.Post, "/api/completion/receive", op, new { serialNo = snA }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await PostOkAsync<SerialDto>("/api/quality/release", planner, new
        {
            serialNo = snA,
            reason = "e2e release"
        });

        foreach (var stepCode in new[] { "FLASH", "ASSEMBLY", "FQC", "PACK" })
        {
            var stId = stations.First(s => s.StepCode == stepCode).Id;
            await PostOkAsync<SerialDto>("/api/station/pass", op, new
            {
                workStationId = stId,
                serialNo = snA
            });
        }

        await PostOkAsync<SerialDto>("/api/completion/receive", op, new { serialNo = snA });

        // 支线 B：报废
        var snB = $"SN-E2E-B-{Guid.NewGuid():N}"[..18].ToUpperInvariant();
        await PostOkAsync<SerialDto>("/api/station/pass", op, new
        {
            workStationId = onlineId,
            serialNo = snB,
            workOrderId = wo.Id
        });
        await PostOkAsync<SerialDto>("/api/station/fail", op, new
        {
            workStationId = flashId,
            serialNo = snB,
            disposition = "Scrap",
            reason = "e2e scrap"
        });

        var detail = await GetAsync<WoDetailDto>($"/api/work-orders/{wo.Id}", planner);
        detail.Header.CompletedQty.Should().Be(1);
        detail.Header.ScrappedQty.Should().Be(1);

        // 有在制时不可取消；本单已无在制则可关单
        detail.Header.InProcessSerialCount.Should().Be(0);
        await PostOkAsync<CloseDto>($"/api/work-orders/{wo.Id}/close", planner, new { });

        var gA = await GetAsync<GenealogyDto>($"/api/genealogy/{snA}", planner);
        gA.Passes.Should().Contain(p => p.Result == "Fail");
        gA.Passes.Should().Contain(p => p.Result == "Release");
        gA.Status.Should().Be("Completed");

        var gB = await GetAsync<GenealogyDto>($"/api/genealogy/{snB}", planner);
        gB.Status.Should().Be("Scrapped");
    }

    [Fact]
    public async Task Phase1_cancel_only_without_wip()
    {
        var planner = await LoginAsync("planner", "Planner@123");
        var op = await LoginAsync("operator", "Operator@123");
        var mats = await GetAsync<List<MatDto>>("/api/materials", planner);
        var fgId = mats.First(m => m.Code == MasterDataSeed.FinishedCode).Id;

        var wo = await PostOkAsync<WoDto>("/api/work-orders", planner, new
        {
            finishedMaterialId = fgId,
            plannedQty = 1
        });
        await PostOkAsync<WoDto>($"/api/work-orders/{wo.Id}/release", planner, new { });

        // 无在制可取消
        var cancelled = await PostOkAsync<WoDto>($"/api/work-orders/{wo.Id}/cancel", planner, new { });
        cancelled.Status.Should().Be("Cancelled");

        // 再建一单并上线后不可取消
        var wo2 = await PostOkAsync<WoDto>("/api/work-orders", planner, new
        {
            finishedMaterialId = fgId,
            plannedQty = 1
        });
        await PostOkAsync<WoDto>($"/api/work-orders/{wo2.Id}/release", planner, new { });
        await PostOkAsync<object>($"/api/work-orders/{wo2.Id}/issue", planner, new { });
        var stations = await GetAsync<List<StationDto>>("/api/stations/active", op);
        var onlineId = stations.First(s => s.StepCode == "ONLINE").Id;
        await PostOkAsync<SerialDto>("/api/station/pass", op, new
        {
            workStationId = onlineId,
            serialNo = $"SN-E2E-C-{Guid.NewGuid():N}"[..16].ToUpperInvariant(),
            workOrderId = wo2.Id
        });
        var bad = await SendAsync(HttpMethod.Post, $"/api/work-orders/{wo2.Id}/cancel", planner, new { });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
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
    private sealed record PassDto(string StepCode, string Result);
    private sealed record BindDto(string ComponentSerialNo);
    private sealed record GenealogyDto(string SerialNo, string Status, List<PassDto> Passes, List<BindDto> Bindings);
    private sealed record OutboxDto(string MessageType);
    private sealed record AuditDto(string Action);
    private sealed record FgDto(string MaterialCode, decimal QuantityOnHand);
    private sealed record CloseDto(string Status);
}
