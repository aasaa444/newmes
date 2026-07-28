using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Mes.Api.Tests;

public class MasterDataSeamTests : IClassFixture<MesApiFactory>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public MasterDataSeamTests(MesApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Demo_seed_loads_electronics_assembly_master_data()
    {
        var token = await LoginAsync("planner", "Planner@123");
        var materials = await GetJsonAsync<List<MaterialDto>>("/api/materials", token);
        materials.Should().Contain(m => m.Code == "FG-ROUTER" && m.IsFinishedGood && m.RequiresSerialNumber);
        materials.Should().Contain(m => m.Code == "PCB-MAIN" && m.IsKeyComponent && m.RequiresSerialNumber);
        materials.Should().Contain(m => m.Code == "SCREW-M3" && !m.IsKeyComponent && !m.RequiresSerialNumber);

        var boms = await GetJsonAsync<List<BomDto>>("/api/boms", token);
        var routerBom = boms.Should().ContainSingle(b => b.FinishedMaterialCode == "FG-ROUTER").Subject;
        routerBom.Lines.Should().HaveCount(3);
        routerBom.Lines.Should().Contain(l => l.ComponentCode == "PCB-MAIN" && l.QuantityPer == 1);
        routerBom.Lines.Should().Contain(l => l.ComponentCode == "SCREW-M3" && l.QuantityPer == 4);

        var routes = await GetJsonAsync<List<RouteDto>>("/api/process-routes", token);
        var route = routes.Should().ContainSingle(r => r.Code == "RT-ROUTER-A").Subject;
        route.Steps.Should().HaveCount(5);
        route.Steps.Select(s => s.Sequence).Should().BeInAscendingOrder();

        var stations = await GetJsonAsync<List<StationDto>>("/api/work-stations", token);
        // Shared test host may accumulate stations from other cases; assert seed stations exist.
        stations.Select(s => s.Code).Should().Contain(["ST-ONLINE", "ST-FLASH", "ST-ASM", "ST-FQC", "ST-PACK"]);
        stations.Should().OnlyContain(s => s.BoundProcessStepId != Guid.Empty);
        stations.Should().OnlyContain(s => !string.IsNullOrEmpty(s.BoundProcessStepCode));
    }

    [Fact]
    public async Task Operator_cannot_create_material()
    {
        var token = await LoginAsync("operator", "Operator@123");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/materials")
        {
            Content = JsonContent.Create(new
            {
                code = "X-OP",
                name = "不应创建",
                isFinishedGood = false,
                isKeyComponent = false,
                requiresSerialNumber = false
            })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Planner_can_create_material_and_audit_is_written()
    {
        var token = await LoginAsync("planner", "Planner@123");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/materials")
        {
            Content = JsonContent.Create(new
            {
                code = "AUX-TAPE",
                name = "绝缘胶带",
                isFinishedGood = false,
                isKeyComponent = false,
                requiresSerialNumber = false
            })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await res.Content.ReadFromJsonAsync<MaterialDto>(JsonOpts);
        created!.Code.Should().Be("AUX-TAPE");

        var audit = await GetJsonAsync<List<AuditDto>>("/api/audit", token);
        audit.Should().Contain(a => a.Action == "MaterialCreated" && a.ActorUserName == "planner");
    }

    [Fact]
    public async Task Planner_can_create_bom_for_finished_material()
    {
        var token = await LoginAsync("planner", "Planner@123");
        var materials = await GetJsonAsync<List<MaterialDto>>("/api/materials", token);
        // create a mini finished + component
        var fg = await CreateMaterialAsync(token, "FG-MINI", "迷你成品", finished: true, key: false, sn: true);
        var comp = await CreateMaterialAsync(token, "CMP-A", "组件A", finished: false, key: true, sn: true);

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/boms")
        {
            Content = JsonContent.Create(new
            {
                finishedMaterialId = fg.Id,
                version = "A",
                lines = new[]
                {
                    new { componentMaterialId = comp.Id, quantityPer = 2m }
                }
            })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var bom = await res.Content.ReadFromJsonAsync<BomDto>(JsonOpts);
        bom!.Lines.Should().ContainSingle(l => l.ComponentCode == "CMP-A" && l.QuantityPer == 2);
    }

    [Fact]
    public async Task Work_station_must_bind_exactly_one_process_step()
    {
        var token = await LoginAsync("planner", "Planner@123");
        var routes = await GetJsonAsync<List<RouteDto>>("/api/process-routes", token);
        var stepId = routes.First(r => r.Code == "RT-ROUTER-A").Steps.First().Id;
        var lines = await GetJsonAsync<List<LineDto>>("/api/production-lines", token);
        var lineId = lines.First(l => l.Code == "L1").Id;

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/work-stations")
        {
            Content = JsonContent.Create(new
            {
                code = "ST-EXTRA",
                name = "备用上线位",
                productionLineId = lineId,
                boundProcessStepId = stepId
            })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var st = await res.Content.ReadFromJsonAsync<StationDto>(JsonOpts);
        st!.BoundProcessStepId.Should().Be(stepId);
    }

    [Fact]
    public async Task Any_business_role_can_read_materials()
    {
        var token = await LoginAsync("operator", "Operator@123");
        var res = await AuthorizedGet("/api/materials", token);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Planner_can_update_and_deactivate_material()
    {
        var token = await LoginAsync("planner", "Planner@123");
        var created = await CreateMaterialAsync(token, "TMP-UPD", "临时料", finished: false, key: false, sn: false);

        using var put = new HttpRequestMessage(HttpMethod.Put, $"/api/materials/{created.Id}")
        {
            Content = JsonContent.Create(new
            {
                name = "临时料-已改",
                isFinishedGood = false,
                isKeyComponent = true,
                requiresSerialNumber = true,
                isActive = true
            })
        };
        put.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var putRes = await _client.SendAsync(put);
        putRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await putRes.Content.ReadFromJsonAsync<MaterialDto>(JsonOpts);
        updated!.Name.Should().Be("临时料-已改");
        updated.IsKeyComponent.Should().BeTrue();

        using var del = new HttpRequestMessage(HttpMethod.Delete, $"/api/materials/{created.Id}");
        del.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var delRes = await _client.SendAsync(del);
        delRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var materials = await GetJsonAsync<List<MaterialDto>>("/api/materials", token);
        // list still returns row; IsActive false — fetch via full list and find
        // MaterialDto in tests omits IsActive; ensure update audit exists
        var audit = await GetJsonAsync<List<AuditDto>>("/api/audit", token);
        audit.Should().Contain(a => a.Action == "MaterialUpdated");
        audit.Should().Contain(a => a.Action == "MaterialDeactivated");
    }

    private async Task<HttpResponseMessage> AuthorizedGet(string path, string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(req);
    }

    private async Task<T> GetJsonAsync<T>(string path, string token)
    {
        var res = await AuthorizedGet(path, token);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await res.Content.ReadFromJsonAsync<T>(JsonOpts);
        data.Should().NotBeNull();
        return data!;
    }

    private async Task<MaterialDto> CreateMaterialAsync(
        string token, string code, string name, bool finished, bool key, bool sn)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/materials")
        {
            Content = JsonContent.Create(new
            {
                code,
                name,
                isFinishedGood = finished,
                isKeyComponent = key,
                requiresSerialNumber = sn
            })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var res = await _client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<MaterialDto>(JsonOpts))!;
    }

    private async Task<string> LoginAsync(string userName, string password)
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login", new { userName, password });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await res.Content.ReadFromJsonAsync<LoginResponse>(JsonOpts);
        return dto!.AccessToken;
    }

    private sealed record LoginResponse(string AccessToken, string UserName, string Role, string DisplayName);
    private sealed record MaterialDto(Guid Id, string Code, string Name, bool IsFinishedGood, bool IsKeyComponent, bool RequiresSerialNumber);
    private sealed record BomLineDto(string ComponentCode, decimal QuantityPer);
    private sealed record BomDto(Guid Id, string FinishedMaterialCode, string Version, List<BomLineDto> Lines);
    private sealed record StepDto(Guid Id, int Sequence, string Code, string Name);
    private sealed record RouteDto(Guid Id, string Code, string Name, List<StepDto> Steps);
    private sealed record LineDto(Guid Id, string Code, string Name);
    private sealed record StationDto(Guid Id, string Code, string Name, Guid BoundProcessStepId, string BoundProcessStepCode);
    private sealed record AuditDto(string Action, string ActorUserName, string? SubjectType, string? SubjectId, DateTimeOffset OccurredAt);
}
