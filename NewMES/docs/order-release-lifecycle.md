# 生产订单下达、执行快照与生命周期

Ticket 05 将 ERP 已接收订单转换为稳定的车间执行依据。下达不是简单改状态：系统必须选择该成品最新发布的执行模板，将完整定义复制为订单快照，并在同一 SQL Server 事务中提交快照、订单状态和业务审计。

## 执行模板

工艺工程师通过 `POST /api/process/execution-templates` 发布新版本，需要同时具备 `ProcessDefinitionManage` 与 `TestSpecificationApprove`。模板必须结构化包含：

- ERP/MES 已启用的成品物料及其来源版本；
- 单层 BOM 版本、组件、单位用量和各组件追溯粒度；
- 线性路线版本以及有序工序；
- 成品追溯策略和身份来源策略；
- 固件要求版本及其批准依据引用；
- 测试规范版本及其批准依据引用；
- 完成门禁版本和必备条件。

组件和成品追溯粒度必须与已启用物料主数据一致。模板保存完整 JSON、`SHA-256-JSON-V1` 哈希、适用性说明、发布主体和时间；SQL Server 触发器拒绝更新和删除，变更只能发布新版本。

仓库中的工业路由器路线与测试/固件引用仍是“技术验证样板，待现场确认”，不是行业统一 SOP。具体 BOM、站序、固件版本、测试阈值和完成责任必须在真实项目中由工艺、质量、生产、仓储与 IT 联合确认后再发布。

## 下达与快照

- `POST /api/planning/production-orders/{id}/release`：仅来源系统、业务引用和来源版本均可证明的 `Received` 订单可首次下达，需要 `ProductionOrderManage`；无法证明来源的 Legacy 订单不得下达。
- 下达复制模板完整 JSON，不只保存模板 ID 或版本字符串；快照同时保存来源模板、版本、哈希、创建主体和 UTC 时间。
- 每张订单只能有一份执行快照；并发下达收敛为同一结果，不重复快照或成功审计。
- `GET /api/planning/production-orders/{id}/execution-snapshot` 返回该订单实际冻结的完整定义。
- 快照由 SQL Server 触发器阻止更新和删除；模板发布 V2 不改变已下达订单的 V1 快照。

## 生命周期

| 当前状态 | 允许命令 | 下一状态 | 额外门禁 |
| --- | --- | --- | --- |
| `Received` | `Release` | `Released` | 已批准且完整的执行模板 |
| `Received` / `Released` | `Cancel` | `Cancelled` | `StartedQuantity = 0` |
| `Released` | 首个 `START_WIP` | `InProduction` | Ticket 07 负责身份、快照关联与制造事件 |
| `InProduction` | `Pause` | `Paused` | 暂停后 Ticket 07/后续过站服务必须拒绝新投产和正常过站 |
| `Paused` | `Resume` | `InProduction` | 保留既有在制、质量和耗料责任 |
| `InProduction` | `CompleteExecution` | `ExecutionCompleted` | 至少已有一个产品投产，全部已投产产品均合格或报废、无未决质量保留；允许保留可审计的未投产余额用于受控短结 |
| `ExecutionCompleted` | `Close` | `Closed` | 仓储交接和 ERP 对账均完成 |

计划端公共命令为 `release`、`pause`、`resume`、`cancel`、`complete-execution` 和 `close`。非法转换返回稳定代码和中文行动说明，并写拒绝审计。订单级 `CompleteExecution` 只确认全部已投产产品都已由 Ticket 13 的真实 `COMPLETE` 制造事件归入合格或报废终态，本身不生成单件制造事件。`Released -> InProduction` 不能由计划员手工点击伪造；它必须由 Ticket 07 的真实 `START_WIP` 事务触发。仓储交接和 ERP 对账标志分别由 Ticket 14、15 产生，本 Ticket 只建立关闭门禁。

## 数量平衡

订单保存计划数、投产数、合格数、报废数和未决质量保留数；工作台派生：

- `UnstartedQuantity = PlannedQuantity - StartedQuantity`
- `WipQuantity = StartedQuantity - QualifiedQuantity - ScrappedQuantity`
- `StartedQuantity <= PlannedQuantity`
- `QualifiedQuantity + ScrappedQuantity <= StartedQuantity`

SQL Server 检查约束拒绝负数、超计划投产和终态数量超过投产数。`GET /api/planning/production-orders` 返回状态、快照版本、六类数量和当前真正可执行的命令。

## 审计与事务

模板发布、下达和每次生命周期成功变更均记录主体、有效角色快照、授权角色、动作、订单和关联 ID。权限拒绝、模板缺失、非法转换、数量未终态、质量保留未解和跨系统未对账也记录原因码。状态、快照与成功审计在同一数据库事务提交。
