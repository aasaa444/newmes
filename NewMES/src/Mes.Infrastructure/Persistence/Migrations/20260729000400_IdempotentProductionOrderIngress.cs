using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Mes.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MesDbContext))]
[Migration(MesMigrationIds.IdempotentProductionOrderIngress)]
/// <summary>建立 ERP Inbox、载荷摘要和来源业务键，使订单入站支持幂等重放与内容冲突检测。</summary>
public sealed class IdempotentProductionOrderIngress : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "integration");
        migrationBuilder.DropCheckConstraint(
            name: "CK_ProductionOrders_Status",
            schema: "mes",
            table: "ProductionOrders");
        // 旧版 Created 与新入站语义中的 Received 等价，先回填再收紧状态约束可保证无损升级。
        migrationBuilder.Sql("""
            UPDATE [mes].[ProductionOrders]
            SET [Status] = N'Received'
            WHERE [Status] = N'Created';
            """);
        migrationBuilder.AddColumn<string>(
            name: "SourceVersion",
            schema: "mes",
            table: "ProductionOrders",
            type: "nvarchar(80)",
            maxLength: 80,
            nullable: true);
        migrationBuilder.AddCheckConstraint(
            name: "CK_ProductionOrders_Status",
            schema: "mes",
            table: "ProductionOrders",
            sql: "[Status] IN ('Received', 'Released', 'Closed', 'Cancelled')");
        migrationBuilder.CreateIndex(
            name: "IX_ProductionOrders_SourceSystem_SourceReference",
            schema: "mes",
            table: "ProductionOrders",
            columns: ["SourceSystem", "SourceReference"],
            unique: true,
            filter: "[SourceSystem] IS NOT NULL AND [SourceReference] IS NOT NULL");

        migrationBuilder.CreateTable(
            name: "IntegrationInboxMessages",
            schema: "integration",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SourceSystem = table.Column<string>(
                    type: "nvarchar(80)",
                    maxLength: 80,
                    nullable: false),
                MessageId = table.Column<string>(
                    type: "nvarchar(120)",
                    maxLength: 120,
                    nullable: false),
                BusinessKey = table.Column<string>(
                    type: "nvarchar(160)",
                    maxLength: 160,
                    nullable: false),
                SourceVersion = table.Column<string>(
                    type: "nvarchar(80)",
                    maxLength: 80,
                    nullable: false),
                ContractVersion = table.Column<string>(
                    type: "nvarchar(32)",
                    maxLength: 32,
                    nullable: false),
                PayloadHash = table.Column<string>(
                    type: "nvarchar(64)",
                    maxLength: 64,
                    nullable: false),
                PayloadHashAlgorithm = table.Column<string>(
                    type: "nvarchar(16)",
                    maxLength: 16,
                    nullable: false),
                Status = table.Column<string>(
                    type: "nvarchar(16)",
                    maxLength: 16,
                    nullable: false),
                ResultCode = table.Column<string>(
                    type: "nvarchar(80)",
                    maxLength: 80,
                    nullable: false),
                ResultMessage = table.Column<string>(
                    type: "nvarchar(400)",
                    maxLength: 400,
                    nullable: false),
                HttpStatusCode = table.Column<int>(type: "int", nullable: false),
                ReceivedAtUtc = table.Column<DateTimeOffset>(
                    type: "datetimeoffset",
                    nullable: false),
                ProcessedAtUtc = table.Column<DateTimeOffset>(
                    type: "datetimeoffset",
                    nullable: false),
                ProductionOrderId = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IntegrationInboxMessages", value => value.Id);
                table.CheckConstraint(
                    "CK_IntegrationInboxMessages_HttpStatusCode",
                    "[HttpStatusCode] BETWEEN 100 AND 599");
                table.CheckConstraint(
                    "CK_IntegrationInboxMessages_Status",
                    "[Status] IN ('Accepted', 'Rejected')");
                table.ForeignKey(
                    name: "FK_IntegrationInboxMessages_ProductionOrders_ProductionOrderId",
                    column: value => value.ProductionOrderId,
                    principalSchema: "mes",
                    principalTable: "ProductionOrders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(
            name: "IX_IntegrationInboxMessages_ProductionOrderId",
            schema: "integration",
            table: "IntegrationInboxMessages",
            column: "ProductionOrderId");
        migrationBuilder.CreateIndex(
            name: "IX_IntegrationInboxMessages_SourceSystem_BusinessKey_SourceVersion",
            schema: "integration",
            table: "IntegrationInboxMessages",
            columns: ["SourceSystem", "BusinessKey", "SourceVersion"]);
        migrationBuilder.CreateIndex(
            name: "IX_IntegrationInboxMessages_SourceSystem_MessageId",
            schema: "integration",
            table: "IntegrationInboxMessages",
            columns: ["SourceSystem", "MessageId"],
            unique: true);

        migrationBuilder.CreateTable(
            name: "IntegrationInboxConflicts",
            schema: "integration",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                InboxMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ExistingPayloadHash = table.Column<string>(
                    type: "nvarchar(64)",
                    maxLength: 64,
                    nullable: false),
                ObservedPayloadHash = table.Column<string>(
                    type: "nvarchar(64)",
                    maxLength: 64,
                    nullable: false),
                ResultCode = table.Column<string>(
                    type: "nvarchar(80)",
                    maxLength: 80,
                    nullable: false),
                ResultMessage = table.Column<string>(
                    type: "nvarchar(400)",
                    maxLength: 400,
                    nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(
                    type: "datetimeoffset",
                    nullable: false),
                CorrelationId = table.Column<string>(
                    type: "nvarchar(64)",
                    maxLength: 64,
                    nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IntegrationInboxConflicts", value => value.Id);
                table.ForeignKey(
                    name: "FK_IntegrationInboxConflicts_IntegrationInboxMessages_InboxMessageId",
                    column: value => value.InboxMessageId,
                    principalSchema: "integration",
                    principalTable: "IntegrationInboxMessages",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(
            name: "IX_IntegrationInboxConflicts_InboxMessageId_OccurredAtUtc",
            schema: "integration",
            table: "IntegrationInboxConflicts",
            columns: ["InboxMessageId", "OccurredAtUtc"]);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "IntegrationInboxConflicts",
            schema: "integration");
        migrationBuilder.DropTable(
            name: "IntegrationInboxMessages",
            schema: "integration");
        migrationBuilder.DropIndex(
            name: "IX_ProductionOrders_SourceSystem_SourceReference",
            schema: "mes",
            table: "ProductionOrders");
        migrationBuilder.DropCheckConstraint(
            name: "CK_ProductionOrders_Status",
            schema: "mes",
            table: "ProductionOrders");
        migrationBuilder.Sql("""
            UPDATE [mes].[ProductionOrders]
            SET [Status] = N'Created'
            WHERE [Status] = N'Received';
            """);
        migrationBuilder.DropColumn(
            name: "SourceVersion",
            schema: "mes",
            table: "ProductionOrders");
        migrationBuilder.AddCheckConstraint(
            name: "CK_ProductionOrders_Status",
            schema: "mes",
            table: "ProductionOrders",
            sql: "[Status] IN ('Created', 'Released', 'Closed', 'Cancelled')");
    }
}
