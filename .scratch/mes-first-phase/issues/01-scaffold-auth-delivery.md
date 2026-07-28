# 01 — 工程脚手架、身份与交付骨架

**What to build:** 能本地/容器拉起的空壳系统：ASP.NET Core API + Vue 3 前端壳（计划端/过站台路由分列）+ SQL Server 连接 + 本地登录与三角色 RBAC 骨架 + Docker Compose（或等价）私有化编排说明可运行。尚不必有完整车间业务，但鉴权前后端均生效（禁止仅藏按钮）。

**Blocked by:** None — can start immediately.

**Status:** resolved

- [x] API 与 Vue 应用可启动并健康检查通过
- [x] 计划员/操作工/班组长三类账号可登录，菜单/API 按角色区分
- [x] 密码安全存储；未授权请求被服务端拒绝
- [x] Compose（或文档化的等价部署）可重复拉起 API + Web + DB 连接
- [x] 业务审计表/管道预留，至少登录或一次受控写操作可落审计（可极简）

## Answer

Implemented modular monolith scaffold under `src/Mes.Api` + `web`: JWT login (BCrypt), Planner/Operator/Leader RBAC, `/health`, business audit on login + `/api/audit`, Docker Compose + Dockerfiles, Vue plan/station shells. Tests: 8/8 green at auth API seam (`Mes.Api.Tests`). Dev path uses LocalDB when Docker engine is down.
