# MES 二期 — 可运营企业壳与工作台

**Status:** ready-for-agent  
**Parent:** 二期 grill-with-docs 共识 + `CONTEXT.md`（二期术语）+ `docs/adr/0006`–`0010`  
**Depends on:** 一期已验收闭环（`.scratch/mes-first-phase/`，票 01–07）  
**Primary test seam (confirmed):** 见文末 Testing Decisions  

---

## Problem Statement

一期已能跑通「工单 → 领料 → 过站/谱系 → 质量异常 → 入库 → ERP 模拟出站」，但对**每天使用的人**仍像演示页拼盘：无统一菜单与信息架构，排版随意，计划员/班组长缺少在制与异常的一等公民工作台，老板无法一打开就可靠掌握在制、产出、报废与隔离积压。  
用户要求二期以**价值**为准——一线可用、企业效率、经营可视——做成**够企业级、可日常运营**的形态，而不是再堆空模块或先省事砍价值；也不在二期强上真 ERP / 完整 WMS / 新车间流程。

## Solution

在**不推翻一期领域模型、不新增车间业务流程**的前提下，交付：

1. **双壳**：管理端（经典侧栏中后台）+ 过站端（现场全屏，仅增强指引与异常入口）。  
2. **管理端一级五菜单**：经营总览 → 生产执行 → 质量异常 → 物料库存 → 系统。  
3. **经营总览五块**（今日口径、执行事实、可下钻）：在制及工序分布、今日合格入库、今日报废、隔离待处理、工单（进行中 / 已下达未开工）。  
4. **工单工作台 + 在制工作台 + 异常工作台 + 库存工作台**，把一期能力装进可运营入口。  
5. **经营者（Owner）角色**及四角色登录默认落地；**用户与角色配置台**（非写死种子）。  
6. 过站端：工艺序工位、当前 SN 下一站提示、清晰不合格/隔离、可返回管理端（有权时）。

成功标准对齐 `CONTEXT.md` **二期完成定义** 七条，而非菜单数量。

## User Stories

