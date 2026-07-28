# -*- coding: utf-8 -*-
"""Rewrite user-visible English errors to Chinese (UTF-8)."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
files = [
    ROOT / "src/Mes.Api/Execution/StationPassService.cs",
    ROOT / "src/Mes.Api/Execution/WorkOrderService.cs",
    ROOT / "src/Mes.Api/Execution/QualityService.cs",
    ROOT / "src/Mes.Api/Execution/CompletionService.cs",
    ROOT / "src/Mes.Api/MasterData/MasterDataEndpoints.cs",
]

# Longer / more specific strings first
pairs = [
    (
        "no issued quantity for this component on the work order; issue material first",
        "本工单尚未领用该组件，请先领料再绑定关键件",
    ),
    (
        "no pending (unbound) quantity for this key component on the work order",
        "本工单该关键件已无待绑定数量（请先领料或检查是否已全部绑定）",
    ),
    (
        "serial is isolated; release before pass (cannot complete while isolated)",
        "序列号处于隔离中，请先放行再过站（隔离品不能按合格品继续）",
    ),
    (
        "serial is isolated; release before further fail actions, or scrap via scrap API",
        "序列号处于隔离中：请先放行后再处理，或直接报废",
    ),
    (
        "isolated serial cannot enter finished goods; release or scrap first",
        "隔离中的序列号不能入成品仓，请先放行或报废",
    ),
    (
        "close requires Completed status or completed+scrapped covering planned qty",
        "关闭工单要求：已完工，或合格完工数+报废数已覆盖计划数量",
    ),
    (
        "boundProcessStepId not found — station must bind exactly one process step",
        "未找到绑定工序：每个工位必须绑定恰好一道工序",
    ),
    (
        "finishedMaterialId must reference an active finished good",
        "成品物料无效或不是启用中的成品",
    ),
    (
        "workOrderId required when creating serial by system number",
        "系统发号创建序列号时必须指定生产工单",
    ),
    (
        "workOrderId required to start a new serial",
        "新开序列号上线时必须指定生产工单",
    ),
    (
        "cannot rework a route-completed serial; isolate or scrap",
        "路线已完成的序列号不能返工，请隔离或报废",
    ),
    (
        "cannot bind while isolated; release first",
        "序列号处于隔离中，请先放行再绑定关键件",
    ),
    (
        "cannot cancel while in-process serials exist",
        "仍有在制序列号，不能取消工单",
    ),
    (
        "cannot close while in-process serials remain",
        "仍有在制序列号，不能关闭工单",
    ),
    (
        "serial already completed to finished goods",
        "该序列号已完工入库",
    ),
    (
        "scrapped serial cannot enter finished goods",
        "已报废的序列号不能入成品仓",
    ),
    (
        "only draft or released orders can be cancelled",
        "仅草稿或已下达的工单可以取消",
    ),
    (
        "issue only allowed on released or in-process orders",
        "仅已下达或生产中的工单可以领料",
    ),
    (
        "work order has no BOM; release first or assign BOM",
        "工单尚无 BOM，请先下达或指定 BOM",
    ),
    (
        "BOM and process route are required to release",
        "下达工单需要有效的 BOM 与工艺路线",
    ),
    (
        "finished material not found or not a finished good",
        "未找到成品物料，或该物料不是成品",
    ),
    (
        "station step not on work order route",
        "当前工位工序不在本工单的工艺路线上",
    ),
    (
        "serial already linked to an open work order",
        "该序列号已挂在未关闭的生产工单上",
    ),
    (
        "component serial already bound",
        "该关键件序列号已绑定到其他成品",
    ),
    (
        "material is not a key component",
        "该物料不是关键件，无需绑定序列号",
    ),
    (
        "cannot bind to scrapped serial",
        "已报废的序列号不能绑定关键件",
    ),
    (
        "work order must be released or in process",
        "生产工单须为已下达或生产中",
    ),
    (
        "work order has no frozen route",
        "生产工单未冻结工艺路线，请先下达",
    ),
    (
        "work order has no process route",
        "生产工单未绑定工艺路线",
    ),
    (
        "only draft work orders can be released",
        "仅草稿状态的工单可以下达",
    ),
    (
        "only isolated serials can be released",
        "仅隔离中的序列号可以放行",
    ),
    (
        "cancelled orders are not closed via this API",
        "已取消的工单无需再关闭",
    ),
    (
        "work order is closed or cancelled",
        "生产工单已关闭或已取消",
    ),
    (
        "work order is closed (read-only)",
        "生产工单已关闭，不可再修改",
    ),
    (
        "one or more component materials not found",
        "存在未找到的组件物料",
    ),
    (
        "disposition must be Rework, Isolate, or Scrap",
        "不合格处置须为：返工、隔离或报废",
    ),
    ("release reason is required", "放行必须填写原因"),
    ("serial already scrapped", "该序列号已报废"),
    ("product serial not found", "未找到该成品序列号"),
    ("component material not found", "未找到该组件物料"),
    ("order number already exists", "工单号已存在"),
    ("plannedQty must be positive", "计划数量必须大于 0"),
    ("quantity must be positive", "数量必须大于 0"),
    ("material code already exists", "物料编码已存在"),
    ("code and name are required", "编码与名称不能为空"),
    ("BOM requires at least one line", "BOM 至少需要一行组件"),
    ("finishedMaterialId must be a finished good", "必须指定成品物料"),
    ("route requires steps", "工艺路线至少需要一道工序"),
    ("route code exists", "工艺路线编码已存在"),
    ("line code exists", "产线编码已存在"),
    ("productionLineId not found", "未找到该产线"),
    ("station code exists", "工位编码已存在"),
    ("work station not found", "未找到该工位"),
    ("work order not found", "未找到该生产工单"),
    ("serial not found", "未找到该序列号"),
    ("material not found", "未找到该物料"),
    ("BOM required", "需要 BOM 才能领料"),
    ("already closed", "工单已关闭"),
]

interp = [
    (
        'serial status is {serial.Status}, cannot pass',
        '序列号当前状态为 {serial.Status}，不能过站',
    ),
    (
        'serial status is {serial.Status}, cannot fail',
        '序列号当前状态为 {serial.Status}，不能做不合格处理',
    ),
    (
        'anti-skip: station step is {stationStep.Code}, serial current step is {cur}',
        '防跳站：工位工序为 {stationStep.Code}，序列号当前工序为 {cur}',
    ),
    (
        'new serial must start at first step {first.Code}, station is {stationStep.Code}',
        '新序列号必须从首道工序 {first.Code} 上线，当前工位为 {stationStep.Code}',
    ),
    (
        'no process step with sequence {seq} on route',
        '工艺路线上不存在顺序号为 {seq} 的工序',
    ),
    (
        'rework target sequence {targetSeq} not found on route',
        '返工目标顺序号 {targetSeq} 在工艺路线上不存在',
    ),
    (
        'material {line.MaterialId} not found',
        '未找到物料 {line.MaterialId}',
    ),
    (
        'insufficient line-side stock for {mat.Code}: need {qty}, have {inv?.QuantityOnHand ?? 0}',
        '线边库存不足：{mat.Code} 需要 {qty}，当前 {inv?.QuantityOnHand ?? 0}',
    ),
    (
        'serial must be RouteCompleted to warehouse, current is {serial.Status}',
        '只有路线完成的序列号才能完工入库，当前状态为 {serial.Status}',
    ),
]


def main() -> None:
    for f in files:
        text = f.read_text(encoding="utf-8")
        for a, b in pairs:
            text = text.replace(a, b)
        for a, b in interp:
            text = text.replace(a, b)
        f.write_text(text, encoding="utf-8")
        print("OK", f.relative_to(ROOT))


if __name__ == "__main__":
    main()
