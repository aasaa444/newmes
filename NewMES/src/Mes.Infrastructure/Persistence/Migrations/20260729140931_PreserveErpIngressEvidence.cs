using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Mes.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MesDbContext))]
[Migration(MesMigrationIds.PreserveErpIngressEvidence)]
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
