# 身份、能力与业务审计

Ticket 03 建立本地账号的授权上下文。JWT 只保存用户 ID 和用户名，不保存角色或能力；每次已认证请求都从 SQL Server 重新读取账号状态和角色，因此停用账号或调整角色会影响下一次请求。

## 业务角色

系统支持计划员、工艺工程师、操作工、产线主管、质量工程师、物料交接员、系统管理员和运营负责人。一个账号可组合多个角色，`PrimaryRole` 只决定默认工作台。服务端通过 `BusinessCapability` 判定动作，并在审计中记录实际授予能力的角色。

- 操作工和产线主管不能批准质量处置或解除质量保留。
- 系统管理员可以管理账号、角色和技术配置，但不自动获得订单、工艺或质量审批能力。
- 运营负责人只有运营查询能力，不执行或批准生产动作。

## API

- `POST /api/auth/login`：本地密码登录，成功和失败均写审计；失败响应不区分账号不存在、密码错误或账号停用。
- `GET /api/identity/me`：返回当前登录身份、主角色、有效角色和能力。
- `PUT /api/admin/accounts/{id}/roles`：调整组合角色和主角色，需要 `AccountManage`。
- `PUT /api/admin/accounts/{id}/active`：启用或停用账号，需要 `AccountManage`。
- `GET /api/admin/audit`：查询最近业务审计，需要 `BusinessAuditRead`。

JWT 签名密钥继续使用 Ticket 02 的外置 `Security__JwtSigningKey`。密码使用 ASP.NET Core `PasswordHasher`，数据库只保存哈希。历史账号在 Migration 中不会被补造密码或角色；未完成受控初始化的账号不能登录。

## 审计边界

`audit.BusinessAuditRecords` 与 `mes.ManufacturingEvents` 分表保存。审计记录包含主体快照、授权角色、能力、动作、业务对象、结果、原因码、UTC 时间和关联 ID。SQL Server 触发器拒绝普通 `UPDATE` 和 `DELETE`；运行日志轮转不删除业务审计。

账号角色/状态的成功变更与成功审计在同一个数据库提交中完成。能力拒绝会单独保存拒绝审计后返回 403。业务服务后续接入时必须复用能力判定和审计上下文，前端隐藏按钮不能替代服务端授权。
