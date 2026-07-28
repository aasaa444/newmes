# 技术栈：.NET + Vue 3 + SQL Server

后端采用 ASP.NET Core Web API 承载 MES 领域与 ERP 适配器；前端明确采用 **Vue 3**（计划端与过站台同工程、不同布局）；数据库默认 **SQL Server**（作者长期主库）。第一期单体模块化，可容器化部署。

作者开发生态为 .NET + SQL Server，用主栈做深工单/过站/谱系/隔离。前端你明确选择 Vue、拒绝 Blazor：与国内中小制造常见交付界面及招聘关键词对齐，并接受「在补强 Vue 产品完成度的同时把领域做透」的成本。曾考虑 PostgreSQL、Java、Blazor：前两者非主生态或非你的前端选择；Blazor 虽一人同语言更快，但与你的明确偏好冲突。

**Consequences**: 简历写「.NET MES + Vue 3 + SQL Server + 用友/金蝶风格适配 + SAP 接口经验可迁移」。过站台必须达到产品级（扫码焦点、大字反馈、拦截态），避免只会中后台表格。领域逻辑全部进 API，Vue 不藏业务真相，便于以后换壳或对接其他客户端。
