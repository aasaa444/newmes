using Mes.Api.Data;

namespace Mes.Api.MasterData;

/// <summary>
/// 离散电子组装演示种子（路由器类成品）。
/// 幂等：已存在 FG-ROUTER 则整段跳过。
/// 结构：1 成品 + 2 关键件(SN) + 1 辅料 → 单层 BOM → 5 步线性路线 → 一线 5 工位。
/// </summary>
public static class MasterDataSeed
{
    public static readonly string FinishedCode = "FG-ROUTER";
    public static readonly string PcbCode = "PCB-MAIN";
    public static readonly string PsuCode = "PSU-MOD";
    public static readonly string ScrewCode = "SCREW-M3";
    public static readonly string RouteCode = "RT-ROUTER-A";
    public static readonly string LineCode = "L1";

    public static void EnsureSeeded(MesDbContext db)
    {
        if (db.Materials.Any(m => m.Code == FinishedCode))
        {
            return;
        }

        var fg = Mat(FinishedCode, "演示路由器整机", finished: true, key: false, sn: true);
        var pcb = Mat(PcbCode, "主板 PCB", finished: false, key: true, sn: true);
        var psu = Mat(PsuCode, "电源模块", finished: false, key: true, sn: true);
        var screw = Mat(ScrewCode, "M3 螺丝", finished: false, key: false, sn: false);
        db.Materials.AddRange(fg, pcb, psu, screw);
        db.SaveChanges();

        var bom = new Bom
        {
            Id = Guid.NewGuid(),
            FinishedMaterialId = fg.Id,
            Version = "A",
            IsActive = true,
            Lines =
            [
                new BomLine { Id = Guid.NewGuid(), ComponentMaterialId = pcb.Id, QuantityPer = 1 },
                new BomLine { Id = Guid.NewGuid(), ComponentMaterialId = psu.Id, QuantityPer = 1 },
                new BomLine { Id = Guid.NewGuid(), ComponentMaterialId = screw.Id, QuantityPer = 4 },
            ]
        };
        db.Boms.Add(bom);

        var route = new ProcessRoute
        {
            Id = Guid.NewGuid(),
            Code = RouteCode,
            Name = "路由器组装默认路线",
            FinishedMaterialId = fg.Id,
            Version = "1",
            IsActive = true
        };
        // Sequence 10..50；烧录失败可回 10；终检失败可回 30
        var steps = new[]
        {
            Step(route.Id, 10, "ONLINE", "上线装配", false, null),
            Step(route.Id, 20, "FLASH", "烧录测试", false, 10),
            Step(route.Id, 30, "ASSEMBLY", "外壳组装", false, 10),
            Step(route.Id, 40, "FQC", "终检", true, 30),
            Step(route.Id, 50, "PACK", "包装", false, null),
        };
        route.Steps.AddRange(steps);
        db.ProcessRoutes.Add(route);
        db.SaveChanges();

        var line = new ProductionLine
        {
            Id = Guid.NewGuid(),
            Code = LineCode,
            Name = "组装一线",
            IsActive = true
        };
        db.ProductionLines.Add(line);
        db.SaveChanges();

        // 一工序一工位，过站台选工位即锁定当前工序
        db.WorkStations.AddRange(
            Station("ST-ONLINE", "上线工位", line.Id, steps[0].Id),
            Station("ST-FLASH", "烧录工位", line.Id, steps[1].Id),
            Station("ST-ASM", "组装工位", line.Id, steps[2].Id),
            Station("ST-FQC", "终检工位", line.Id, steps[3].Id),
            Station("ST-PACK", "包装工位", line.Id, steps[4].Id)
        );
        db.SaveChanges();
    }

    private static Material Mat(string code, string name, bool finished, bool key, bool sn) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = name,
        IsFinishedGood = finished,
        IsKeyComponent = key,
        RequiresSerialNumber = sn,
        IsActive = true
    };

    private static ProcessStep Step(Guid routeId, int seq, string code, string name, bool quality, int? rework) => new()
    {
        Id = Guid.NewGuid(),
        ProcessRouteId = routeId,
        Sequence = seq,
        Code = code,
        Name = name,
        IsQualityStep = quality,
        ReworkToSequence = rework
    };

    private static WorkStation Station(string code, string name, Guid lineId, Guid stepId) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = name,
        ProductionLineId = lineId,
        BoundProcessStepId = stepId,
        IsActive = true
    };
}
