using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssemblyBindingConsumption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_MaterialTransactions_DeltaShape",
                schema: "mes",
                table: "MaterialTransactions");

            migrationBuilder.AddColumn<string>(
                name: "OperationCode",
                schema: "mes",
                table: "MaterialTransactions",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProductIdentityId",
                schema: "mes",
                table: "MaterialTransactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceabilityMode",
                schema: "mes",
                table: "MaterialTransactions",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssemblyCommandReceipts",
                schema: "mes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceSystem = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CommandType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CommandHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CommandHashAlgorithm = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ProductIdentityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResultJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssemblyCommandReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssemblyCommandReceipts_ProductIdentities_ProductIdentityId",
                        column: x => x.ProductIdentityId,
                        principalSchema: "mes",
                        principalTable: "ProductIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssemblyComponentBindings",
                schema: "mes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductIdentityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExecutionSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComponentSerialNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LotNumber = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    OperationCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ConsumptionTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BindingEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    BoundAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BoundByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnboundAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UnboundByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorrectionReason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssemblyComponentBindings", x => x.Id);
                    table.CheckConstraint("CK_AssemblyComponentBindings_State", "([IsActive] = 1 AND [UnboundAtUtc] IS NULL AND [UnboundByUserId] IS NULL AND [CorrectionReason] IS NULL) OR ([IsActive] = 0 AND [UnboundAtUtc] IS NOT NULL AND [UnboundByUserId] IS NOT NULL AND LEN([CorrectionReason]) > 0)");
                    table.ForeignKey(
                        name: "FK_AssemblyComponentBindings_ManufacturingEvents_BindingEventId",
                        column: x => x.BindingEventId,
                        principalSchema: "mes",
                        principalTable: "ManufacturingEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssemblyComponentBindings_MaterialTransactions_ConsumptionTransactionId",
                        column: x => x.ConsumptionTransactionId,
                        principalSchema: "mes",
                        principalTable: "MaterialTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssemblyComponentBindings_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalSchema: "mes",
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssemblyComponentBindings_ProductIdentities_ProductIdentityId",
                        column: x => x.ProductIdentityId,
                        principalSchema: "mes",
                        principalTable: "ProductIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssemblyComponentBindings_ProductionOrderExecutionSnapshots_ExecutionSnapshotId",
                        column: x => x.ExecutionSnapshotId,
                        principalSchema: "mes",
                        principalTable: "ProductionOrderExecutionSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssemblyComponentBindings_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalSchema: "mes",
                        principalTable: "ProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssemblyComponentBindings_UserAccounts_BoundByUserId",
                        column: x => x.BoundByUserId,
                        principalSchema: "security",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AssemblyComponentBindings_UserAccounts_UnboundByUserId",
                        column: x => x.UnboundByUserId,
                        principalSchema: "security",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialTransactions_ProductIdentityId_MaterialId_OperationCode_RecordedAtUtc",
                schema: "mes",
                table: "MaterialTransactions",
                columns: ["ProductIdentityId", "MaterialId", "OperationCode", "RecordedAtUtc"]);

            migrationBuilder.AddCheckConstraint(
                name: "CK_MaterialTransactions_DeltaShape",
                schema: "mes",
                table: "MaterialTransactions",
                sql: "([TransactionType] = 'LineSideTransfer' AND [ProductionOrderId] IS NULL AND [ReversesTransactionId] IS NULL AND [LineSideQuantityDelta] = [Quantity] AND [OrderAvailableQuantityDelta] = 0 AND [OrderIssuedQuantityDelta] = 0) OR ([TransactionType] = 'OrderIssue' AND [ProductionOrderId] IS NOT NULL AND [ReversesTransactionId] IS NULL AND [LineSideQuantityDelta] = -[Quantity] AND [OrderAvailableQuantityDelta] = [Quantity] AND [OrderIssuedQuantityDelta] = [Quantity]) OR ([TransactionType] = 'OrderReturn' AND [ProductionOrderId] IS NOT NULL AND [ReversesTransactionId] IS NULL AND [LineSideQuantityDelta] = [Quantity] AND [OrderAvailableQuantityDelta] = -[Quantity] AND [OrderIssuedQuantityDelta] = -[Quantity]) OR ([TransactionType] = 'Consumption' AND [ProductionOrderId] IS NOT NULL AND [ProductIdentityId] IS NOT NULL AND [OperationCode] IS NOT NULL AND [TraceabilityMode] IS NOT NULL AND [ReversesTransactionId] IS NULL AND [LineSideQuantityDelta] = 0 AND [OrderAvailableQuantityDelta] = -[Quantity] AND [OrderIssuedQuantityDelta] = 0) OR ([TransactionType] = 'Reversal' AND [ReversesTransactionId] IS NOT NULL) OR ([TransactionType] = 'Adjustment' AND [ProductionOrderId] IS NULL AND [ReversesTransactionId] IS NULL AND ABS([LineSideQuantityDelta]) = [Quantity] AND [OrderAvailableQuantityDelta] = 0 AND [OrderIssuedQuantityDelta] = 0)");

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyCommandReceipts_ProductIdentityId_CompletedAtUtc",
                schema: "mes",
                table: "AssemblyCommandReceipts",
                columns: ["ProductIdentityId", "CompletedAtUtc"]);

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyCommandReceipts_SourceSystem_IdempotencyKey",
                schema: "mes",
                table: "AssemblyCommandReceipts",
                columns: ["SourceSystem", "IdempotencyKey"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyComponentBindings_BindingEventId",
                schema: "mes",
                table: "AssemblyComponentBindings",
                column: "BindingEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyComponentBindings_BoundByUserId",
                schema: "mes",
                table: "AssemblyComponentBindings",
                column: "BoundByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyComponentBindings_ComponentSerialNumber",
                schema: "mes",
                table: "AssemblyComponentBindings",
                column: "ComponentSerialNumber",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyComponentBindings_ConsumptionTransactionId",
                schema: "mes",
                table: "AssemblyComponentBindings",
                column: "ConsumptionTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyComponentBindings_ExecutionSnapshotId",
                schema: "mes",
                table: "AssemblyComponentBindings",
                column: "ExecutionSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyComponentBindings_MaterialId",
                schema: "mes",
                table: "AssemblyComponentBindings",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyComponentBindings_ProductIdentityId_MaterialId_OperationCode_IsActive",
                schema: "mes",
                table: "AssemblyComponentBindings",
                columns: ["ProductIdentityId", "MaterialId", "OperationCode", "IsActive"]);

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyComponentBindings_ProductionOrderId",
                schema: "mes",
                table: "AssemblyComponentBindings",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_AssemblyComponentBindings_UnboundByUserId",
                schema: "mes",
                table: "AssemblyComponentBindings",
                column: "UnboundByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_MaterialTransactions_ProductIdentities_ProductIdentityId",
                schema: "mes",
                table: "MaterialTransactions",
                column: "ProductIdentityId",
                principalSchema: "mes",
                principalTable: "ProductIdentities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE OR ALTER TRIGGER [mes].[TR_MaterialTransactions_ValidateInsert]
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
                              OR original.[TransactionType] = 'Reversal'
                              OR current_row.[MaterialId] <> original.[MaterialId]
                              OR current_row.[LotNumber] <> original.[LotNumber]
                              OR current_row.[Unit] <> original.[Unit]
                              OR current_row.[Quantity] <> original.[Quantity]
                              OR ISNULL(current_row.[ProductionOrderId], '00000000-0000-0000-0000-000000000000')
                                 <> ISNULL(original.[ProductionOrderId], '00000000-0000-0000-0000-000000000000')
                              OR ISNULL(current_row.[ProductIdentityId], '00000000-0000-0000-0000-000000000000')
                                 <> ISNULL(original.[ProductIdentityId], '00000000-0000-0000-0000-000000000000')
                              OR ISNULL(current_row.[OperationCode], '') <> ISNULL(original.[OperationCode], '')
                              OR ISNULL(current_row.[TraceabilityMode], '') <> ISNULL(original.[TraceabilityMode], '')
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

            migrationBuilder.Sql("""
                CREATE TRIGGER [mes].[TR_AssemblyCommandReceipts_AppendOnly]
                ON [mes].[AssemblyCommandReceipts]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51014, 'Assembly command receipts are append-only.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR ALTER TRIGGER [mes].[TR_MaterialTransactions_ValidateInsert]
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

            migrationBuilder.DropForeignKey(
                name: "FK_MaterialTransactions_ProductIdentities_ProductIdentityId",
                schema: "mes",
                table: "MaterialTransactions");

            migrationBuilder.DropTable(
                name: "AssemblyCommandReceipts",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "AssemblyComponentBindings",
                schema: "mes");

            migrationBuilder.DropIndex(
                name: "IX_MaterialTransactions_ProductIdentityId_MaterialId_OperationCode_RecordedAtUtc",
                schema: "mes",
                table: "MaterialTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_MaterialTransactions_DeltaShape",
                schema: "mes",
                table: "MaterialTransactions");

            migrationBuilder.DropColumn(
                name: "OperationCode",
                schema: "mes",
                table: "MaterialTransactions");

            migrationBuilder.DropColumn(
                name: "ProductIdentityId",
                schema: "mes",
                table: "MaterialTransactions");

            migrationBuilder.DropColumn(
                name: "TraceabilityMode",
                schema: "mes",
                table: "MaterialTransactions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_MaterialTransactions_DeltaShape",
                schema: "mes",
                table: "MaterialTransactions",
                sql: "([TransactionType] = 'LineSideTransfer' AND [ProductionOrderId] IS NULL AND [ReversesTransactionId] IS NULL AND [LineSideQuantityDelta] = [Quantity] AND [OrderAvailableQuantityDelta] = 0 AND [OrderIssuedQuantityDelta] = 0) OR ([TransactionType] = 'OrderIssue' AND [ProductionOrderId] IS NOT NULL AND [ReversesTransactionId] IS NULL AND [LineSideQuantityDelta] = -[Quantity] AND [OrderAvailableQuantityDelta] = [Quantity] AND [OrderIssuedQuantityDelta] = [Quantity]) OR ([TransactionType] = 'OrderReturn' AND [ProductionOrderId] IS NOT NULL AND [ReversesTransactionId] IS NULL AND [LineSideQuantityDelta] = [Quantity] AND [OrderAvailableQuantityDelta] = -[Quantity] AND [OrderIssuedQuantityDelta] = -[Quantity]) OR ([TransactionType] = 'Consumption' AND [ProductionOrderId] IS NOT NULL AND [ReversesTransactionId] IS NULL AND [LineSideQuantityDelta] = 0 AND [OrderAvailableQuantityDelta] = -[Quantity] AND [OrderIssuedQuantityDelta] = 0) OR ([TransactionType] = 'Reversal' AND [ReversesTransactionId] IS NOT NULL) OR ([TransactionType] = 'Adjustment' AND [ProductionOrderId] IS NULL AND [ReversesTransactionId] IS NULL AND ABS([LineSideQuantityDelta]) = [Quantity] AND [OrderAvailableQuantityDelta] = 0 AND [OrderIssuedQuantityDelta] = 0)");
        }
    }
}
