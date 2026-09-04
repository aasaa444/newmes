import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

import {
  SpreadsheetFile,
  Workbook,
} from "file:///C:/Users/User/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/@oai/artifact-tool/dist/artifact_tool.mjs";

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const SOURCE = path.join(ROOT, "docs", "project-initiation");
const OUTPUT = path.join(ROOT, "deliverables", "project-initiation");
const RENDER_DIR = path.join(ROOT, ".artifacts", "xlsx-renders");
const OUTPUT_FILE = path.join(OUTPUT, "04-工业路由器装配MES-项目管理台账.xlsx");

const COLORS = {
  navy: "#0F2B5B",
  blue: "#1E6FD9",
  lightBlue: "#E8F1FC",
  orange: "#F39800",
  ink: "#1F2A37",
  gray: "#5A6672",
  grayBg: "#F3F5F7",
  green: "#2E7D46",
  greenBg: "#E6F3EA",
  red: "#C8392E",
  redBg: "#FBEAE8",
  white: "#FFFFFF",
  border: "#D8DEE5",
};

const FONT = "Microsoft YaHei";

const registers = JSON.parse(
  await fs.readFile(path.join(SOURCE, "project-registers.json"), "utf8"),
);
const brs = await fs.readFile(
  path.join(SOURCE, "02-business-requirements-specification.md"),
  "utf8",
);

const requirements = [...brs.matchAll(/^[-*]\s+\*\*((?:FR|NFR)-[A-Z]+-\d+)\*\*[：:]\s*(.+)$/gm)].map(
  ([, id, text]) => ({ id, text: text.trim() }),
);

if (requirements.length !== 54) {
  throw new Error(`Expected 54 requirements, found ${requirements.length}`);
}

const domainNames = {
  MD: "主数据与执行快照",
  PO: "生产订单",
  ID: "产品身份",
  WIP: "在制执行",
  MAT: "物料执行",
  FW: "固件与配置",
  TEST: "测试执行",
  QA: "质量闭环",
  CMP: "完工与交接",
  TRC: "单件谱系",
  INT: "企业集成",
  SEC: "权限与安全",
  AUD: "审计",
  OPS: "运营查询",
  DATA: "数据一致性",
  CONS: "事务一致性",
  DB: "数据库演进",
  REC: "恢复能力",
  PERF: "性能",
  RET: "数据保留",
  USAB: "可用性",
};

function requirementGroup(id) {
  return id.split("-")[1];
}

function stageFor(id) {
  const group = requirementGroup(id);
  if (["DB", "SEC"].includes(group)) return "R1/R6";
  if (["MD", "PO", "ID", "WIP", "MAT", "DATA", "CONS"].includes(group)) return "R2";
  if (["FW", "TEST", "QA", "CMP", "TRC"].includes(group)) return "R3";
  if (group === "INT") return "R4";
  if (["AUD", "OPS", "USAB", "PERF"].includes(group)) return "R5";
  if (["REC", "RET"].includes(group)) return "R6";
  return "待确认";
}

function ownerFor(id) {
  const group = requirementGroup(id);
  if (["QA", "TEST", "TRC", "RET"].includes(group)) return "质量/工艺";
  if (["MAT", "CMP"].includes(group)) return "物料/计划";
  if (["INT", "DB", "SEC", "REC", "PERF"].includes(group)) return "ERP/IT";
  if (["PO", "MD"].includes(group)) return "计划/工艺";
  return "MES项目经理";
}

function uatFor(id) {
  const prefix = id.replace(/-\d+$/, "");
  const matches = registers.uat
    .filter((item) => item.requirements.split(",").map((v) => v.trim()).includes(prefix))
    .map((item) => item.id);
  return matches.length ? matches.join("、") : "技术测试/待补充";
}

const requirementRows = requirements.map((item) => {
  const group = requirementGroup(item.id);
  return [
    item.id,
    item.id.startsWith("NFR") ? "非功能" : "功能",
    domainNames[group] ?? group,
    item.text,
    "Must",
    "未验证",
    "待实现/待验证",
    ownerFor(item.id),
    "BRS 0.1",
    "不得因候选路线而声称现场已确认",
  ];
});