1. As a 经营者, I want to land on 经营总览 after login, so that I see plant status without hunting menus.  
2. As a 经营者, I want 经营总览五块 with real numbers for 今日, so that I trust what I see.  
3. As a 经营者, I want to drill from 在制 into 在制分布 and SN lists, so that I find bottlenecks.  
4. As a 经营者, I want to drill from 隔离待处理 into 异常工作台, so that I see what is on hold.  
5. As a 经营者, I want to drill from 工单 counts into 工单工作台 filtered views, so that I see backlog.  
6. As a 经营者, I want read-only access to 生产执行 / 质量异常 / 物料库存, so that I do not accidentally change execution.  
7. As a 经营者, I want to be blocked from 下达 / 领料 / 主数据写 / 过站写, so that duties stay separated.  
8. As a 计划员, I want to land on 工单工作台 after login, so that my daily path starts with orders.  
9. As a 计划员, I want a management 侧栏 with the five fixed menus, so that the system feels like an enterprise app.  
10. As a 计划员, I want 工单工作台 list with status, 领料 visibility, and filters, so that I stop using Excel to track orders.  
11. As a 计划员, I want to 下达 / 领料 / 关闭 from 工单工作台, so that plan actions live in one place.  
12. As a 计划员, I want 在制工作台 showing counts by 工序, so that I align with the line on WIP.  
13. As a 计划员, I want to open SN list for a 工序 from 在制分布, so that I act on stuck units.  
14. As a 计划员, I want 异常工作台 to 放行 or 报废 with reason, so that holds are cleared without the station corner only.  
15. As a 计划员, I want 库存工作台 for 线边 and 成品, so that issue and receipt make sense.  
16. As a 计划员, I want to 线边收料 from 库存工作台, so that demo and pilot stock can be topped up.  
17. As a 计划员, I want 系统 menu for 主数据, 用户与角色配置台, 业务审计, ERP 出站, so that config is not mixed into daily execution.  
18. As a 计划员, I want optional 查看经营总览, so that I can use the same numbers the boss sees.  
19. As a 班组长, I want to land on 在制工作台 after login, so that I start with where units are stuck.  
20. As a 班组长, I want 异常工作台 as a first-class menu, so that isolation is not buried on the station screen.  
21. As a 班组长, I want to 放行 isolated SN with mandatory reason, so that release is accountable.  
22. As a 班组长, I want to 报废 from 异常工作台 when appropriate, so that dead units leave WIP cleanly.  
23. As a 班组长, I want read access to 工单 progress, so that I coordinate without a second system.  
24. As a 班组长, I want to switch to 过站端 when I must work on the line, so that dual-shell supports both jobs.  
25. As an 操作工, I want to land on 过站端 after login, so that I never open a sidebar maze first.  
26. As an 操作工, I want 工位 list ordered by 工艺路线 Sequence, so that the dropdown matches real line order.  
27. As an 操作工, I want a hint for the current SN’s required 工序 / next station, so that I reduce wrong-station passes.  
28. As an 操作工, I want clear 不合格 actions (返工 / 隔离 / 报废), so that quality paths are obvious.  
29. As an 操作工, I want Chinese error messages on fail, so that I know what to do next.  
30. As an 操作工, I want NOT to see management sidebar as primary UX, so that scan speed stays high.  
31. As an 操作工 with no management rights, I want no write access to 主数据 or 工单配置, so that RBAC stays real.  
32. As a user with both shells allowed, I want a top-bar control to switch 管理端 ↔ 过站端, so that dual-shell is navigable.  
33. As a 计划员 admin, I want 用户与角色配置台 to create users, so that pilots are not stuck with seed accounts.  
34. As a 计划员 admin, I want to assign a single primary role (Planner / Operator / Leader / Owner), so that landing and menus stay predictable.  
35. As a 计划员 admin, I want to enable/disable users and reset passwords, so that turnover is operable.  
36. As a 计划员 admin, I want to grant 查看经营总览 to Planner/Leader, so that ops visibility is configurable.  
37. As any authorized user, I want 业务审计 to record user/role changes, so that admin actions are accountable.  
38. As a 经营者, I want 今日口径 clearly labeled, so that I do not confuse day totals with shifts.  
39. As a 经营者, I want 在制分布 by 工序, so that bottlenecks are visible without asking the line.  
40. As a 经营者, I want 今日合格入库 and 今日报废 side by side, so that yield pressure is obvious.  
41. As a 班组长, I want filters on 异常工作台 by 工单 or time, so that I clear the right queue.  
42. As a 计划员, I want 工单工作台 to show 已领料 / 未领料, so that issue is not invisible after click.  
43. As a 计划员, I want consistent page chrome (title, breadcrumb, query bar, primary button), so that every module feels one product.  
44. As a 计划员, I want 系统 → 主数据 inside the shell, so that master data is not a stray page.  
45. As a 计划员, I want 系统 → ERP 出站 list, so that integration demos stay one click away.  
46. As a 计划员, I want 系统 → 业务审计 list, so that compliance review is in the shell.  
47. As an implementer, I want phase-1 golden path still green after phase-2 UI/auth changes, so that execution kernel is not broken.  
48. As an implementer, I want phase-2 acceptance to match 二期完成定义 seven items, so that “done” is not vague polish.  
49. As a 班组长, I want 质量异常 menu not empty, so that enterprise IA matches daily exception work.  
50. As a 经营者, I want empty-state copy when a metric is zero, so that zero is trustworthy not “broken”.  
51. As an 操作工, I want 过站端 to show ordered stations 1..N by process, so that UI matches physical flow.  
52. As a user, I want logout and identity visible in 管理端 top bar, so that shared PCs are safer.  
53. As a 计划员, I want deep links from 经营总览 cards into the correct workbench with filters applied, so that drill-down saves time.  
54. As a 经营者, I want to be denied write APIs with clear Chinese errors, so that read-only is enforced server-side.  
55. As a 班组长, I want 在制 SN list to show 序列号, 工单, 状态, 当前工序, so that I can walk the line with data.  
56. As a 计划员, I want 物料库存 to separate 线边 and 成品, so that MES inventory story stays clear vs WMS.  
57. As an 操作工, I want bind-component still requiring prior 领料, so that material discipline remains.  
58. As a 计划员, I want no new mandatory processes (退料/APS/标签/SPC) in phase 2, so that shell and workbenches actually ship.  
59. As a stakeholder, I want ADRs 0006–0010 respected, so that dual-shell, Owner, scope discipline, and layout are not re-litigated in code review.  
60. As a demo operator, I want seed user `owner` (经营者演示) with known password, so that boss journey is demonstrable.  
61. As a 班组长, I want Chinese status labels on workbenches, so that Released/InProcess are not opaque.  
62. As a 经营者, I want metrics derived only from execution facts (SN/work order/pass/scrap/isolate/receipt), so that numbers stay reliable.  
63. As a 计划员, I want 生产执行 sub-nav between 工单工作台 and 在制工作台, so that both daily tools are one menu domain.  
64. As an 操作工, I want failure messages for anti-skip and wrong first station in Chinese, so that training cost drops.  
65. As a user switching shells, I want authorization to hide switch entry when not allowed, so that IA matches RBAC.

