# 03 — 生产工单生命周期与领料

**What to build:** 计划员可创建草稿生产工单、下达（冻结路线版本）、在无在制 SN 时取消；已下达后可按 BOM 做齐套提示（默认欠料警告不强制）并领料：扣减线边库存账、形成工单用料（非关键件默认已耗，关键件按待耗/已领规则）。工单状态机按共识运转；班组长只读进度可开始具备基础列表。

**Blocked by:** 02 — 执行主数据与种子数据

**Status:** resolved

- [x] 状态：草稿→已下达→（后续票推进）生产中/已完工/已关闭；取消仅无在制 SN
- [x] 下达后路线版本固定；可查询应领与齐套结果
- [x] 领料减少线边账并留下工单用料事件
- [x] 线边收料（或种子预置库存）足以支持演示领料
- [x] 下达/取消/领料可审计

## Answer

WorkOrder + LineSideInventory + IssueLines; WorkOrderService (release freezes BOM/route, kitting soft shortage, issue deducts line-side; key→Pending, non-key→Consumed). InventorySeed for PCB/PSU/screws. APIs under /api/work-orders* and /api/inventory/line-side*. Plan UI WorkOrdersView. Tests WorkOrderSeamTests 8 + full suite.
