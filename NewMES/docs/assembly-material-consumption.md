# 装配绑定与三种粒度物料耗用

Ticket 08 把工单发料后的“可用物料”转化为具体成品的“实际用料”。工单发料仍只是物料责任分配；只有装配命令成功后，系统才追加 `Consumption` 事务并把它纳入成品谱系。

## 冻结执行规则

订单执行快照中的每项直接物料固定以下内容：物料编码、单位用量、基础单位、追溯粒度、装配工序和耗用规则。发布新模板时这些字段必须完整；旧快照缺失装配规则时执行会以稳定错误停止，不会用代码默认值补造证据。已下达订单不会因模板或物料主数据后续停用、改单位、改追溯粒度而改变执行依据。

| 追溯粒度 | 工位采集 | 关系与耗用 |
| --- | --- | --- |
| `Serial` | 部件 SN、责任 Lot；每次 1 个基础单位 | 创建唯一有效的“成品 SN -> 部件 SN”关系，同时追加实际耗用 |
| `Lot` | 供应/生产 Lot、实际数量 | 不伪造部件 SN 关系；把 Lot 和实际数量关联到成品并追加耗用 |
| `Quantity` | 责任 Lot；按规则采集实际数量或不填数量 | `PerProductActual` 由操作工填实际数量；`OrderBackflush` 由冻结 BOM 自动计算，操作工不能改写 |

内部主数据沿用 `TraceabilityMode.None` 表示数量粒度，公共装配 API 显示为 `Quantity`。即使数量件不做召回粒度的 Lot 追溯，物料事务仍保留用于线边责任账扣减的 Lot，避免产生无法核对的库存余额。

## 原子门禁

耗用在 SQL Server `Serializable` 事务中完成以下校验和写入：

1. 成品已完成 `START_WIP`，订单处于 `InProduction`，当前工序与请求及快照一致；
2. 物料、单位、追溯粒度、耗用规则与冻结 BOM 及主数据一致；
3. 该 Lot 已完成线边交接并向当前订单发料，工单可用余额充足；
4. 本成品剩余需求足够，序列件部件 SN 没有其他有效成品关系；
5. 同时追加耗用事务、部件关系（仅序列件）、制造事件、成功审计和永久幂等回执；
6. 当前工序的全部物料满足后，追加 `ASSEMBLY_OPERATION_COMPLETED` 并推进到冻结路线的下一工序。

任一门禁失败时不产生关系、耗用或工序推进。`SourceSystem + IdempotencyKey` 完全重投返回首次结果；相同键不同载荷拒绝覆盖。

## 纠错与历史

解绑和替换必须填写原因并引用原绑定。系统把原关系转为非活动状态，追加 `COMPONENT_UNBOUND`，并以引用原 `Consumption` 的 `Reversal` 精确冲回物料。替换在同一事务中再追加新关系、新耗用和相应事件；不会删除原关系、原事务或原事件。

Lot 件和数量件没有部件关系可解绑，使用独立耗用冲正 API。冲正引用原 `Consumption`，追加 `Reversal` 与 `MATERIAL_CONSUMPTION_REVERSED`；成品谱系同时显示原耗用、冲正事务、原事务引用、纠错原因和正负净耗用量。

出现固件、测试、质量等后续制造事实后，不能再用装配纠错绕过返工流程。独立解绑会把当前工序恢复为原装配工序；原命令回执不会随关系状态改变，因此重复解绑或替换不会二次冲正。

## 公共 API

- `POST /api/execution/assembly/{finishedSerialNumber}/materials/consume`
- `GET /api/execution/assembly/{finishedSerialNumber}/materials`
- `POST /api/execution/assembly/bindings/{bindingId}/unbind`
- `POST /api/execution/assembly/bindings/{bindingId}/replace`
- `POST /api/execution/assembly/consumptions/{transactionId}/reverse`
- `GET /api/genealogy/products/{finishedSerialNumber}/materials`
- `GET /api/genealogy/components/{componentSerialNumber}/products`
- `GET /api/genealogy/lots/{lotNumber}/products?materialCode=...`

装配写命令需要 `StationExecute`；装配物料需求是工位视图，同样需要 `StationExecute`。成品、关键件和 Lot 谱系查询需要独立的 `GenealogyRead`，授予操作工、线长、质量工程师和运营经理，但该只读能力不授予装配写权限。

Ticket 08 的查询只聚合物料关系和耗用。固件、测试、质量、完工、仓储和 ERP 回执将在后续 Ticket 完成后由完整谱系端点统一聚合。
