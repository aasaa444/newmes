# 07 — 端到端演示与第一期验收

**What to build:** 用种子数据跑通并固化「黄金路径 + 异常支线」演示脚本/清单（可人工点击或自动化高缝测试）：下达→领料→过站绑关键件→合格入库→谱系；再跑不合格→隔离→放行或返工/报废；三角色切换；ERP 出站与审计可展示。对照 `CONTEXT.md` 第一期完成定义勾选验收，补齐 README 级启动与演示说明（仍不写游戏仓库）。

**Blocked by:** 06 — 完工入库、ERP 回写模拟器与审计查询

**Status:** resolved

- [x] 黄金路径可重复演示（有文档步骤）
- [x] 异常支线可重复演示
- [x] 高缝自动化测试（或等价清单+证据）覆盖主路径与防跳站/取消限制等关键规则
- [x] 第一期完成定义四条全部满足且 Out of Scope 未偷加范围
- [x] 启动/演示说明足够陌生人按文档跑起来

## Answer

docs/DEMO.md, docs/PHASE1_ACCEPTANCE.md, docs/DEMO_API.ps1; Phase1E2eSeamTests (golden + exception + cancel/anti-skip + RBAC + ERP/audit). README entry points. Full suite green.
