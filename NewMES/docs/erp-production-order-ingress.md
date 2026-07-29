# ERP 生产订单幂等入站

Ticket 04 实现 ERP 计划权威进入 MES 执行域的第一条正式契约。真实 ERP 适配器和 Development 环境的 ERP 模拟器都调用同一个 `ProductionOrderIngressService`，不得直接写 `mes.ProductionOrders`。

## HTTP 契约

- `POST /api/integration/erp/production-orders`：正式入站，需要 `ProductionOrderManage`。
- `POST /api/simulator/erp/production-orders`：仅 Development 映射，使用完全相同的请求、授权、Inbox 和业务服务。
- `GET /api/planning/production-orders`：计划工作台查询，需要 `ProductionOrderRead`；响应分为 `orders` 与 `inboundResults`，无效契约、未知物料等未创建订单的拒绝结果也必须可见。

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

Inbox 以 `SourceSystem + MessageId` 唯一标识消息，并保存消息类型、业务键、来源版本、契约版本、完整原始 JSON、原文 SHA-256 校验值、处理状态、HTTP 状态和业务结果。未知扩展字段也属于原始载荷；同一消息 ID 下任何原文差异都会形成冲突，不会被模型绑定静默丢弃。

- 首次有效消息在一个 SQL Server 事务中写入 Inbox、`Received` 工单和成功审计。
- 完全相同的消息重投直接返回 Inbox 中的首次结果，不产生新工单或审计。
- 同一消息 ID 的载荷不同返回 `INBOUND_IDEMPOTENCY_CONFLICT`，并在冲突表保存首次和本次载荷校验值。
- 同一 ERP 业务键使用新消息提交时返回 `PRODUCTION_ORDER_CHANGE_REQUIRED`，不覆盖现有工单；受控变更属于 Ticket 05。
- 使用至少一次投递和幂等消费语义，不宣称严格 exactly-once。

## 载荷安全与保留

- ERP 入站契约不得携带密码、访问令牌、私钥或与生产订单无关的个人敏感信息；适配器负责人必须在发送前完成字段白名单控制。
- 原始载荷只保存在 `integration.IntegrationInboxMessages`，不写入应用日志，也不通过计划工作台 API 返回；访问权限按业务审计数据级别限制给集成运维、审计和受权排障人员。
- 原始载荷与 Inbox 处理结果按工厂订单追溯和审计制度共同保留，不得早于关联生产订单及制造证据的保留期清理；正式保留年限必须在上线前由客户质量、法务与 IT 共同确认。
- 从早期 `004` 结构升级的旧 Inbox 无法反向恢复原始报文，`PayloadJson = null` 明确表示该证据不可证明；系统不会用重建 JSON 伪装原始载荷。

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