## Implementation Decisions

- **Scope discipline (ADR-0010):** No new shop-floor processes as phase-2 must-haves (full 退料, batch pass, labels, SPC/APS, etc.). Re-home and thicken existing execution capabilities inside enterprise IA.  
- **Dual shell (ADR-0006):** Management shell vs station shell; shared API and accounts; role-based default landing (CONTEXT 登录默认落地).  
- **Management IA (CONTEXT):** Fixed primary menu order: 经营总览 → 生产执行 → 质量异常 → 物料库存 → 系统.  
- **Layout (ADR-0009):** Management = left sidebar + top bar + content (title, breadcrumb, standard list toolbar). Station = full-screen; not the sidebar shell.  
- **Owner role (ADR-0007):** Add `Owner` to role set; seed demo user; policies for ops-overview read vs execution write.  
- **Optional capability:** e.g. `CanViewOpsOverview` (or equivalent) for Planner/Leader without making them Owner.  
- **经营总览 API:** Single (or tightly related) read model returning five blocks for “today” local calendar date; include 在制 total + per-工序 breakdown payload for charts/tables.  
- **在制 APIs:** Read model for counts by 工序 (optional 产线 filter later); read model for SN list filtered by 工序/状态. Reuse `ProductSerial` / `ProcessStep` facts; statuses InProcess (and any non-terminal in-process definition consistent with CONTEXT—exclude Completed/Scrapped finished goods path).  
- **异常工作台:** Prefer existing isolate list + release/scrap endpoints; add filters as needed; ensure management UI is first-class.  
- **工单工作台:** Extend existing work-order list/detail/issue UX inside shell; keep `materialIssued` visibility; Chinese status labels.  
- **库存工作台:** Compose existing line-side + finished-goods reads + receive; clear separation of accounts.  
- **用户配置台:** CRUD-ish admin for users: list, active flag, password reset, primary role, overview flag; audit admin actions.  
- **Login/me contract:** Return role, display name, default shell/route hint, capabilities, allowed shells.  
- **Frontend modules:** Management app shell (layout, menu, router guards by role); pages for overview, production (orders + WIP), quality exceptions, inventory, system (users, master data, audit, ERP outbox); station app enhancements only.  
- **Backend modules:** Identity/role expansion; overview/WIP query services; user admin endpoints; thin BFF-style aggregation if needed—avoid rewriting execution kernel.  
- **Tech stack unchanged (ADR-0002):** ASP.NET Core + SQL Server + Vue 3; modular monolith.  
- **i18n:** User-visible errors remain Chinese (phase-1 fix).  
- **Do not** hardcode ephemeral file paths in acceptance; structure by capability names above.

