# 版本化测试规范与逐项测试执行

Ticket 10 把“测试定义”和“测试事实”分开管理。规范说明什么需要测、采用什么单位和判定规则；执行记录说明某台成品在某次测试中实际提交了什么证据。系统不内置工业路由器的真实电压、端口或性能阈值，本文和集成测试中的数值只用于验证判定机制，生产阈值必须来自客户批准的产品规范或测试 SOP。

## 规范版本与职责分离

工艺工程师使用 `ProcessDefinitionManage` 创建不可重号的规范草稿，质量工程师使用独立的 `TestSpecificationApprove` 能力批准版本；两类角色都以 `TestSpecificationRead` 读取草稿、定义和批准证据。批准是规范唯一允许的原地状态转换，SQL Server 触发器只允许一次 `Draft -> Approved`，并拒绝删除、改写定义或改写已有批准。批准字段由检查约束保证成组为空或成组完整。

完整规范的规范化表示覆盖编码、版本、适用物料、工序、适用性和全部有序项目，并计算 `SHA-256-JSON-V1` 哈希。执行模板发布只需要工艺定义权限，但引用的每个规范必须已经批准，且适用物料和工序必须存在于模板产品与路线中，否则返回 `TEST_SPECIFICATION_NOT_APPROVED` 或 `TEST_SPECIFICATION_NOT_APPLICABLE`。

订单下达时，模板中的规范编码、版本、产品、工序、批准依据、定义哈希、哈希算法和全部项目被复制进执行快照。后续新建或批准的规范版本不会改变已下达订单。

规范项目当前支持：

- `Numeric`：必需单位、0 至 6 位允许小数位，以及至少一个上下限；上下限均按包含边界判定，规范限值和实测值必须处于 SQL Server `decimal(18,6)` 的可保存范围。
- `Text`：无单位，按区分大小写的精确期望值判定。
- `Boolean`：无单位，按明确的 `true` 或 `false` 期望值判定。

具体阈值、精度、单位和期望值属于客户受控输入。缺少依据时应保留为 Fit-Gap 或 RAID 项，不从前端、互联网示例或开发者经验补造。

## 测试执行

操作员只能在订单为 `InProduction`、成品已绑定执行快照且当前工序等于冻结规范工序时提交。一次执行保存成品 SN、订单、快照、规范版本与定义哈希、设备和版本、夹具和版本、原始报告引用、操作主体、位置、起止时间、记录时间、关联 ID 和制造事件。

每项测量独立保存未经裁剪的原始字符串、冻结名称和数据类型、单位、精度、限值或期望值、解析后的数值或布尔值、MES 判定、MES 诊断以及设备上报诊断。服务端拒绝未知项、重复项、单位不符、数据类型不符、超过冻结精度或 SQL 存储范围的数据；客户端不能提交或覆盖总体 Pass/Fail。

如果测试台已经提交一次执行但漏掉部分必测项，该执行仍形成 `Failed` 事实：缺项以空原始值和 `TEST_MEASUREMENT_REQUIRED` 诊断保存，工作台和谱系可以看到失败并要求复测。如果请求连一次测量、设备、夹具、原始报告或有效时间都没有，则视为尚未形成可确认的执行命令，返回中文可行动的 `422 TEST_RUN_INVALID`，不伪造运行事实。

一次执行中所有已提交项目均通过时，总体结果才是 `Succeeded`。失败执行仍完整提交，但不推进路线；复测必须以 `RetryOfTestRunId` 引用同一成品、同一冻结规范的最新失败。复测创建新的运行、测量和制造事件，原失败永远保留。

同一工序可能有多个必需规范。只有该工序全部必需规范都已有唯一成功事实时，系统才追加 `TEST_OPERATION_COMPLETED` 并把成品推进到冻结路线下一工序。验证、运行、测量、事件、路线状态和成功审计处于同一 SQL Server 事务；任一落库失败会整体回滚。

## API 与权限

- `POST /api/quality/test-specifications/versions`：工艺工程师创建草稿。
- `POST /api/quality/test-specifications/{code}/versions/{version}/approve`：质量工程师批准。
- `GET /api/quality/test-specifications/{code}/versions/{version}`：读取规范与批准证据。
- `POST /api/execution/tests/{finishedSerialNumber}/runs`：操作员提交一次逐项测试。
- `GET /api/execution/tests/{finishedSerialNumber}`：测试工作台读取冻结规范、当前状态和全部运行。
- `GET /api/genealogy/products/{finishedSerialNumber}/tests`：产品谱系读取全部运行和测量。

执行与工作台使用 `StationExecute`，产品谱系使用 `GenealogyRead`。相同 `SourceSystem + IdempotencyKey` 的完全重投返回原事实；相同键的不同载荷返回 `TEST_RUN_IDEMPOTENCY_CONFLICT`。SQL Server 的过滤唯一索引保证同一成品、同一冻结规范最多一条成功事实，运行和测量表的触发器拒绝普通更新或删除。

## 现场接入边界

当前 HTTP 契约接受测试台或适配器提交的原始测量和报告引用，不直接控制仪器，也不解析某家厂商的二进制报告。真实项目进入 UAT 前至少需要确认测试设备协议、设备与夹具主数据、校准状态来源、报告存储和保留期限、时间同步、工位认证、规范审批流程、失败代码字典以及返工/偏差放行流程。Ticket 11 和 12 将在现有失败事实之上建立不合格、质量保留、处置与受控复测闭环。
