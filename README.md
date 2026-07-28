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

## 票 01 范围

- [x] API + Vue 壳可启动，`/health`
- [x] 三角色登录与 JWT；计划区 RBAC（操作工访问 `/api/plan/ping` → 403）
- [x] BCrypt 密码哈希；未授权 → 401
- [x] Compose 文件与 Dockerfile
- [x] 登录写入业务审计，`GET /api/audit`
