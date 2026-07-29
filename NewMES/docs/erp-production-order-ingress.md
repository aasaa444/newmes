# ERP 生产订单幂等入站

Ticket 04 实现 ERP 计划权威进入 MES 执行域的第一条正式契约。真实 ERP 适配器和 Development 环境的 ERP 模拟器都调用同一个 `ProductionOrderIngressService`，不得直接写 `mes.ProductionOrders`。

## HTTP 契约

- `POST /api/integration/erp/production-orders`：正式入站，需要 `ProductionOrderManage`。
- `POST /api/simulator/erp/production-orders`：仅 Development 映射，使用完全相同的请求、授权、Inbox 和业务服务。
- `GET /api/planning/production-orders`：计划工作台查询，需要 `ProductionOrderRead`，返回来源系统、业务键、来源版本和入站结果。

当前契约版本为 `1.0`：

```json
{
  "sourceSystem": "ERP-U8",
  "messageId": "MSG-ERP-0401",
  "businessKey": "PO-ERP-0401",
  "sourceVersion": "7",
  "contractVersion": "1.0",
  "orderNumber": "PO-ERP-0401",
  "materialCode": "ROUTER-FG-01",
  "plannedQuantity": 10
}
```

## 幂等与冲突

Inbox 以 `SourceSystem + MessageId` 唯一标识消息，并保存业务键、来源版本、契约版本、SHA-256 载荷校验值、处理状态、HTTP 状态和业务结果。

- 首次有效消息在一个 SQL Server 事务中写入 Inbox、`Received` 工单和成功审计。
- 完全相同的消息重投直接返回 Inbox 中的首次结果，不产生新工单或审计。
- 同一消息 ID 的载荷不同返回 `INBOUND_IDEMPOTENCY_CONFLICT`，并在冲突表保存首次和本次载荷校验值。
- 同一 ERP 业务键使用新消息提交时返回 `PRODUCTION_ORDER_CHANGE_REQUIRED`，不覆盖现有工单；受控变更属于 Ticket 05。
- 使用至少一次投递和幂等消费语义，不宣称严格 exactly-once。

## 稳定拒绝

| 代码 | 含义与行动 |
| --- | --- |
| `INBOUND_ENVELOPE_INVALID` | 缺少来源系统或消息 ID；ERP 集成负责人修正消息信封 |
| `CONTRACT_VERSION_UNSUPPORTED` | 契约不是 `1.0`；适配器转换后重试 |
| `PRODUCTION_ORDER_PAYLOAD_INVALID` | 订单字段缺失或计划数量非法；ERP 计划数据负责人修正 |
| `MATERIAL_NOT_FOUND` | MES 中没有启用的成品物料；先完成主数据维护 |
| `INBOUND_IDEMPOTENCY_CONFLICT` | 相同消息 ID 出现不同载荷；核对重发内容并换新消息 ID |
| `PRODUCTION_ORDER_CHANGE_REQUIRED` | 工单已存在；进入计划员受控变更流程 |

缺少来源系统或消息 ID 时无法形成可靠幂等身份，因此返回 400 并写拒绝审计，但不伪造 Inbox 标识。其他具有完整消息信封的业务拒绝保存为 `Rejected` Inbox，完全重投仍返回首次拒绝结果。
