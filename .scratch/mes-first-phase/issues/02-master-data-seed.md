# 02 — 执行主数据与种子数据

**What to build:** 计划员可在 MES 维护物料（含执行期属性）、单层 BOM、线性工艺路线与工序、产线与工位（工位绑定单工序）；提供可重复加载的种子数据（电子组装示例：成品+关键件+辅料+默认路线+工位），使后续工单与过站有主数据可依。无 ERP 导入也能闭环。

**Blocked by:** 01 — 工程脚手架、身份与交付骨架

**Status:** resolved

- [x] 计划员可 CRUD 物料/BOM/工艺路线/产线/工位
- [x] 关键件与采集 SN 标记可配置；工位仅绑定一道工序
- [x] 种子数据一键或一命令加载成功
- [x] 非计划员无法改主数据（RBAC）
- [x] 主数据变更写入业务审计

## Answer

Master data domain + `MasterDataSeed` (electronics demo). API under `/api/materials|boms|process-routes|production-lines|work-stations` and `POST /api/master-data/seed`. Planner-only writes; AnyBusinessRole reads. Material PUT/DELETE (soft). Vue plan page lists seed. Tests: MasterDataSeamTests + Auth — 14 green.
