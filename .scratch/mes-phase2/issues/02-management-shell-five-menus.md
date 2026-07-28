# 02 — 管理端经典壳（侧栏五菜单 + 顶栏）

**What to build:** 管理端呈现企业中后台形态：左侧一级五菜单（经营总览 → 生产执行 → 质量异常 → 物料库存 → 系统）、顶栏（用户/角色、切换过站端）、内容区页标题与面包屑；列表页查询区与主按钮位置统一。菜单按角色显隐；一期散落的管理页迁入壳内路由，不再作为无壳主入口。

**Blocked by:** 01 — 经营者角色 + 登录默认落地 + 能力声明

**Status:** resolved

- [x] 管理端任意获权角色可见统一侧栏与顶栏
- [x] 五席顺序与名称符合二期约定；无权限菜单不展示或不可进
- [x] 顶栏可切换至过站端（仅权限允许的角色）
- [x] 原计划端页面在壳内可到达，无「孤儿散页」作为主路径
- [x] 操作工默认不进入管理端迷宫（与落地约定一致）

## Answer

ManagementLayout (sidebar + topbar + content); nav endpoint /api/nav/management by role; existing plan pages moved under /plan children; deleted PlanHome.vue (replaced by OpsOverviewView). 55/55 tests green.