### Decision-rich shapes (from design)

**Login default landing**

```
Owner    -> Management / OpsOverview
Planner  -> Management / WorkOrderWorkbench
Leader   -> Management / WipWorkbench
Operator -> Station / StationHome
```

**Ops overview five blocks (today)**

```
WipTotal + WipByProcess[]
TodayQualifiedReceipts
TodayScraps
IsolatedPendingCount
OrdersInProcessCount + OrdersReleasedNotStartedCount
```

**Primary menu**

```
OpsOverview | ProductionExecution | QualityExceptions | MaterialInventory | System
```

## Testing Decisions

- **Good tests** assert externally visible behavior through public HTTP (and documented UI acceptance checklists where pure layout): status codes, role denials, overview numbers consistent with seeded execution facts, drill filters, user admin effects, phase-1 golden path regression. Do not assert private component tree structure.  
- **Primary seam (user-confirmed), one high-level path:**  
  Login (four roles / default landing claims) → management menu visibility by role → ops overview five blocks → drill to WIP / isolated / orders → planner write vs owner read-only on execution APIs → exception release/scrap on workbench APIs → inventory reads → user admin create/assign role + audit → station ordered stations + pass still works → phase-1 golden path still green.  
- **Prior art:** `MesApiFactory` + xUnit seam tests (`AuthSeamTests`, `WorkOrderSeamTests`, `StationPassSeamTests`, `QualitySeamTests`, `CompletionSeamTests`, `Phase1E2eSeamTests`). Add `Phase2*SeamTests` (or extend phase-1 e2e) rather than a second test stack.  
- **Frontend shell:** Mechanical checklist or light e2e optional; must not replace API seam for authorization and metrics correctness.  
- **Metrics tests:** Build known SN/work-order facts in test host, assert overview counts—no hardcoded production timestamps without control of clock/seed.

## Out of Scope

- Live 用友/金蝶/SAP integration beyond existing simulator/outbox  
- Full WMS (locations, waves, cycle count programs), APS/scheduling Gantt, PLC/SCADA, offline station queues  
- Multi-plant / multi-tenant SaaS  
- Full QMS/SPC, sampling plans, label printing subsystems  
- New mandatory processes: full 退料 flow, batch pass, etc. (ADR-0010)  
- SSO/AD/WeCom, multi-role stacking, field-level ACL, line-level data permissions  
- Financial value/cost/OEE dashboards without execution data foundation  
- Rewriting phase-1 domain model or abandoning single-plant discrete electronics scope  

## Further Notes

- Phase 2 is **operable enterprise shape** on top of phase-1 execution kernel; vocabulary must match `CONTEXT.md` (管理端, 过站端, 经营总览, 工单工作台, 在制工作台, 异常工作台, 库存工作台, 经营者, 双壳, 今日口径, …).  
- Respect ADRs: 0006 dual-shell, 0007 Owner, 0008 phase-2 completion definition, 0009 sidebar layout, 0010 no new process scope; plus phase-1 ADRs still in force for web station, stack, master data ownership, compose, ERP simulator.  
- Demo seeds: keep planner/operator/leader; add owner; document passwords in README when implemented.  
- CONTEXT.md currently has some duplicated phase-2 glossary blocks from iterative grilling—implementers should treat definitions as one set; a doc cleanup pass may run alongside phase-2 without changing meaning.  
- Next step after this spec: `to-tickets` into tracer-bullet issues under `.scratch/mes-phase2/issues/`, then implement in dependency order (shell + auth/landing → overview/WIP APIs → workbenches → user admin → station enhancements → e2e acceptance).  
- Success narrative for interviews/pilots: “daily operable MES with boss visibility and shop-floor station UX,” not “we listed every commercial MES module.”
