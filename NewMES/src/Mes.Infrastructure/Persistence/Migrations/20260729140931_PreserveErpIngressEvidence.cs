using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Mes.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MesDbContext))]
[Migration(MesMigrationIds.PreserveErpIngressEvidence)]
/// <summary>补齐消息类型与原始载荷，使 ERP 入站结果之外仍保留实际接收证据。</summary>
public sealed class PreserveErpIngressEvidence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "MessageType",
            schema: "integration",
            table: "IntegrationInboxMessages",
            type: "nvarchar(80)",
            maxLength: 80,
            nullable: true);
        // 历史行都来自当时唯一支持的订单 Upsert，使用该事实回填后才能安全改为非空。
        migrationBuilder.Sql("""
            UPDATE [integration].[IntegrationInboxMessages]
            SET [MessageType] = N'ProductionOrderUpsert'
            WHERE [MessageType] IS NULL;
            """);
        migrationBuilder.AlterColumn<string>(
            name: "MessageType",
            schema: "integration",
            table: "IntegrationInboxMessages",
            type: "nvarchar(80)",
            maxLength: 80,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(80)",
            oldMaxLength: 80,
            oldNullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PayloadJson",
            schema: "integration",
            table: "IntegrationInboxMessages",
            type: "nvarchar(max)",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "MessageType",
            schema: "integration",
            table: "IntegrationInboxMessages");
        migrationBuilder.DropColumn(
            name: "PayloadJson",
            schema: "integration",
            table: "IntegrationInboxMessages");
    }
}