const fitGap = [
  ["FG-001", "执行快照", "当前订单仅保存 BOM/路线 ID 与版本字符串", "下达时冻结自包含执行依据", "Gap", "Critical", "纵向切片替换，旧订单只读", "R2", "开发测试", "SQL Server 集成测试"],
  ["FG-002", "制造事件", "现有追溯聚合于部件绑定和状态记录", "只追加、类型化制造事件与可解释查询", "Gap", "Critical", "先建事件契约，再迁移核心流程", "R2", "开发测试", "固定 SN 谱系复现"],
  ["FG-003", "物料事务", "发料直接影响库存并混同耗用", "交接、发料、耗用、退料、冲正分账", "Gap", "Critical", "引入事务账和可用量投影", "R2", "物料/开发", "数量平衡与冲正"],
  ["FG-004", "测试与复测", "失败、放行和当前状态存在覆盖风险", "测试规范版本化；每次执行与测量独立保留", "Gap", "Critical", "新测试域模型优先", "R3", "质量/开发", "失败-返工-复测场景"],
  ["FG-005", "质量处置", "不合格、保留、处置和返工边界不清", "职责分离的质量保留与处置闭环", "Gap", "Critical", "建立独立不合格与处置对象", "R3", "质量/开发", "负向权限与门禁"],
  ["FG-006", "ERP 集成", "当前模拟发送可被误解为成功", "Inbox/Outbox、重试、死信、回执与对账", "Gap", "Critical", "模拟器走正式契约", "R4", "ERP/IT", "重复、失败与恢复"],
  ["FG-007", "数据库演进", "EnsureCreated 与自动建库缺少升级路径", "EF Migration 与真实 SQL Server 新装/升级", "Gap", "Critical", "R1 先清除数据库风险", "R1", "开发测试", "空库和带数据升级"],
  ["FG-008", "生产安全", "Compose 存在硬编码密钥和 Development 配置", "HTTPS、外置密钥、最小权限和启动门禁", "Gap", "Critical", "生产配置拒绝不安全启动", "R1/R6", "ERP/IT", "部署与安全检查"],
  ["FG-009", "一线工作流", "功能存在但页面可能按 API 菜单组织", "计划、工位、质量、追溯四条任务流", "Partial", "Major", "R5 前冻结非核心 UI", "R5", "开发测试", "浏览器 E2E 与视觉 QA"],
  ["FG-010", "现场可信度", "公开资料可支撑边界，不能证明真实工序", "制造从业者评审和真实现场验证", "External", "Critical", "证据分级，不补造客户事实", "R0-R6", "MES项目经理", "评审纪要和现场签字"],
];

const workbook = Workbook.create();

function setColumnWidths(sheet, widths) {
  widths.forEach((width, index) => {
    sheet.getRangeByIndexes(0, index, 1, 1).format.columnWidth = width;
  });
}

function prepareSheet(sheet, title, subtitle, columns, rows, widths, tableName) {
  sheet.showGridLines = false;
  const lastCol = String.fromCharCode(64 + columns.length);
  sheet.getRange(`A1:${lastCol}1`).merge();
  sheet.getRange("A1").values = [[title]];
  sheet.getRange("A1").format = {
    fill: COLORS.navy,
    font: { name: FONT, size: 18, bold: true, color: COLORS.white },
    rowHeight: 34,
    verticalAlignment: "center",
  };
  sheet.getRange(`A2:${lastCol}2`).merge();
  sheet.getRange("A2").values = [[subtitle]];
  sheet.getRange("A2").format = {
    fill: COLORS.lightBlue,
    font: { name: FONT, size: 10, color: COLORS.gray },
    rowHeight: 26,
    verticalAlignment: "center",
  };
  const allRows = [columns, ...rows];
  const endRow = 4 + rows.length;
  sheet.getRange(`A4:${lastCol}${endRow}`).values = allRows;
  sheet.getRange(`A4:${lastCol}4`).format = {
    fill: COLORS.blue,
    font: { name: FONT, size: 10, bold: true, color: COLORS.white },
    rowHeight: 28,
    wrapText: true,
    verticalAlignment: "center",
  };
  if (rows.length) {
    sheet.getRange(`A5:${lastCol}${endRow}`).format = {
      font: { name: FONT, size: 10, color: COLORS.ink },
      borders: { preset: "all", style: "thin", color: COLORS.border },
      wrapText: true,
      verticalAlignment: "top",
    };
    sheet.getRange(`A5:${lastCol}${endRow}`).format.rowHeight = 36;
    const table = sheet.tables.add(`A4:${lastCol}${endRow}`, true, tableName);
    table.style = "TableStyleMedium2";
    table.showBandedRows = true;
  }
  setColumnWidths(sheet, widths);
  sheet.freezePanes.freezeRows(4);
}

