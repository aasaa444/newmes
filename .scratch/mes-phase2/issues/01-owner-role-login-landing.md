# 01 — 经营者角色 + 登录默认落地 + 能力声明

**What to build:** 四角色可登录（计划员/操作工/班组长/经营者）；登录与「当前用户」接口声明主角色、默认壳（管理端/过站端）、默认落地页、是否可查看经营总览等能力。经营者对工单下达/领料/主数据写/过站写被服务端拒绝；种子含经营者演示账号。一期三角色行为不回归。

**Blocked by:** None — can start immediately.

**Status:** resolved

- [x] 角色集合含经营者（Owner）；种子账号可登录
- [x] me/登录响应含默认壳、默认路由提示与能力（含可看总览）
- [x] 经营者写执行/主数据/过站类 API 返回 403 或等价拒绝（中文错误）
- [x] 计划员/操作工/班组长原有可写/可读边界不回退
- [x] 鉴权与角色相关自动化测试通过

## Answer

Owner role + CanViewOpsOverview; RoleAccess landing profile; login/me payloads; StationRoles excludes Owner; IdentitySeed ensures owner; Phase2OwnerRoleTests; frontend resolveHomePath.
