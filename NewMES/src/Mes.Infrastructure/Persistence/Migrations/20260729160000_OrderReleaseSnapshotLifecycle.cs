using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(MesDbContext))]
    [Migration(MesMigrationIds.OrderReleaseSnapshotLifecycle)]
    public partial class OrderReleaseSnapshotLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductionOrders_Status",
                schema: "mes",
                table: "ProductionOrders");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedAtUtc",
                schema: "mes",
                table: "ProductionOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ErpReconciled",
                schema: "mes",
                table: "ProductionOrders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExecutionCompletedAtUtc",
                schema: "mes",
                table: "ProductionOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OpenQualityHoldQuantity",
                schema: "mes",
                table: "ProductionOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "QualifiedQuantity",
                schema: "mes",
                table: "ProductionOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReleasedAtUtc",
                schema: "mes",
                table: "ProductionOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScrappedQuantity",
                schema: "mes",
                table: "ProductionOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StartedQuantity",
                schema: "mes",
                table: "ProductionOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "WarehouseHandoffCompleted",
                schema: "mes",
                table: "ProductionOrders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ProductExecutionTemplateVersions",
                schema: "mes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Applicability = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    DefinitionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DefinitionHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DefinitionHashAlgorithm = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductExecutionTemplateVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductExecutionTemplateVersions_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalSchema: "mes",
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductExecutionTemplateVersions_UserAccounts_PublishedByUserId",
                        column: x => x.PublishedByUserId,
                        principalSchema: "security",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionOrderExecutionSnapshots",
                schema: "mes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotVersion = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DefinitionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DefinitionHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DefinitionHashAlgorithm = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionOrderExecutionSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionOrderExecutionSnapshots_ProductExecutionTemplateVersions_SourceTemplateId",
                        column: x => x.SourceTemplateId,
                        principalSchema: "mes",
                        principalTable: "ProductExecutionTemplateVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionOrderExecutionSnapshots_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalSchema: "mes",
                        principalTable: "ProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionOrderExecutionSnapshots_UserAccounts_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "security",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductionOrders_QuantityBalance",
                schema: "mes",
                table: "ProductionOrders",
                sql: "[StartedQuantity] >= 0 AND [QualifiedQuantity] >= 0 AND [ScrappedQuantity] >= 0 AND [OpenQualityHoldQuantity] >= 0 AND [StartedQuantity] <= [PlannedQuantity] AND [QualifiedQuantity] + [ScrappedQuantity] <= [StartedQuantity] AND [OpenQualityHoldQuantity] <= [StartedQuantity]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductionOrders_Status",
                schema: "mes",
                table: "ProductionOrders",
                sql: "[Status] IN ('Received', 'Released', 'InProduction', 'Paused', 'ExecutionCompleted', 'Closed', 'Cancelled')");

            migrationBuilder.CreateIndex(
                name: "IX_ProductExecutionTemplateVersions_MaterialId_PublishedAtUtc",
                schema: "mes",
                table: "ProductExecutionTemplateVersions",
                columns: ["MaterialId", "PublishedAtUtc"]);

            migrationBuilder.CreateIndex(
                name: "IX_ProductExecutionTemplateVersions_MaterialId_Version",
                schema: "mes",
                table: "ProductExecutionTemplateVersions",
                columns: ["MaterialId", "Version"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductExecutionTemplateVersions_PublishedByUserId",
                schema: "mes",
                table: "ProductExecutionTemplateVersions",
                column: "PublishedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderExecutionSnapshots_CreatedByUserId",
                schema: "mes",
                table: "ProductionOrderExecutionSnapshots",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderExecutionSnapshots_ProductionOrderId",
                schema: "mes",
                table: "ProductionOrderExecutionSnapshots",
                column: "ProductionOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderExecutionSnapshots_SourceTemplateId",
                schema: "mes",
                table: "ProductionOrderExecutionSnapshots",
                column: "SourceTemplateId");

            migrationBuilder.Sql("""
                CREATE TRIGGER [mes].[TR_ProductExecutionTemplateVersions_AppendOnly]
                ON [mes].[ProductExecutionTemplateVersions]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51007, 'Execution template versions are append-only; publish a new version instead.', 1;
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER [mes].[TR_ProductionOrderExecutionSnapshots_AppendOnly]
                ON [mes].[ProductionOrderExecutionSnapshots]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51008, 'Production order execution snapshots are append-only.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductionOrderExecutionSnapshots",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "ProductExecutionTemplateVersions",
                schema: "mes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductionOrders_QuantityBalance",
                schema: "mes",
                table: "ProductionOrders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductionOrders_Status",
                schema: "mes",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "ClosedAtUtc",
                schema: "mes",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "ErpReconciled",
                schema: "mes",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "ExecutionCompletedAtUtc",
                schema: "mes",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "OpenQualityHoldQuantity",
                schema: "mes",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "QualifiedQuantity",
                schema: "mes",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "ReleasedAtUtc",
                schema: "mes",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "ScrappedQuantity",
                schema: "mes",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "StartedQuantity",
                schema: "mes",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "WarehouseHandoffCompleted",
                schema: "mes",
                table: "ProductionOrders");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductionOrders_Status",
                schema: "mes",
                table: "ProductionOrders",
                sql: "[Status] IN ('Received', 'Released', 'Closed', 'Cancelled')");
        }
    }
}