const overview = workbook.worksheets.add("项目总览");
overview.showGridLines = false;
overview.getRange("A1:H1").merge();
overview.getRange("A1").values = [["工业路由器装配 MES · 项目管理台账"]];
overview.getRange("A1").format = { fill: COLORS.navy, font: { name: FONT, size: 20, bold: true, color: COLORS.white }, rowHeight: 38 };
overview.getRange("A2:H2").merge();
overview.getRange("A2").values = [["试点候选 · 证据透明 · 先验证后承诺 | 版本 0.1 | 2026-07-29"]];
overview.getRange("A2").format = { fill: COLORS.lightBlue, font: { name: FONT, size: 10, color: COLORS.gray }, rowHeight: 25 };
overview.getRange("A4:B10").values = [
  ["项目", registers.metadata.project],
  ["负责人", registers.metadata.owner],
  ["当前状态", registers.metadata.status],
  ["试点边界", "已测 PCBA → 整机装配 → 合格整机交接"],
  ["第一价值", "成品 SN 全过程追溯"],
  ["系统边界", "ERP 计划权威；MES 执行权威"],
  ["可信度", "未验证；尚未完成制造从业者评审或真实现场验证"],
];
overview.getRange("A4:A10").format = { fill: COLORS.grayBg, font: { name: FONT, size: 10, bold: true, color: COLORS.navy }, borders: { preset: "all", style: "thin", color: COLORS.border } };
overview.getRange("B4:B10").format = { font: { name: FONT, size: 10, color: COLORS.ink }, borders: { preset: "all", style: "thin", color: COLORS.border }, wrapText: true };
overview.getRange("D4:E10").values = [
  ["指标", "当前值"],
  ["需求总数", null],
  ["Must 需求", null],
  ["开放 RAID", null],
  ["候选接口", null],
  ["UAT 场景", null],
  ["待澄清问题", null],
];
overview.getRange("E5").formulas = [["=COUNTA('需求清单'!$A$5:$A$58)"]];
overview.getRange("E6").formulas = [["=COUNTIF('需求清单'!$E$5:$E$58,\"Must\")"]];
overview.getRange("E7").formulas = [["=COUNTIF('RAID'!$H$5:$H$16,\"<>Closed\")"]];
overview.getRange("E8").formulas = [["=COUNTA('接口清单'!$A$5:$A$12)"]];
overview.getRange("E9").formulas = [["=COUNTA('UAT'!$A$5:$A$8)"]];
overview.getRange("E10").formulas = [["=COUNTIF('待澄清问题'!$F$5:$F$16,\"Open\")"]];
overview.getRange("D4:E4").format = { fill: COLORS.blue, font: { name: FONT, size: 10, bold: true, color: COLORS.white } };
overview.getRange("D5:D10").format = { fill: COLORS.grayBg, font: { name: FONT, size: 10, bold: true, color: COLORS.navy }, borders: { preset: "all", style: "thin", color: COLORS.border } };
overview.getRange("E5:E10").format = { fill: COLORS.greenBg, font: { name: FONT, size: 14, bold: true, color: COLORS.green }, borders: { preset: "all", style: "thin", color: COLORS.border }, horizontalAlignment: "center" };
overview.getRange("A13:H13").merge();
overview.getRange("A13").values = [["使用说明：台账是候选项目基线，不是客户已签署范围；现场数据、接口字段和业务规则完成验证后再升级证据等级。"]];
overview.getRange("A13").format = { fill: "#FDF3E0", font: { name: FONT, size: 10, bold: true, color: "#9A6300" }, rowHeight: 34, wrapText: true };
setColumnWidths(overview, [16, 54, 4, 18, 18, 4, 16, 16]);

const reqSheet = workbook.worksheets.add("需求清单");
prepareSheet(reqSheet, "需求清单", "54 条 FR/NFR；证据等级默认未验证", ["需求ID", "类型", "领域", "需求描述", "优先级", "证据等级", "状态", "责任角色", "来源", "备注"], requirementRows, [16, 10, 18, 66, 11, 14, 16, 16, 13, 34], "RequirementsTable");
reqSheet.getRange("E5:E58").dataValidation = { rule: { type: "list", values: ["Must", "Should", "Could", "Won't"] } };
reqSheet.getRange("F5:F58").dataValidation = { rule: { type: "list", values: ["未验证", "已实现", "技术验证通过", "制造从业者评审通过", "真实现场验证通过"] } };

const traceRows = requirements.map((item) => [
  item.id,
  domainNames[requirementGroup(item.id)] ?? requirementGroup(item.id),
  "Must",
  `BRS 6.${Math.max(1, Object.keys(domainNames).indexOf(requirementGroup(item.id)) + 1)}`,
  stageFor(item.id),
  uatFor(item.id),
  "待生成",
  "未验证",
]);
prepareSheet(workbook.worksheets.add("需求追踪"), "需求追踪矩阵", "从需求到设计、阶段、测试和证据的闭环", ["需求ID", "领域", "优先级", "设计基线", "实现阶段", "测试/UAT", "证据位置", "当前结论"], traceRows, [16, 20, 10, 18, 13, 24, 28, 16], "TraceabilityTable");

