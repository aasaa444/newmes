# 受控产品身份、标签与 START_WIP

Ticket 07 把“获得身份”和“投入生产”分成两个受控动作。ERP、标签系统或 MES 受控号池先登记有来源证明的身份；`START_WIP` 只把已有身份绑定到生产订单、成品物料和订单冻结的执行快照，不在工位随机生成生产 SN。

## 身份来源

每个产品身份必须有且仅有一个 `SerialNumber`，还可包含 `MacAddress`、`Imei` 和 `Certificate`。每个标识分别保存来源类型、来源系统和来源引用：

| 来源类型 | 用途 | 生产标记 |
| --- | --- | --- |
| `Erp` | ERP 已分配的成品 SN 或受控标识 | 生产 |
| `LabelSystem` | 获得分配权的标签系统 | 生产 |
| `MesControlledPool` | MES 通过受控号池适配器取得的号码 | 生产 |
| `DemoControlledPool` | 演示和技术验证数据 | 明确 `IsDemo = true`，不能冒充生产来源 |

来源必须先通过配置接口注册，记录来源类型、授权依据、允许分配的标识类型和唯一授权调用账号。身份登记同时核对当前 JWT 账号、来源注册与标识类型，不能靠请求中的字符串自称 ERP 或号池。`ControlledIdentifier.Type + Value` 由 SQL Server 全局唯一约束保护；同一个产品也不能重复拥有同类型标识。`SourceSystem + IdempotencyKey` 保证完全重投返回原身份，相同键不同载荷拒绝覆盖。

## 订单快照门禁

订单下达时冻结的 `IdentityPolicy` 决定成品 SN 来源、必需标识和分配时机。分配时机支持 `AtOrderRelease`、`BeforeStartWip` 和 `OnDemandBeforeStartWip`；后两者都要求先通过独立受控分配命令获得身份，`START_WIP` 本身不发号。`START_WIP` 在一个 Serializable SQL Server 事务中检查：

1. 订单状态只能是 `Released` 或 `InProduction`；暂停、未下达及终态订单拒绝新投产；
2. 身份尚未绑定、未作废，且成品物料与订单一致；
3. SN 来源满足 `FinishedSerialSource`；
4. `RequiredIdentifiers` 中的所有标识均已从受控来源登记；
5. 订单投产数小于计划数，当前策略禁止未经受控订单变更的超投；
6. 路线首个事件是 `START_WIP`，下一工序从冻结路线读取而不是写死。

成功后同一事务更新身份绑定、订单投产数和首件状态 `Released -> InProduction`，追加 `IDENTITY_BOUND`、`START_WIP`、成功业务审计和不可改写的投产命令回执。回执独立于当前绑定，因此身份解绑或作废后，`SourceSystem + IdempotencyKey` 完全重投仍返回首次结果，不会重复增加数量或事件。

## 身份与标签事件

身份当前状态保存在 `ProductIdentities`，历史保存在只追加 `ManufacturingEvents`。错误绑定在尚无后续制造事实时可以填写原因解绑：系统追加 `IDENTITY_UNBOUND` 和引用原 `START_WIP` 的 `START_WIP_REVERSED`，保持原事实可见并恢复订单数量；已有后续制造事实时必须进入后续返工或质量流程，不能直接解绑。只有未绑定身份可以作废。

`ProductLabels` 只保存标签载体的当前状态。打印、补打、作废和换标分别追加 `LABEL_PRINTED`、`LABEL_REPRINTED`、`LABEL_VOIDED` 和 `LABEL_REPLACED`；换标创建新标签并把旧标签标为 `Replaced`，不会覆盖原记录。补打、作废、换标必须填写原因。

## 公共 API

- `POST /api/integration/product-identities`
- `POST /api/configuration/identity-sources`
- `POST /api/execution/production-orders/{orderId}/start-wip`
- `GET /api/execution/product-identities/by-serial/{serialNumber}/workstation`
- `POST /api/execution/product-identities/{identityId}/unbind`
- `POST /api/execution/product-identities/{identityId}/void`
- `POST /api/execution/product-identities/{identityId}/labels`
- `POST /api/execution/product-identities/{identityId}/labels/{labelId}/reprint`
- `POST /api/execution/product-identities/{identityId}/labels/{labelId}/void`
- `POST /api/execution/product-identities/{identityId}/labels/{labelId}/replace`

身份登记需要 `SystemConfigurationManage`，由系统管理员承担授权适配器接入责任；投产、身份纠错和标签命令需要 `StationExecute`。工位查询返回身份来源、演示标记、当前订单、快照版本、全部受控标识和下一应执行工序，并对不存在、错产品、错来源和缺失标识返回稳定错误码与中文处理建议。
