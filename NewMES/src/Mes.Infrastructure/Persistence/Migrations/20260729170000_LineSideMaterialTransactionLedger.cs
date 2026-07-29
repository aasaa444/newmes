using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mes.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MesDbContext))]
[Migration(MesMigrationIds.LineSideMaterialTransactionLedger)]
public partial class LineSideMaterialTransactionLedger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "BaseUnit",
            schema: "mes",
            table: "Materials",
            type: "nvarchar(24)",
            maxLength: 24,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "MaterialTransactions",
            schema: "mes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TransactionType = table.Column<string>(
                    type: "nvarchar(32)",
                    maxLength: 32,
                    nullable: false),
                MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProductionOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                LotNumber = table.Column<string>(
                    type: "nvarchar(120)",
                    maxLength: 120,
                    nullable: false),
                Quantity = table.Column<decimal>(
                    type: "decimal(18,6)",
                    precision: 18,
                    scale: 6,
                    nullable: false),
                Unit = table.Column<string>(
                    type: "nvarchar(24)",
                    maxLength: 24,
                    nullable: false),
                LineSideQuantityDelta = table.Column<decimal>(
                    type: "decimal(18,6)",
                    precision: 18,
                    scale: 6,
                    nullable: false),
                OrderAvailableQuantityDelta = table.Column<decimal>(
                    type: "decimal(18,6)",
                    precision: 18,
                    scale: 6,
                    nullable: false),
                OrderIssuedQuantityDelta = table.Column<decimal>(
                    type: "decimal(18,6)",
                    precision: 18,
                    scale: 6,
                    nullable: false),
                SourceSystem = table.Column<string>(
                    type: "nvarchar(80)",
                    maxLength: 80,
                    nullable: false),
                IdempotencyKey = table.Column<string>(
                    type: "nvarchar(120)",
                    maxLength: 120,
                    nullable: false),
                SourceDocumentType = table.Column<string>(
                    type: "nvarchar(80)",
                    maxLength: 80,
                    nullable: true),
                SourceDocumentNumber = table.Column<string>(
                    type: "nvarchar(160)",
                    maxLength: 160,
                    nullable: true),
                FromParty = table.Column<string>(
                    type: "nvarchar(160)",
                    maxLength: 160,
                    nullable: true),
                ToParty = table.Column<string>(
                    type: "nvarchar(160)",
                    maxLength: 160,
                    nullable: true),
                ReversesTransactionId = table.Column<Guid>(
                    type: "uniqueidentifier",
                    nullable: true),
                Reason = table.Column<string>(
                    type: "nvarchar(400)",
                    maxLength: 400,
                    nullable: true),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(
                    type: "datetimeoffset",
                    nullable: false),
                RecordedAtUtc = table.Column<DateTimeOffset>(
                    type: "datetimeoffset",
                    nullable: false),
                CommandHash = table.Column<string>(
                    type: "nvarchar(64)",
                    maxLength: 64,
                    nullable: false),
                CommandHashAlgorithm = table.Column<string>(
                    type: "nvarchar(24)",
                    maxLength: 24,
                    nullable: false),
                CorrelationId = table.Column<string>(
                    type: "nvarchar(64)",
                    maxLength: 64,
                    nullable: false),
                LineSideBalanceAfter = table.Column<decimal>(
                    type: "decimal(18,6)",
                    precision: 18,
                    scale: 6,
                    nullable: false),
                OrderAvailableBalanceAfter = table.Column<decimal>(
                    type: "decimal(18,6)",
                    precision: 18,
                    scale: 6,
                    nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MaterialTransactions", item => item.Id);
                table.CheckConstraint(
                    "CK_MaterialTransactions_Type",
                    "[TransactionType] IN ('LineSideTransfer', 'OrderIssue', 'OrderReturn', 'Consumption', 'Reversal', 'Adjustment')");
                table.CheckConstraint(
                    "CK_MaterialTransactions_Quantity",
                    "[Quantity] > 0");
                table.CheckConstraint(
                    "CK_MaterialTransactions_Reason",
                    "[TransactionType] NOT IN ('OrderReturn', 'Reversal', 'Adjustment') OR LEN([Reason]) > 0");
                table.CheckConstraint(
                    "CK_MaterialTransactions_DeltaShape",
                    "([TransactionType] = 'LineSideTransfer' AND [ProductionOrderId] IS NULL AND [ReversesTransactionId] IS NULL AND [LineSideQuantityDelta] = [Quantity] AND [OrderAvailableQuantityDelta] = 0 AND [OrderIssuedQuantityDelta] = 0) OR "
                    + "([TransactionType] = 'OrderIssue' AND [ProductionOrderId] IS NOT NULL AND [ReversesTransactionId] IS NULL AND [LineSideQuantityDelta] = -[Quantity] AND [OrderAvailableQuantityDelta] = [Quantity] AND [OrderIssuedQuantityDelta] = [Quantity]) OR "
                    + "([TransactionType] = 'OrderReturn' AND [ProductionOrderId] IS NOT NULL AND [ReversesTransactionId] IS NULL AND [LineSideQuantityDelta] = [Quantity] AND [OrderAvailableQuantityDelta] = -[Quantity] AND [OrderIssuedQuantityDelta] = -[Quantity]) OR "
                    + "([TransactionType] = 'Consumption' AND [ProductionOrderId] IS NOT NULL AND [ReversesTransactionId] IS NULL AND [LineSideQuantityDelta] = 0 AND [OrderAvailableQuantityDelta] = -[Quantity] AND [OrderIssuedQuantityDelta] = 0) OR "
                    + "([TransactionType] = 'Reversal' AND [ReversesTransactionId] IS NOT NULL) OR "
                    + "([TransactionType] = 'Adjustment' AND [ProductionOrderId] IS NULL AND [ReversesTransactionId] IS NULL AND ABS([LineSideQuantityDelta]) = [Quantity] AND [OrderAvailableQuantityDelta] = 0 AND [OrderIssuedQuantityDelta] = 0)");
                table.ForeignKey(
                    name: "FK_MaterialTransactions_Materials_MaterialId",
                    column: item => item.MaterialId,
                    principalSchema: "mes",
                    principalTable: "Materials",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_MaterialTransactions_ProductionOrders_ProductionOrderId",
                    column: item => item.ProductionOrderId,
                    principalSchema: "mes",
                    principalTable: "ProductionOrders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_MaterialTransactions_MaterialTransactions_ReversesTransactionId",
                    column: item => item.ReversesTransactionId,
                    principalSchema: "mes",
                    principalTable: "MaterialTransactions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_MaterialTransactions_UserAccounts_ActorUserId",
                    column: item => item.ActorUserId,
                    principalSchema: "security",
                    principalTable: "UserAccounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MaterialTransactions_ActorUserId",
            schema: "mes",
            table: "MaterialTransactions",
            column: "ActorUserId");
        migrationBuilder.CreateIndex(
            name: "IX_MaterialTransactions_MaterialId_LotNumber_RecordedAtUtc",
            schema: "mes",
            table: "MaterialTransactions",
            columns: ["MaterialId", "LotNumber", "RecordedAtUtc"]);
        migrationBuilder.CreateIndex(
            name: "IX_MaterialTransactions_ProductionOrderId_MaterialId_LotNumber_RecordedAtUtc",
            schema: "mes",
            table: "MaterialTransactions",
            columns: ["ProductionOrderId", "MaterialId", "LotNumber", "RecordedAtUtc"]);
        migrationBuilder.CreateIndex(
            name: "IX_MaterialTransactions_ReversesTransactionId",
            schema: "mes",
            table: "MaterialTransactions",
            column: "ReversesTransactionId",
            unique: true,
            filter: "[ReversesTransactionId] IS NOT NULL");
        migrationBuilder.CreateIndex(
            name: "IX_MaterialTransactions_SourceSystem_IdempotencyKey",
            schema: "mes",
            table: "MaterialTransactions",
            columns: ["SourceSystem", "IdempotencyKey"],
            unique: true);

        migrationBuilder.Sql("""
            CREATE TRIGGER [mes].[TR_MaterialTransactions_AppendOnly]
            ON [mes].[MaterialTransactions]
            INSTEAD OF UPDATE, DELETE
            AS
            BEGIN
                SET NOCOUNT ON;
                THROW 51009, 'Material transactions are append-only; use a return, reversal, or adjustment.', 1;
            END;
            """);
        migrationBuilder.Sql("""
            CREATE TRIGGER [mes].[TR_MaterialTransactions_ValidateInsert]
            ON [mes].[MaterialTransactions]
            AFTER INSERT
            AS
            BEGIN
                SET NOCOUNT ON;

                IF EXISTS
                (
                    SELECT 1
                    FROM inserted AS current_row
                    LEFT JOIN [mes].[MaterialTransactions] AS original
                        ON original.[Id] = current_row.[ReversesTransactionId]
                    WHERE current_row.[TransactionType] = 'Reversal'
                      AND
                      (
                          original.[Id] IS NULL
                          OR original.[TransactionType] IN ('Reversal', 'Consumption')
                          OR current_row.[MaterialId] <> original.[MaterialId]
                          OR current_row.[LotNumber] <> original.[LotNumber]
                          OR current_row.[Unit] <> original.[Unit]
                          OR current_row.[Quantity] <> original.[Quantity]
                          OR ISNULL(current_row.[ProductionOrderId], '00000000-0000-0000-0000-000000000000')
                             <> ISNULL(original.[ProductionOrderId], '00000000-0000-0000-0000-000000000000')
                          OR current_row.[LineSideQuantityDelta] <> -original.[LineSideQuantityDelta]
                          OR current_row.[OrderAvailableQuantityDelta] <> -original.[OrderAvailableQuantityDelta]
                          OR current_row.[OrderIssuedQuantityDelta] <> -original.[OrderIssuedQuantityDelta]
                      )
                )
                BEGIN
                    THROW 51010, 'A reversal must exactly compensate one eligible original material transaction.', 1;
                END;

                IF EXISTS
                (
                    SELECT 1
                    FROM
                    (
                        SELECT DISTINCT [MaterialId], [LotNumber]
                        FROM inserted
                    ) AS affected
                    CROSS APPLY
                    (
                        SELECT SUM([LineSideQuantityDelta]) AS [Balance]
                        FROM [mes].[MaterialTransactions]
                        WHERE [MaterialId] = affected.[MaterialId]
                          AND [LotNumber] = affected.[LotNumber]
                    ) AS current_balance
                    WHERE current_balance.[Balance] < 0
                )
                BEGIN
                    THROW 51011, 'A material transaction cannot make line-side inventory negative.', 1;
                END;

                IF EXISTS
                (
                    SELECT 1
                    FROM
                    (
                        SELECT DISTINCT [ProductionOrderId], [MaterialId], [LotNumber]
                        FROM inserted
                        WHERE [ProductionOrderId] IS NOT NULL
                    ) AS affected
                    CROSS APPLY
                    (
                        SELECT SUM([OrderAvailableQuantityDelta]) AS [Balance]
                        FROM [mes].[MaterialTransactions]
                        WHERE [ProductionOrderId] = affected.[ProductionOrderId]
                          AND [MaterialId] = affected.[MaterialId]
                          AND [LotNumber] = affected.[LotNumber]
                    ) AS current_balance
                    WHERE current_balance.[Balance] < 0
                )
                BEGIN
                    THROW 51012, 'A material transaction cannot make order-available inventory negative.', 1;
                END;
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "MaterialTransactions",
            schema: "mes");
        migrationBuilder.DropColumn(
            name: "BaseUnit",
            schema: "mes",
            table: "Materials");
    }
}
