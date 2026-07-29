# NewMES

`NewMES` 是工业路由器整机装配 MES 的全新实现目录。仓库根目录下原有的 `src`、`web` 和旧脚本仅作为历史参考，不再承载新开发。

当前 Ticket 01 建立了数据库演进基线：

- SQL Server 是正式数据库，EF Core Migration 是唯一结构演进机制。
- `Mes.DbMigrator` 显式执行迁移或非生产演示初始化。
- `Mes.Api` 启动时只检查兼容性，不建库、不删库、不自动升级。
- 真实 SQL Server 门禁覆盖空库安装、带数据升级、约束、事务回滚、重复迁移、就绪检查和显式演示初始化。
- SQLite 证据不会被当作 SQL Server 发布证据；当前基线没有引入 SQLite 测试。

## 工程结构

```text
src/Mes.Domain                     领域对象
src/Mes.Infrastructure             EF Core 映射、迁移、兼容性检查、演示初始化
src/Mes.DbMigrator                 受控数据库命令
src/Mes.Api                        API 与存活/就绪检查
tests/Mes.Database.Tests           无外部数据库的快速行为测试
tests/Mes.SqlServer.IntegrationTests 真实 SQL Server 发布门禁
scripts                            可重复执行脚本
docs                               运维说明
```

## 快速验证

```powershell
dotnet build Mes.slnx
dotnet test tests\Mes.Database.Tests\Mes.Database.Tests.csproj
.\scripts\run-sqlserver-gate.ps1
```

数据库安装、升级、初始化和回退步骤见 [docs/database-operations.md](docs/database-operations.md)。
