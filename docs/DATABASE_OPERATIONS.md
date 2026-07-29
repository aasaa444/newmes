# MES 数据库安装、升级与回退

## 原则

- EF Core Migration 是唯一数据库结构演进机制。
- Web API 普通启动只验证数据库可连接且没有待应用 Migration，不创建、升级、删除或重建数据库。
- 演示账号、主数据和线边库存只通过显式非生产命令装载；Production 环境拒绝执行。
- 任何升级必须先备份。Migration 失败时停止发布，保留原数据库用于调查或恢复。

## 全新安装

```powershell
dotnet run --project src/Mes.Api -- database migrate
```

命令成功后可用 `database status` 验证版本。非生产演示环境再执行：

```powershell
dotnet run --project src/Mes.Api -- database seed-demo
```

两条命令都成功后才启动 API。重复执行 `database migrate` 是安全的；已应用 Migration 不会重复执行。

## 从旧版 EnsureCreated 数据库升级

旧版本没有 Migration 历史。`database migrate` 只在数据库的表、列、存储类型、空值约束、标识列、主键/唯一约束、索引和外键与已知 Legacy 基线完全一致时登记初始基线。登记过程不修改业务数据。

如果结构不一致，命令会在写入 Migration 历史前失败。不要手工伪造 Migration 历史，也不要删除数据库后重新开始；应保留错误输出和备份，完成差异分析后制定单独的数据升级方案。

## 升级步骤

1. 停止 API 和所有会写数据库的后台任务。
2. 确认当前应用版本、数据库名称和计划应用的 Migration。
3. 创建可恢复的完整数据库备份，并把备份保存在数据库实例之外。
4. 在隔离环境恢复该备份，先执行一次 `database migrate` 和业务冒烟。
5. 对目标数据库执行 `database migrate`。
6. 执行 `database status`，再启动 API 并检查 `/health` 和关键业务查询。
7. 保留 Migration 输出、开始/结束时间、执行人和备份标识作为升级证据。

## 失败回退

- Migration 命令返回非零时，不启动新版本 API，不执行 `database seed-demo`，也不尝试自动删库重建。
- 保存完整错误、数据库备份和当前数据库状态，确认失败 Migration 是否已由 SQL Server 事务回滚。
- 若数据库状态不能确认，恢复升级前备份到新的隔离数据库并验证，再按变更流程决定恢复目标数据库或修复后重试。
- 回退应用版本前确认它与恢复后的数据库版本兼容。不要只回退应用二进制而保留一个旧版本无法识别的数据库。

## 真实 SQL Server 测试

数据库接缝测试在 Windows 默认使用 LocalDB。CI 或容器环境通过 `MES_SQLSERVER_TEST_CONNECTION` 指向允许创建临时数据库的 SQL Server；测试会使用唯一数据库名并在结束时清理。

```powershell
dotnet test src/Mes.Api.Tests --filter "Database=SqlServer"
```

SQLite 测试仍用于快速 API 回归，但不计为 SQL Server 安装或升级证据。
