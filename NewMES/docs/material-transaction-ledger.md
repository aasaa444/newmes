# 线边物料交接与工单发料事务账

Ticket 06 建立制造侧物料责任账，不扩展为完整 WMS。系统只记录物料进入 MES 线边责任范围、分配给生产订单、从订单退回、冲正和受控调整；库位、波次、盘点任务和财务库存仍由 ERP/WMS 负责。

## 事务语义

| 类型 | 线边增量 | 工单可用增量 | 净发料增量 | 说明 |
| --- | ---: | ---: | ---: | --- |
| `LineSideTransfer` | `+数量` | `0` | `0` | 仓储/ERP/WMS 与物料交接员确认物料进入线边，不自动分配订单 |
| `OrderIssue` | `-数量` | `+数量` | `+数量` | 把线边物料分配给已下达或执行中的订单，不产生产品实际耗用 |
| `OrderReturn` | `+数量` | `-数量` | `-数量` | 把尚未耗用的订单物料退回线边，必须填写原因 |
| `Reversal` | 原事务三个增量的相反数 | 同左 | 同左 | 精确补偿一条可冲正原事务，必须引用原事务并填写原因 |
| `Adjustment` | `+/-数量` | `0` | `0` | 受控线边调整，必须填写原因且不得形成负库存 |
| `Consumption` | `0` | `-数量` | `0` | 只保留数据库事务类型；Ticket 08 在具体工序、成品 SN 和追溯门禁下追加 |

`LineSideBalance`、`OrderAvailableBalance` 和 `NetIssuedQuantity` 都由上述增量求和，不存在普通 API 可直接修改的余额字段。事务表由 SQL Server 拒绝更新和删除；更正只能追加退料、冲正或调整。

## 单位与批次

物料基础单位由 ERP 主数据权威提供，`Material.BaseUnit` 是记账校验依据。升级前旧物料的单位保持 `null`，表示无法证明，不会批量猜测为 `EA`；这类物料必须先通过受控主数据同步补齐单位才能记账。所有 Ticket 06 事务要求明确 Lot，用 `decimal(18,6)` 保存数量并严格使用基础单位。

## 公共 API

- `POST /api/material/line-side-transfers`
- `POST /api/material/order-issues`
- `POST /api/material/order-returns`
- `POST /api/material/adjustments`
- `POST /api/material/transactions/{id}/reverse`
- `GET /api/material/workbench?materialCode=&lotNumber=&productionOrderId=&transactionType=`

写接口需要 `MaterialTransactionExecute`，当前由物料交接员承担。操作工不能用这些接口把发料冒充耗用；后续工位服务只能通过 Ticket 08 的制造命令追加 `Consumption`。每个命令使用 `SourceSystem + IdempotencyKey` 唯一标识：完全重投返回原事务，不重复记账；相同键不同内容返回 `MATERIAL_IDEMPOTENCY_CONFLICT`。

`line-side-transfers` 是物料交接员确认物理责任已经转移的 MES 领域命令，来源系统和单据仅作为可核对依据，不是让 WMS 匿名直写事务账的集成入口。未来自动化 ERP/WMS 适配器必须先经过通用 Inbox 保存原始载荷和处理结果，再以受控服务身份调用同一领域命令；该适配器不在 Ticket 06 范围内。

## 余额和超发门禁

发料在 SQL Server Serializable 事务中同时校验：

1. 物料存在、启用且单位与主数据一致；
2. 订单已经下达且尚未关闭或取消；
3. 物料存在于订单冻结 BOM，单位与快照一致；
4. 指定物料和 Lot 的线边余额不会小于零；
5. 工单 Lot 可用量不会小于零；
6. 所有 Lot 的净发料总量不超过 `订单计划数量 × 快照 BOM 单位用量`。

使用独立的净发料增量是为了避免耗用后工单可用量下降，随后被错误地再次发料并突破 BOM 总需求。并发请求依靠 SQL Server 隔离级别、余额索引和唯一键收敛，不使用单进程内存锁。

## 审计和失败边界

成功物料事务和业务成功审计在同一事务提交。可行动的业务拒绝保存拒绝审计；数据库异常则回滚物料事务和成功审计，不留下部分余额。SQL Server 插入触发器还会拒绝不精确的冲正以及导致线边或工单可用量为负的直接写入。
