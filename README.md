# 无名 MES（单厂试点 · 第一期）

离散电子组装车间制造执行系统骨架。领域词汇见 [`CONTEXT.md`](./CONTEXT.md)，决策见 [`docs/adr/`](./docs/adr/)，规格与票见 [`.scratch/mes-first-phase/`](./.scratch/mes-first-phase/)。

## 技术栈

- 后端：ASP.NET Core（.NET 10）+ SQL Server（开发可用 LocalDB）
- 前端：Vue 3 + Vue Router（计划端 / 过站台分布局）
- 测试：xUnit + `WebApplicationFactory`（SQLite 内存库）
- 交付：`docker-compose.yml`（需 Docker 守护进程）

## 快速开始（无 Docker）

### API

```powershell
sqllocaldb start MSSQLLocalDB
dotnet run --project src/Mes.Api
# http://localhost:5101
```

健康检查：`GET /health`

### Web

```powershell
cd web
npm install
npm run dev
```

默认 `VITE_API_BASE=http://localhost:5101`（见 `web/.env.development`）。

### 演示账号

| 用户名 | 密码 | 角色 |
|--------|------|------|
| planner | Planner@123 | 计划员 Planner |
| operator | Operator@123 | 操作工 Operator |
| leader | Leader@123 | 班组长 Leader |

## 测试

```powershell
dotnet test src/Mes.Api.Tests
```

## Docker Compose

Docker Desktop 运行后：

```powershell
docker compose up --build
```

- API：http://localhost:8080  
- Web：http://localhost:8081  
- SQL Server：localhost:1433（sa / `Mes_Dev_Passw0rd!`）

当前环境若 Docker 引擎未启动，请用 LocalDB 开发路径。

## 票进度

### 01 脚手架 / 身份 / 交付
- [x] API + Vue 壳，`/health`，JWT RBAC，审计，Compose

### 02 执行主数据与种子
- [x] 物料（关键件 / 采 SN）、单层 BOM、线性工艺路线、产线、工位（绑单工序）
- [x] 种子：`FG-ROUTER` + PCB/PSU/螺丝 + `RT-ROUTER-A` + `L1` 五工位
- [x] 计划员写、全角色读；变更审计；`POST /api/master-data/seed`
- [x] 计划端「执行主数据」只读浏览页

### 03 生产工单与领料
- [x] 草稿 → 下达（冻结 BOM/路线版本）→ 取消（无在制 SN）
- [x] 齐套查询（欠料软提示）；领料扣线边；关键件待耗 / 非关键件已耗
- [x] 线边库存种子 + 收料 API；计划端「生产工单」页

## 主数据 API（摘要）

| 方法 | 路径 | 授权 |
|------|------|------|
| GET | `/api/materials` `/api/boms` `/api/process-routes` `/api/production-lines` `/api/work-stations` | 任意业务角色 |
| POST/PUT/DELETE | 同上资源（物料完整；BOM/路线/线/工位以 POST 创建为主） | 仅计划员 |
| POST | `/api/master-data/seed` | 仅计划员 |

## 数据库（LocalDB）

票 01 若已建过只有 `Users` 的 `MesDb`，EF `EnsureCreated` **不会**自动加 `Materials` 等新表，会出现 `Invalid object name 'Materials'`。

**现已处理：** 开发环境启动时 `DatabaseBootstrap` 会探测主数据表；缺失则删库重建并重新种子（仅 Development/Testing）。直接再跑：

```powershell
dotnet run --project src/Mes.Api
```

也可手动删库后启动：

```powershell
sqllocaldb stop MSSQLLocalDB
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "DROP DATABASE IF EXISTS MesDb"
sqllocaldb start MSSQLLocalDB
dotnet run --project src/Mes.Api
```

正式试点请改用 EF Migration，避免 `EnsureDeleted`。
