# NewMES

`NewMES` 是工业路由器整机装配 MES 的全新实现目录。仓库根目录下原有的 `src`、`web` 和旧脚本仅作为历史参考，不再承载新开发。

Ticket 01 建立了数据库演进基线：

- SQL Server 是正式数据库，EF Core Migration 是唯一结构演进机制。
- `Mes.DbMigrator` 显式执行迁移或非生产演示初始化。
- `Mes.Api` 启动时只检查兼容性，不建库、不删库、不自动升级。
- 真实 SQL Server 门禁覆盖空库安装、带数据升级、约束、事务回滚、重复迁移、就绪检查和显式演示初始化。
- SQLite 证据不会被当作 SQL Server 发布证据；当前基线没有引入 SQLite 测试。

Ticket 02 建立了单厂私有化运行安全基线：

- Nginx 是唯一 HTTPS 入口，API、Migration 和 SQL Server 只在内部网络通信。
- 密钥、证书和数据库凭据以仓库外 secret 文件挂载；生产就绪拒绝示例密钥、演示初始化、不安全 CORS、Development 行为及高权限 API 数据库账号。
- 存活与就绪检查分离；JSON 日志和响应带关联 ID，容器日志设置大小与份数上限。
- 自动化检查覆盖解析后的 Compose 拓扑、生产配置失败条件、敏感日志边界和真实 SQL Server 权限。

Ticket 03 建立了可组合角色与业务审计上下文：

- 八类业务角色映射到具体能力，主角色只决定默认工作台。
- JWT 不固化角色；账号停用和角色变更在下一次请求重新从数据库生效。
- 全新部署通过只执行一次的运维命令建立首个系统管理员，密码来自外置 secret，不开放匿名初始化接口。
- 登录身份、有效角色和能力可通过 API 查询，账号管理和审计查询由服务端能力控制。
- 业务审计与制造事件分表保存，并由 SQL Server 拒绝更新和删除。

## 工程结构

```text
src/Mes.Domain                     领域对象
src/Mes.Infrastructure             EF Core 映射、迁移、兼容性检查、演示初始化
src/Mes.DbMigrator                 受控数据库命令
src/Mes.Api                        API 与存活/就绪检查
tests/Mes.Database.Tests           无外部数据库的快速行为测试
tests/Mes.Identity.Tests           角色能力矩阵与职责分离快速测试
tests/Mes.Security.Tests           生产配置、外置 secret、日志与就绪安全测试
tests/Mes.SqlServer.IntegrationTests 真实 SQL Server 发布门禁
scripts                            可重复执行脚本
deploy                             私有化 Compose、Nginx 和部署说明
docs                               运维说明
```

## 快速验证

```powershell
dotnet build Mes.slnx
dotnet test Mes.slnx -c Release
.\scripts\test-production-topology.ps1
.\scripts\run-sqlserver-gate.ps1
```

数据库安装、升级、初始化和回退步骤见 [数据库运维](docs/database-operations.md)；身份和审计契约见 [身份、能力与业务审计](docs/identity-access-audit.md)；生产账号、秘密、日志、备份恢复和故障处置见 [安全与运维手册](docs/security-operations.md)。
