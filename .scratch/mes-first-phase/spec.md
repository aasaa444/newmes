# MES 第一期 — 单厂离散电子组装试点

**Status:** ready-for-agent  
**Parent:** grill-with-docs 会话共识 + `CONTEXT.md` + `docs/adr/0001`–`0005`  
**Seam (测试最高缝):** 种子数据 → 工单下达/领料 API → 工位过站 API → 完工入库 API → 谱系查询 / 审计查询 / ERP 回写出站日志

---

## Problem Statement

希望用 vibe coding 做出一套**真实可试点、界面精致、可长期演进**的 MES，而不是玩具 demo。目标用户语境是中小离散**电子组装**工厂；个人目标同时覆盖：学透 MES（及与 ERP/WMS 边界）、服务求职面试、以及按单厂试点工程标准交付。已有企业内部系统与 SAP 接口经验，目标客户 ERP 语境偏用友/金蝶；技术主栈为 .NET + SQL Server，前端明确 Vue 3。

没有真实甲方现场时，「可落地」定义为：单垂直深度、可私有化部署、主业务账与状态机正确、集成边界可演示——而不是通用多租户 MES 产品。

## Solution

交付**单厂试点级** MES 第一期：

- **闭环**：生产工单下达 → 线边领料 → 工位过站（成品 SN 首站创建/扫入 + 关键件 SN 绑定）→ 质量判定 → 合格完工入库 → 按 SN 查谱系  
- **异常**：不合格 → 隔离 / 返工 / 报废，全程进谱系与业务审计  
- **库存**：线边账、在制账、成品账三分离；不做完整 WMS  
- **集成**：MES 维护执行主数据；ERP 适配器以**回写模拟器**产出用友/金蝶风格完工/耗料可查报文  
- **角色与 UI**：计划员、操作工、班组长；Web 计划端 + 工位过站台（同 Vue 工程、不同布局）  
- **工程**：ASP.NET Core + SQL Server + Vue 3；RBAC + 业务审计；Docker Compose 私有化  

词汇与硬决策以仓库根 `CONTEXT.md` 与 `docs/adr/*` 为准。

## User Stories

1. As a 计划员, I want to maintain 物料 with 执行期属性 (是否关键件、是否采集 SN), so that 过站与追溯规则有主数据依据.  
2. As a 计划员, I want to maintain a single-level BOM for a finished 物料, so that 领料与齐套可以计算应发量.  
3. As a 计划员, I want to maintain a linear 工艺路线 with ordered 工序, so that SN 按固定顺序过站.  
4. As a 计划员, I want to maintain 产线 and 工位, each 工位 bound to one 工序, so that 过站台有明确上下文.  
5. As a 计划员, I want to create a 生产工单 in 草稿, so that I can prepare plan quantity and route version before release.  
6. As a 计划员, I want to 下达 a 生产工单, freezing the chosen 工艺路线 version, so that in-process changes do not scramble WIP.  
7. As a 计划员, I want 齐套 hints against 线边库存账 when issuing material, so that I know shortages before launch (block optional, default warn-only).  
8. As a 计划员, I want to 领料 from 线边仓 onto the 生产工单, so that non-关键件 become 已耗 and 关键件 enter 待耗/usable issued state per rules.  
9. As a 操作工, I want to open the 过站台, select my 工位, and only process SN due at that 工序, so that 防跳站 is enforced.  
10. As a 操作工, I want the first successful 过站 on a released 工单 to create or scan-bind a finished-good SN, so that WIP individuals exist only after line entry.  
11. As a 操作工, I want to bind 关键件 SNs required at a 工序, so that 谱系 records component identity.  
12. As a 操作工, I want pass/fail 质量判定 at a station step, so that pass advances the route and fail can 返工, 隔离, or 报废.  
13. As a 班组长 (or authorized role), I want to 放行 an isolated SN with reason, so that controlled return to flow is auditable.  
14. As a 计划员, I want 合格完工入库 to increase 成品库存账 and clear that SN from normal WIP, so that sellable stock is not mixed with hold/scrap.  
15. As any authorized user, I want to query 谱系 by finished SN, so that I can answer “which components and stations did this unit go through?”.  
16. As a 班组长, I want to see 工单 progress and WIP by 工序/产线, so that I can manage the line without a second planning system.  
17. As a 计划员, I want 工单状态 草稿→已下达→生产中→已完工→已关闭, so that lifecycle is explicit.  
18. As a 计划员, I want to cancel only when no in-process SN exists, so that we never orphan WIP by cancel.  
19. As a system, I want completion and consumption events to emit ERP 回写模拟器 messages (用友/金蝶-style), so that integration boundary is demonstrable without a live ERP.  
20. As a 计划员, I want to browse outbound ERP simulator messages, so that I can show integration in interviews and pilots.  
21. As each of 计划员/操作工/班组长, I want to log in with a local account and only see allowed menus/APIs, so that RBAC is real (not UI-only).  
22. As a 计划员, I want 业务审计 of release, issue, 过站, bind, isolate/release/scrap, and master-data changes, so that people-actions are traceable separately from product 谱系.  
23. As an implementer/demo operator, I want one-click or scripted 种子数据, so that the golden path and exception path can be replayed.  
24. As an implementer, I want Docker Compose (or equivalent) private deploy of API + Web + SQL Server connectivity, so that a pilot box is repeatable.  
25. As a 操作工, I want large-type, scan-first 过站台 UX (keyboard-wedge scanners), so that the station UI is product-grade, not an admin grid.  
26. As a 计划员, I want modern plan-desk UI for orders, master data, inventory, genealogy, audit, and ERP outbox, so that the system feels pilot-ready.  
27. As the system, I want network-online operation only in phase 1 (no offline queue), so that consistency stays simple and honest.  
28. As the system, I want MES to own 执行主数据 maintenance without requiring ERP import to run the loop, so that demos and pilots work without a customer ERP tenant.

