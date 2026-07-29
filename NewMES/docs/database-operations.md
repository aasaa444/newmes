# SQL Server 安装、升级与回退

## 不可绕过的边界

- 只有 `Mes.DbMigrator migrate` 可以改变正式数据库结构。
- API 不调用 `EnsureCreated`、`EnsureDeleted` 或 `Migrate`，也不根据运行时表结构猜测版本。
- 正式环境不自动创建账号、物料、库存或订单。
- 演示数据只允许在非生产环境通过显式命令装载，且所有业务编号带 `DEMO-` 标识。
- 迁移失败后 API 就绪检查保持失败；不得手工写迁移历史或把不完整数据库标记为可用。

## 全新安装

1. 创建空数据库和最小权限部署账号。部署账号在迁移窗口内需要 DDL 权限，API 运行账号只保留业务运行所需 DML 权限。
2. 通过环境变量提供连接串，连接串不得提交到仓库。
3. 显式执行迁移。
4. 启动 API 并验证 `/health/live` 返回成功、`/health/ready` 返回 `DB_READY`。

```powershell
$env:ConnectionStrings__MesDatabase = '<受控连接串>'
dotnet run --project src\Mes.DbMigrator\Mes.DbMigrator.csproj -- migrate
dotnet run --project src\Mes.Api\Mes.Api.csproj
```

## 非生产演示数据

必须同时满足“环境不是 Production”和“显式确认”两个条件。命令可重复执行，不会重复创建演示账号、物料或工单。

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ConnectionStrings__MesDatabase = '<非生产连接串>'
dotnet run --project src\Mes.DbMigrator\Mes.DbMigrator.csproj -- seed-demo --confirm-non-production
```

## 版本升级

1. 宣布维护窗口，停止写入并记录应用提交号、数据库名、已应用迁移和当前就绪结果。
2. 执行 SQL Server 完整备份；使用 `RESTORE VERIFYONLY` 验证备份可读。没有已验证备份不得升级。
3. 在隔离环境从该备份恢复一次，并在恢复副本上先执行迁移与冒烟验证。
4. 对正式数据库运行相同的 `migrate` 命令。命令非零退出时立即停止，不启动新版本 API。
5. 验证 `/health/ready` 为 `DB_READY`，并复核升级前选定的代表性工单仍可读取。

每个 EF Migration 默认在事务中执行。若后续某个 Migration 失败，已完成的早期版本可能仍留在迁移历史中，但 API 会因存在待执行迁移而拒绝就绪，不会把该数据库伪装成当前版本。

## 回退与失败恢复

数据库回退以升级前完整备份为准，不把 `dotnet ef database update <old>` 当作正式回退方案，因为 Down Migration 可能丢失升级后数据。

1. 保持 API 停止或隔离，保存迁移器错误和 SQL Server 日志。
2. 不修改 `__EFMigrationsHistory`，不运行自动删库重建脚本。
3. 将升级前备份恢复到隔离数据库，验证迁移版本、代表性数据和只读查询。
4. 经变更负责人确认后切换到已验证恢复库，或修复 Migration 后从原备份重新演练升级。
5. 恢复服务后再次验证就绪检查和代表性工单；记录实际恢复时间与证据位置。

## 就绪状态代码

| 代码 | 含义 | 运维动作 |
| --- | --- | --- |
| `DB_READY` | 结构与应用版本一致 | 可以进入就绪状态 |
| `DB_UNAVAILABLE` | 数据库缺失或不可连接 | 检查数据库、网络、账号和连接串 |
| `DB_SCHEMA_OUTDATED` | 有待执行 Migration | 停止 API 写入，按升级流程执行迁移 |
| `DB_SCHEMA_NEWER_THAN_APPLICATION` | 数据库含应用未知版本 | 部署匹配版本应用，禁止猜测兼容 |
| `DB_COMPATIBILITY_CHECK_FAILED` | 检查过程异常 | 保存日志并保持不就绪 |

## SQL Server 发布门禁

`scripts/run-sqlserver-gate.ps1` 要求本机 Docker 引擎可用，并由 Testcontainers 启动真实 SQL Server 2022。脚本会显式设置 `NEWMES_RUN_SQLSERVER_TESTS=true`；普通 `dotnet test` 中这些用例显示为跳过，不能作为 SQL Server 发布证据。

门禁覆盖：

- 空库安装到当前版本；
- 上一版本带代表性物料和工单升级，旧数据保留且制造事件仍为空；
- 唯一键、外键、状态与正数量约束；
- 事务失败回滚；
- Migration 重复执行无副作用；
- 旧版本拒绝就绪、当前版本进入就绪；
- 显式演示初始化幂等且不补造制造事件。