prepareSheet(workbook.worksheets.add("Fit-Gap"), "Fit-Gap 分析", "区分系统缺口、部分能力和必须依赖现场确认的外部缺口", ["编号", "能力", "当前评估", "目标能力", "类型", "优先级", "处理策略", "阶段", "责任人", "验证方式"], fitGap, [13, 18, 42, 42, 12, 12, 38, 12, 16, 30], "FitGapTable");

const raciRows = registers.raci.map((item) => [item.activity, ...item.values]);
prepareSheet(workbook.worksheets.add("RACI"), "RACI 责任矩阵", "R=负责执行，A=最终负责，C=征询，I=知会", ["活动", ...registers.raci_roles], raciRows, [34, ...registers.raci_roles.map(() => 15)], "RaciTable");

const wbsRows = registers.wbs.map((item) => [item.id, item.stage, item.deliverable, item.owner, item.duration_days, item.depends_on, item.exit, "未开始"]);
const wbsSheet = workbook.worksheets.add("WBS");
prepareSheet(wbsSheet, "R0–R6 工作分解结构", "5–6 周为候选救援估算；真实客户项目需重新估算", ["WBS ID", "阶段", "交付物", "责任人", "工期(天)", "前置", "退出条件", "状态"], wbsRows, [13, 10, 40, 16, 12, 14, 42, 14], "WbsTable");
wbsSheet.getRange(`H5:H${4 + wbsRows.length}`).dataValidation = { rule: { type: "list", values: ["未开始", "进行中", "阻塞", "已完成"] } };

const raidRows = registers.raid.map((item) => [item.id, item.type, item.description, item.probability, item.impact, item.owner, item.response, item.status]);
prepareSheet(workbook.worksheets.add("RAID"), "RAID 台账", "风险、假设、问题和依赖必须可追踪，不得藏在会议记录中", ["编号", "类型", "描述", "概率", "影响", "责任人", "应对/验证", "状态"], raidRows, [14, 14, 42, 12, 12, 16, 42, 16], "RaidTable");

const interfaceRows = registers.interfaces.map((item) => [item.id, item.name, item.direction, item.object, item.trigger, item.idempotency, item.ack, item.status]);
prepareSheet(workbook.worksheets.add("接口清单"), "候选接口清单", "接口名称与字段仅为契约候选；需结合 ERP/WMS 产品、版本和样例报文确认", ["编号", "接口", "方向", "业务对象", "触发", "幂等键", "回执", "状态"], interfaceRows, [13, 26, 18, 18, 20, 28, 24, 18], "InterfacesTable");

const uatRows = registers.uat.map((item) => [item.id, item.scenario, item.roles, item.requirements, item.expected, item.status]);
prepareSheet(workbook.worksheets.add("UAT"), "UAT 主场景", "技术验证不等于现场验收；真实 UAT 需由关键用户执行并签字", ["编号", "场景", "参与角色", "覆盖需求族", "预期结果", "状态"], uatRows, [13, 28, 24, 42, 58, 16], "UatTable");

const questionRows = registers.open_questions.map((item) => [item.id, item.area, item.question, item.owner, item.gate, item.status]);
prepareSheet(workbook.worksheets.add("待澄清问题"), "待澄清问题", "这些问题会影响工艺真实性、接口范围、估算、部署和验收", ["编号", "领域", "问题", "责任人", "决策门禁", "状态"], questionRows, [13, 14, 58, 18, 28, 16], "QuestionsTable");

await fs.mkdir(OUTPUT, { recursive: true });
await fs.mkdir(RENDER_DIR, { recursive: true });
const xlsx = await SpreadsheetFile.exportXlsx(workbook);
await xlsx.save(OUTPUT_FILE);

// Render the exported workbook so preview QA exercises the actual XLSX package.
const renderedWorkbook = await SpreadsheetFile.importXlsx(await fs.readFile(OUTPUT_FILE));
for (const sheet of renderedWorkbook.worksheets.items) {
  const image = await renderedWorkbook.render({
    sheetName: sheet.name,
    autoCrop: "all",
    scale: 1,
    format: "png",
  });
  const safeName = sheet.name.replace(/[\\/:*?"<>|]/g, "-");
  await fs.writeFile(
    path.join(RENDER_DIR, `${safeName}.png`),
    new Uint8Array(await image.arrayBuffer()),
  );
}
console.log(OUTPUT_FILE);