## Implementation Decisions

- **Domain vocabulary**: Use terms from `CONTEXT.md` only (生产工单, 过站, 谱系, 关键件, 隔离, 线边库存账, ERP 回写模拟器, …).  
- **Architecture**: Modular monolith — domain/application services behind ASP.NET Core Web API; Vue 3 SPA with plan layout vs station layout; SQL Server persistence.  
- **ADRs in force**: Web station (0001); .NET + SQL Server + Vue 3 (0002); MES-owned master data, adapter export-first (0003); containerized private delivery (0004); phase-1 acceptance includes ERP simulator (0005).  
- **Primary test seam (one)**: Seed → issue/release order → material issue → station check-in (pass/fail paths) → completion → genealogy + audit + ERP outbox observables. Prefer API-level (or process-level) tests on this seam over many fine-grained internal mocks.  
- **Order status**: Draft → Released → InProcess → Completed → Closed; cancel only with no in-process SN; no Pause state in phase 1.  
- **SN**: One order many units; SN created/bound at first station pass; key components bind into genealogy; non-key consume by quantity (+ optional Lot).  
- **Routing**: Linear only; anti-skip by station’s single bound operation vs SN’s current operation.  
- **Inventory**: Line-side / WIP / finished separated; issue before station consumption rules per glossary.  
- **Identity**: Local accounts, password hashing, single primary role per user, server-side authorization, business audit trail.  
- **ERP**: Outbound simulator messages for completion/consumption; no live 用友/金蝶/SAP in phase 1.  
- **Do not** encode ephemeral file paths as requirements; structure modules by domain capability (master data, orders, inventory, station execution, quality, genealogy, identity/audit, erp-adapter, seed/demo).

### Decision-rich shapes (from design, not a running prototype)

**工单状态**

```
Draft -> Released -> InProcess -> Completed -> Closed
Released -> Cancelled  (only if no in-process SN)
```

**过站结果（概念）**

```
Pass -> advance to next operation (or complete route)
Fail -> ReworkTo(op) | Isolate | Scrap
Isolate -> Release(to op or flow) | Scrap
```

## Testing Decisions

- Good tests assert **observable behaviour** on the primary seam (API responses, persisted genealogy, inventory balances, outbox messages, audit rows)—not private method structure.  
- Cover at minimum: golden path one unit end-to-end; fail→isolate→release/rework or scrap; anti-skip rejection; cancel blocked when WIP SN exists; RBAC denial on forbidden API; ERP outbox written on completion/consumption.  
- Prefer few high-seam tests over broad UI snapshot suites in phase 1; add targeted UI checks only for station critical path if feasible.  
- Greenfield repo: no prior test art; establish API/integration test project beside the API as the default pattern.

## Out of Scope

- Live ERP/WMS integration, multi-plant, multi-tenant SaaS  
- Full WMS (locations strategies, waves, docks), APS, PLC/SCADA auto pass-through  
- Offline/disconnected station queues  
- SPC / full QMS, label print subsystem, heavy reporting, load/perf campaigns  
- Parallel/optional complex routings, multi-level BOM explosion, MRP  
- SSO/AD/WeCom/DingTalk as phase-1 requirement  
- Replacing ERP finance or total-warehouse ledgers  

## Further Notes

- Demo narrative product: simplified router/electronics assembly (成品 + PCB 等关键件).  
- Interview story: domain depth + 用友/金蝶-style adapter boundary + SAP integration experience as transferable discipline.  
- All implementation for this effort lives under `D:\Game\MES` — do not touch the 无名江湖 game repo.
