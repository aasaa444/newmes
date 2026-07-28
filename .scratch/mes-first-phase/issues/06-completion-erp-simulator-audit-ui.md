# 06 — 完工入库、ERP 回写模拟器与审计查询

**What to build:** 路线完成且合格的 SN 可完工入库：增加成品库存账、退出正常在制；工单在规则满足时至已完工/可关闭。完工与耗料事件触发 ERP 回写模拟器，生成用友/金蝶风格可查询出站报文/日志（不连真 ERP）。计划端可浏览出站消息与业务审计。至此第一期完成定义中的集成与追责面可演示。

**Blocked by:** 05 — 质量判定、隔离、返工与报废

**Status:** resolved

- [x] 合格入库更新成品账与 SN/工单进度
- [x] 隔离/报废品不可混入可售成品账
- [x] 完工/耗料产生可查的模拟回写报文
- [x] 审计查询覆盖关键业务动作
- [x] 工单可关闭且关闭后关键写操作只读

## Answer

CompletionService + FinishedGoodsInventory; ErpWritebackSimulator outbox (MaterialIssue/ProductionReceipt/WorkOrderClose). Isolated/scrapped blocked from FG. Close WO read-only. Plan IntegrationView + station complete button. CompletionSeamTests; suite 38 green.
