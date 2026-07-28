# 05 — 质量判定、隔离、返工与报废

**What to build:** 在过站能力上支持不合格路径：返工至指定工序、进入隔离、或报废；隔离品不得合格完工入库；授权角色可放行（原因+操作者进谱系与审计）。班组长可查看隔离/异常列表并做必要放行或确认报废（权限可配置）。历史失败与再过站不可物理删除覆盖。

**Blocked by:** 04 — 过站台、SN、防跳站与谱系（黄金路径核心）

**Status:** resolved

- [x] 不合格可走返工/隔离/报废之一（按工序或操作选择）
- [x] 隔离 SN 不能走正常完工入库
- [x] 放行留痕后可回到路线流动
- [x] 报废计入工单报废数并结束该 SN 正常在制
- [x] 质量相关动作可审计且谱系可查

## Answer

QualityService: Fail(Rework|Isolate|Scrap), Release, Scrap, ListIsolated. Isolated blocks station pass/bind. Scrap updates WorkOrder.ScrappedQty and InProcessSerialCount. Genealogy keeps Fail/Rework/Release/Scrap history. APIs + station UI. QualitySeamTests 5 green.
