# 私有化安全与运维手册

本文适用于单厂 Linux 主机上的 Docker Compose 试点。它定义可重复执行的最低基线，不代表零停机、高可用、异地灾备或企业 SSO 承诺。

## 信任边界

- Nginx 是唯一用户入口，只发布 HTTPS 443；API、Migration 和 SQL Server 不发布宿主端口。
- `backend` 网络设置为 `internal: true`。API 信任该网络内 Nginx 传入的转发协议和关联 ID，不应脱离该边界直接暴露。
- API 和 Migration 镜像以非 root 用户运行、使用只读根文件系统，并移除 Linux capabilities。
- SQL Server 默认使用可生产许可的 Express；客户有相应授权时可显式选择批准的 edition，禁止用 Developer 承载正式数据。
- 同源部署默认不启用 CORS。确需跨域时，只允许明确的非回环 HTTPS 来源；通配符、HTTP 和 localhost 会阻断生产就绪。

## 账号与最小权限

数据库账号必须分离：

| 身份 | 使用时机 | 允许范围 | 禁止范围 |
| --- | --- | --- | --- |
| SQL 管理账号 | 初次建库、账号管理、应急 DBA 操作 | 受控维护窗口 | API、日常 Migration |
| Migration 账号 | 安装和升级窗口 | 当前版本 Migration 所需 DDL/DML | API 运行、长期交互登录 |
| API 运行账号 | API 生命周期 | 仅当前业务表和执行存储过程所需权限 | `sysadmin`、`db_owner`、数据库或服务器 `CONTROL`、DDL |

账号由客户 DBA 按企业规范创建。每次授权变更后，先用 API 连接串运行 `/health/ready`；安全检查会主动拒绝高权限连接。权限不足则数据库兼容性或业务冒烟失败，禁止用追加 `db_owner` 的方式绕过，应补齐精确的对象或 schema 权限并记录变更。

## 外置秘密

以下文件放在仓库和镜像之外，只允许部署账号读取：API 连接串、Migration 连接串、JWT 签名密钥、SQL 管理员初始密码、TLS 证书和私钥。文件路径通过 `NEWMES_*_FILE` 环境变量交给 Compose，秘密内容不作为环境变量传入容器。

- JWT 签名密钥至少 32 个随机字符，不得包含 `demo`、`sample`、`changeme` 等示例标记。
- 轮换数据库凭据时，先创建新凭据并验证就绪，再撤销旧凭据；不要原地覆盖后直接删除回退路径。
- 轮换证书后重新加载代理并执行 HTTPS 冒烟，检查证书链、主机名和有效期。
- 不在工单、日志、截图或聊天记录中粘贴秘密值；只记录秘密版本、保管位置和轮换时间。

## 启动与升级

1. 执行 `scripts/test-production-topology.ps1`，确认端口隔离、生产环境、外置 secret 和日志轮转配置。
2. 检查最近一次可恢复备份；升级前额外执行一次完整备份。
3. 单独启动 SQL Server，在维护窗口运行 `Mes.DbMigrator migrate`。
4. Migration 成功后启动 API 和 Nginx；失败则保持 API 隔离，不修改迁移历史。
5. 对受信任 HTTPS 地址执行 `scripts/test-production-deployment.ps1`。

完整命令见 [部署说明](../deploy/README.md)，数据库迁移与回退边界见 [数据库运维](database-operations.md)。

## 健康检查与故障定位

- `/health/live` 只证明 API 进程可响应，不访问数据库，也不返回配置。
- `/health/ready` 验证数据库版本、生产配置和 API 数据库账号权限。失败返回 HTTP 503、检查状态和稳定代码，不返回连接串、账号、密钥或异常详情。
- `/api/system/info` 是当前关键 API 冒烟入口，并回显安全的 `X-Correlation-ID`。

先按就绪响应中的代码分类处理：`DB_*` 进入数据库运维流程，`SEC_DATABASE_HIGH_PRIVILEGE` 收紧 API 账号，其他 `SEC_*` 修正对应生产配置。不要为了恢复 200 响应而关闭门禁。

## 日志与审计

API 在 Production 输出 JSON 日志；Nginx 输出 JSON access log。两者只记录关联 ID、方法、路径、状态和耗时等运维字段，不记录 Authorization、密码或查询参数。Compose 将每个容器日志限制为 10 MB、保留 5 份；生产主机仍需将日志转存到受控介质并按工厂制度设置访问权限和保留期。

技术日志不替代制造事件和业务审计。制造事实继续进入追加式制造事件存储；业务审计能力在对应领域 Ticket 实现前不得宣称已交付。

## 备份与恢复演练

试点目标是 **RPO 24 小时、RTO 4 小时**，这是需要演练证明的目标，不是高可用承诺。

- 每日执行一次 SQL Server 完整备份并启用 checksum；升级前额外备份。
- 用 `RESTORE VERIFYONLY` 检查备份介质，并将备份复制到独立、受限且被监控容量的存储位置。只留在 SQL Server 数据卷内不算有效备份。
- 至少每季度在隔离环境执行恢复演练：恢复最近备份、启动匹配版本应用、验证迁移版本、代表性工单和成品谱系查询，并记录实际恢复时间。
- 演练超过 4 小时或可恢复点超过 24 小时时，登记风险并调整备份频率、介质或恢复步骤。

正式回退以升级前已验证备份为准，不使用 Down Migration 猜测性回退。恢复后重新执行就绪检查和 HTTPS 冒烟，再解除维护窗口。
