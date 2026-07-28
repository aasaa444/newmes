using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Mes.Api.MasterData;

namespace Mes.Api.Tests;

/// <summary>票 04：过站、防跳站、关键件绑定、谱系。</summary>
public class StationPassSeamTests : IClassFixture<MesApiFactory>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public StationPassSeamTests(MesApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Active_stations_ordered_by_process_step_sequence()
    {
        var token = await LoginAsync("operator", "Operator@123");
        var stations = await GetAsync<List<StationOrderDto>>("/api/stations/active", token);
        stations.Should().NotBeEmpty();
        // 工艺顺序 ONLINE(10)→FLASH(20)→ASSEMBLY(30)→FQC(40)→PACK(50)，不能按 ST-ASM 字母序
        var codes = stations.Select(s => s.StepCode).ToList();
        codes.Should().ContainInOrder("ONLINE", "FLASH", "ASSEMBLY", "FQC", "PACK");
        stations.Select(s => s.StepSequence).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task First_station_pass_creates_serial_and_moves_work_order_in_process()
    {
        var token = await LoginAsync("operator", "Operator@123");
        var planner = await LoginAsync("planner", "Planner@123");
        var (woId, onlineId) = await ReleasedOrderAndOnlineStationAsync(planner);

        var pass = await PostOkAsync<SerialDto>("/api/station/pass", token, new
        {
            workStationId = onlineId,
            serialNo = (string?)null,
            workOrderId = woId
        });
        pass.SerialNo.Should().StartWith("SN-");
        pass.Status.Should().Be("InProcess");
        pass.CurrentStepCode.Should().Be("FLASH"); // 首站 ONLINE 后进入烧录

        // 详情 API 返回 { header: {...}, issueLines: [...] }
        var detail = await GetAsync<WoDetailDto>($"/api/work-orders/{woId}", planner);
        detail.Header.Status.Should().Be("InProcess");
        detail.Header.InProcessSerialCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Anti_skip_rejects_wrong_station()
    {
        var op = await LoginAsync("operator", "Operator@123");
        var planner = await LoginAsync("planner", "Planner@123");
        var (woId, onlineId) = await ReleasedOrderAndOnlineStationAsync(planner);
        var stations = await GetAsync<List<StationDto>>("/api/stations/active", op);
        var flashId = stations.First(s => s.StepCode == "FLASH").Id;

        var created = await PostOkAsync<SerialDto>("/api/station/pass", op, new
        {
            workStationId = onlineId,
            serialNo = "SN-ANTISKIP-001",
            workOrderId = woId
        });

        // 当前应在 FLASH，却在 FQC 过站 → 拒绝
        var fqcId = stations.First(s => s.StepCode == "FQC").Id;
        var res = await SendAsync(HttpMethod.Post, "/api/station/pass", op, new
        {
            workStationId = fqcId,
            serialNo = created.SerialNo,
            workOrderId = (Guid?)null
        });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("防跳站");

        // 正确在 FLASH 过站
        var ok = await PostOkAsync<SerialDto>("/api/station/pass", op, new
        {
            workStationId = flashId,
            serialNo = created.SerialNo
        });
        ok.CurrentStepCode.Should().Be("ASSEMBLY");
    }

    [Fact]
    public async Task Bind_key_component_and_genealogy_shows_passes_and_bindings()
    {
        var op = await LoginAsync("operator", "Operator@123");
        var planner = await LoginAsync("planner", "Planner@123");
        var (woId, onlineId) = await ReleasedOrderAndOnlineStationAsync(planner);

        // 领料后才有 Pending
        await PostOkAsync<object>($"/api/work-orders/{woId}/issue", planner, new { });

        var pass = await PostOkAsync<SerialDto>("/api/station/pass", op, new
        {
            workStationId = onlineId,
            serialNo = "SN-GENE-001",
            workOrderId = woId
        });

        var materials = await GetAsync<List<MatDto>>("/api/materials", planner);
        var pcbId = materials.First(m => m.Code == MasterDataSeed.PcbCode).Id;

        await PostOkAsync<object>("/api/station/bind-component", op, new
        {
            productSerialNo = pass.SerialNo,
            componentMaterialId = pcbId,
            componentSerialNo = "PCB-SN-999",
            workStationId = onlineId
        });

        var g = await GetAsync<GenealogyDto>($"/api/genealogy/{pass.SerialNo}", planner);
        g.SerialNo.Should().Be(pass.SerialNo);
        g.Passes.Should().Contain(p => p.StepCode == "ONLINE" && p.Result == "Pass");
        g.Bindings.Should().Contain(b => b.ComponentSerialNo == "PCB-SN-999" && b.ComponentMaterialCode == MasterDataSeed.PcbCode);
    }

    [Fact]
    public async Task Cannot_bind_without_pending_issue()
    {
        var op = await LoginAsync("operator", "Operator@123");
        var planner = await LoginAsync("planner", "Planner@123");
        // 下达但不领料
        var (woId, onlineId) = await ReleasedOrderAndOnlineStationAsync(planner);
        var pass = await PostOkAsync<SerialDto>("/api/station/pass", op, new
        {
            workStationId = onlineId,
            serialNo = "SN-NOPEND-1",
            workOrderId = woId
        });
        var materials = await GetAsync<List<MatDto>>("/api/materials", planner);
        var pcbId = materials.First(m => m.Code == MasterDataSeed.PcbCode).Id;
        var res = await SendAsync(HttpMethod.Post, "/api/station/bind-component", op, new
        {
            productSerialNo = pass.SerialNo,
            componentMaterialId = pcbId,
            componentSerialNo = "PCB-X",
            workStationId = onlineId
        });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Pass_and_bind_write_audit()
    {
        var op = await LoginAsync("operator", "Operator@123");
        var planner = await LoginAsync("planner", "Planner@123");
        var (woId, onlineId) = await ReleasedOrderAndOnlineStationAsync(planner);
        await PostOkAsync<object>($"/api/work-orders/{woId}/issue", planner, new { });
        var pass = await PostOkAsync<SerialDto>("/api/station/pass", op, new
        {
            workStationId = onlineId,
            serialNo = "SN-AUD-04",
            workOrderId = woId
        });
        var pcbId = (await GetAsync<List<MatDto>>("/api/materials", planner))
            .First(m => m.Code == MasterDataSeed.PcbCode).Id;
        await PostOkAsync<object>("/api/station/bind-component", op, new
        {
            productSerialNo = pass.SerialNo,
            componentMaterialId = pcbId,
            componentSerialNo = "PCB-AUD-1",
            workStationId = onlineId
        });
        var audit = await GetAsync<List<AuditDto>>("/api/audit", planner);
        audit.Should().Contain(a => a.Action == "StationPass");
        audit.Should().Contain(a => a.Action == "ComponentBound");
    }

    [Fact]
    public async Task Full_route_pass_marks_route_completed()
    {
        var op = await LoginAsync("operator", "Operator@123");
        var planner = await LoginAsync("planner", "Planner@123");
        var (woId, _) = await ReleasedOrderAndOnlineStationAsync(planner);
        var stations = await GetAsync<List<StationDto>>("/api/stations/active", op);
        string? sn = null;
        foreach (var stepCode in new[] { "ONLINE", "FLASH", "ASSEMBLY", "FQC", "PACK" })
        {
            var stId = stations.First(s => s.StepCode == stepCode).Id;
            var body = sn is null
                ? (object)new { workStationId = stId, serialNo = "SN-FULL-ROUTE", workOrderId = woId }
                : new { workStationId = stId, serialNo = sn, workOrderId = (Guid?)null };
            var result = await PostOkAsync<SerialDto>("/api/station/pass", op, body);
            sn = result.SerialNo;
            if (stepCode == "PACK")
            {
                result.Status.Should().Be("RouteCompleted");
                result.CurrentStepCode.Should().BeNull();
            }
        }

        var g = await GetAsync<GenealogyDto>($"/api/genealogy/{sn}", planner);
        g.Passes.Should().HaveCount(5);
    }

    private async Task<(Guid WoId, Guid OnlineStationId)> ReleasedOrderAndOnlineStationAsync(string plannerToken)
    {
        var mats = await GetAsync<List<MatDto>>("/api/materials", plannerToken);
        var fgId = mats.First(m => m.Code == MasterDataSeed.FinishedCode).Id;
        var wo = await PostOkAsync<WoDto>("/api/work-orders", plannerToken, new
        {
            finishedMaterialId = fgId,
            plannedQty = 3
        });
        await PostOkAsync<WoDto>($"/api/work-orders/{wo.Id}/release", plannerToken, new { });
        var stations = await GetAsync<List<StationDto>>("/api/stations/active", plannerToken);
        var online = stations.First(s => s.StepCode == "ONLINE").Id;
        return (wo.Id, online);
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
        if (typeof(T) == typeof(object) || text is "{}" or "")
        {
            return default!;
        }

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
    private sealed record StationOrderDto(Guid Id, string Code, string StepCode, int StepSequence);
    private sealed record SerialDto(string SerialNo, string Status, string? CurrentStepCode);
    private sealed record WoDto(Guid Id, string Status, int InProcessSerialCount);
    private sealed record WoDetailDto(WoDto Header);
    private sealed record PassDto(string StepCode, string Result);
    private sealed record BindDto(string ComponentSerialNo, string ComponentMaterialCode);
    private sealed record GenealogyDto(string SerialNo, List<PassDto> Passes, List<BindDto> Bindings);
    private sealed record AuditDto(string Action);
}
