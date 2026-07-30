using Mes.DbMigrator;

// 顶层入口只转交并保留 MigratorApplication 定义的稳定退出码，便于部署流水线判断成败。
return await MigratorApplication.RunAsync(args);
