# 旧一期技术验证记录

> **历史状态，已被领域救援重评估取代。** 本页的勾选只表示旧规则曾通过当时的演示与 SQLite/API 自动化测试，不表示当前领域模型已经实现，也不表示制造从业者评审或真实现场验收。新的版本化证据包建立后，本页不再作为发布门禁。

以下内容对照当时的 `CONTEXT.md` 第一期完成定义与 `.scratch/mes-first-phase/spec.md` 保留，便于回归旧行为和解释项目演进。

## A. 完成定义四条（必须）

| # | 要求 | 证据 | 状态 |
|---|------|------|------|
| 1 | 黄金路径：种子 → 下达 → 领料 → 过站绑关键件 → 合格入库 → 谱系 | UI：`docs/DEMO.md` §1；自动化：`Phase1E2eSeamTests.Phase1_golden_path_*` | ✅ |
| 2 | 异常支线：不合格 → 隔离 → 放行/返工/报废 | UI：`docs/DEMO.md` §2；自动化：`Phase1_exception_isolate_release_and_scrap_branch` | ✅ |
| 3 | 三角色 RBAC + 关键业务审计可查 | 登录三账号；操作工写主数据 403；`GET /api/audit`；E2E 断言审计动作 | ✅ |
| 4 | ERP 回写模拟器：完工/耗料可查报文 | `GET /api/erp/outbox`；`MaterialIssue` / `ProductionReceipt` / `WorkOrderClose` | ✅ |

## B. 关键规则（高缝）

| 规则 | 证据 | 状态 |
|------|------|------|
| 防跳站 | `StationPassSeamTests` + E2E 非首站失败 | ✅ |
| 取消仅无在制 SN | `Phase1_cancel_only_without_wip` | ✅ |
| 隔离不可入库 | `CompletionSeamTests` + E2E 异常支线 | ✅ |
| 关单后只读 | `Close_work_order_then_issue_is_rejected` | ✅ |
| 线边 / 在制 / 成品分账 | 领料降线边、入库增成品 | ✅ |

## C. 工程与交付

| 项 | 状态 |
|----|------|
| ASP.NET Core + SQL Server（LocalDB 开发）+ Vue 3 | ✅ |
| Docker Compose 文件（引擎可用时） | ✅ 文件在仓；本机引擎未强制实跑 |
| 中文领域注释 + `CONTEXT.md` + ADR | ✅ |
| 全量单元/集成测试 | `dotnet test` **41** 用例（含票 07 E2E） |

## D. 明确不在第一期（Out of Scope — 未偷加）

- 真连用友/金蝶/SAP  
- 完整 WMS、APS、PLC、离线过站  
- 多工厂 / 多租户 SaaS  
- SPC / 完整 QMS / 标签打印 / 压测报表中心  

## E. 验收命令

```powershell
dotnet test D:\Game\MES\src\Mes.Api.Tests
dotnet test D:\Game\MES\src\Mes.Api.Tests --filter "FullyQualifiedName~Phase1E2eSeamTests"
cd D:\Game\MES\web; npm run build
```

人工：按 `docs/DEMO.md` 点一遍黄金 + 异常路径。

## F. 结论

**第一期（票 01–07）满足完成定义，可演示、可测、可私有化启动。**  
后续可选：EF Migration、真 ERP 适配器、更精致 UI、票外产线报表。
