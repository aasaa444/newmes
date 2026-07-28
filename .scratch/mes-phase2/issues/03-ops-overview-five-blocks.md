# 03 — 经营总览五块 + 下钻入口

**What to build:** 经营者（及获权角色）打开经营总览即可看到今日口径五块真实指标：在制（含按工序分布数据）、今日合格入库、今日报废、隔离待处理数、工单（进行中 / 已下达未开工）。每块可下钻到对应工作台或列表（在制/异常/工单）；零值有可信空态而非空白故障感。

**Blocked by:** 02 — 管理端经典壳（侧栏五菜单 + 顶栏）

**Status:** resolved

- [x] 总览 API（或等价读模型）返回五块，数据来自执行事实
- [x] 今日流量指标按自然日口径
- [x] 在制含总数及按工序分布，可支撑下钻
- [x] UI 五块展示 + 下钻到在制/异常/工单入口
- [x] 无总览权限者不可读总览 API；有自动化断言

## Answer

OpsOverviewService aggregates five blocks from execution facts (today local date window): wip total + per-step distribution, today qualified receipts, today scraps, isolated pending, orders in-process / released-not-started. Drill APIs /api/ops/wip?processStepId, /api/ops/orders?bucket, /api/quality/isolated. OpsOverviewRead policy. Management overview tiles + drill panels (wip/isolated/orders) with step filter chips. 60/60 tests green.

