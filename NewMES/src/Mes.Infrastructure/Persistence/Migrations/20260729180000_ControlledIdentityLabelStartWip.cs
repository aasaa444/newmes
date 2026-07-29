using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ControlledIdentityLabelStartWip : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CausationEventId",
                schema: "mes",
                table: "ManufacturingEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                schema: "mes",
                table: "ManufacturingEvents",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExecutionSnapshotId",
                schema: "mes",
                table: "ManufacturingEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Location",
                schema: "mes",
                table: "ManufacturingEvents",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProductIdentityId",
                schema: "mes",
                table: "ManufacturingEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProductionOrderId",
                schema: "mes",
                table: "ManufacturingEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RecordedAtUtc",
                schema: "mes",
                table: "ManufacturingEvents",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.Sql("""
                DISABLE TRIGGER [mes].[TR_ManufacturingEvents_AppendOnly]
                ON [mes].[ManufacturingEvents];

                UPDATE [mes].[ManufacturingEvents]
                SET [RecordedAtUtc] = [OccurredAtUtc]
                WHERE [RecordedAtUtc] IS NULL;

                ENABLE TRIGGER [mes].[TR_ManufacturingEvents_AppendOnly]
                ON [mes].[ManufacturingEvents];
                """);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "RecordedAtUtc",
                schema: "mes",
                table: "ManufacturingEvents",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "ProductIdentities",
                schema: "mes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SerialSourceType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SerialSourceSystem = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    SerialSourceReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsDemo = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExecutionSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NextOperationCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    AllocatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BoundAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    StartSourceSystem = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    StartIdempotencyKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    StartCommandHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AllocationSourceSystem = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    AllocationIdempotencyKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    AllocationCommandHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductIdentities", x => x.Id);
                    table.CheckConstraint("CK_ProductIdentities_BindingShape", "([Status] = 'Bound' AND [ProductionOrderId] IS NOT NULL AND [ExecutionSnapshotId] IS NOT NULL AND [BoundAtUtc] IS NOT NULL AND [StartSourceSystem] IS NOT NULL AND [StartIdempotencyKey] IS NOT NULL AND [StartCommandHash] IS NOT NULL) OR ([Status] IN ('Allocated', 'Voided') AND [ProductionOrderId] IS NULL AND [ExecutionSnapshotId] IS NULL AND [BoundAtUtc] IS NULL AND [StartSourceSystem] IS NULL AND [StartIdempotencyKey] IS NULL AND [StartCommandHash] IS NULL)");
                    table.CheckConstraint("CK_ProductIdentities_Status", "[Status] IN ('Allocated', 'Bound', 'Voided')");
                    table.ForeignKey(
                        name: "FK_ProductIdentities_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalSchema: "mes",
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductIdentities_ProductionOrderExecutionSnapshots_ExecutionSnapshotId",
                        column: x => x.ExecutionSnapshotId,
                        principalSchema: "mes",
                        principalTable: "ProductionOrderExecutionSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductIdentities_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalSchema: "mes",
                        principalTable: "ProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ControlledIdentifiers",
                schema: "mes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductIdentityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceSystem = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    SourceReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsDemo = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ControlledIdentifiers", x => x.Id);
                    table.CheckConstraint("CK_ControlledIdentifiers_DemoFlag", "([SourceType] = 'DemoControlledPool' AND [IsDemo] = 1) OR ([SourceType] <> 'DemoControlledPool' AND [IsDemo] = 0)");
                    table.CheckConstraint("CK_ControlledIdentifiers_SourceType", "[SourceType] IN ('Erp', 'LabelSystem', 'MesControlledPool', 'DemoControlledPool')");
                    table.CheckConstraint("CK_ControlledIdentifiers_Type", "[Type] IN ('SerialNumber', 'MacAddress', 'Imei', 'Certificate')");
                    table.ForeignKey(
                        name: "FK_ControlledIdentifiers_ProductIdentities_ProductIdentityId",
                        column: x => x.ProductIdentityId,
                        principalSchema: "mes",
                        principalTable: "ProductIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductLabels",
                schema: "mes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductIdentityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersion = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Printer = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ReplacesLabelId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductLabels", x => x.Id);
                    table.CheckConstraint("CK_ProductLabels_Status", "[Status] IN ('Active', 'Voided', 'Replaced')");
                    table.ForeignKey(
                        name: "FK_ProductLabels_ProductIdentities_ProductIdentityId",
                        column: x => x.ProductIdentityId,
                        principalSchema: "mes",
                        principalTable: "ProductIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductLabels_ProductLabels_ReplacesLabelId",
                        column: x => x.ReplacesLabelId,
                        principalSchema: "mes",
                        principalTable: "ProductLabels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingEvents_CausationEventId",
                schema: "mes",
                table: "ManufacturingEvents",
                column: "CausationEventId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingEvents_ProductIdentityId_RecordedAtUtc",
                schema: "mes",
                table: "ManufacturingEvents",
                columns: ["ProductIdentityId", "RecordedAtUtc"]);

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingEvents_ProductionOrderId_RecordedAtUtc",
                schema: "mes",
                table: "ManufacturingEvents",
                columns: ["ProductionOrderId", "RecordedAtUtc"]);

            migrationBuilder.CreateIndex(
                name: "IX_ControlledIdentifiers_ProductIdentityId_Type",
                schema: "mes",
                table: "ControlledIdentifiers",
                columns: ["ProductIdentityId", "Type"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ControlledIdentifiers_Type_Value",
                schema: "mes",
                table: "ControlledIdentifiers",
                columns: ["Type", "Value"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductIdentities_AllocationSourceSystem_AllocationIdempotencyKey",
                schema: "mes",
                table: "ProductIdentities",
                columns: ["AllocationSourceSystem", "AllocationIdempotencyKey"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductIdentities_ExecutionSnapshotId",
                schema: "mes",
                table: "ProductIdentities",
                column: "ExecutionSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductIdentities_MaterialId",
                schema: "mes",
                table: "ProductIdentities",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductIdentities_ProductionOrderId",
                schema: "mes",
                table: "ProductIdentities",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductIdentities_SerialNumber",
                schema: "mes",
                table: "ProductIdentities",
                column: "SerialNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductIdentities_StartSourceSystem_StartIdempotencyKey",
                schema: "mes",
                table: "ProductIdentities",
                columns: ["StartSourceSystem", "StartIdempotencyKey"],
                unique: true,
                filter: "[StartSourceSystem] IS NOT NULL AND [StartIdempotencyKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductLabels_ProductIdentityId_CreatedAtUtc",
                schema: "mes",
                table: "ProductLabels",
                columns: ["ProductIdentityId", "CreatedAtUtc"]);

            migrationBuilder.CreateIndex(
                name: "IX_ProductLabels_ReplacesLabelId",
                schema: "mes",
                table: "ProductLabels",
                column: "ReplacesLabelId");

            migrationBuilder.AddForeignKey(
                name: "FK_ManufacturingEvents_ManufacturingEvents_CausationEventId",
                schema: "mes",
                table: "ManufacturingEvents",
                column: "CausationEventId",
                principalSchema: "mes",
                principalTable: "ManufacturingEvents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ManufacturingEvents_ManufacturingEvents_CausationEventId",
                schema: "mes",
                table: "ManufacturingEvents");

            migrationBuilder.DropTable(
                name: "ControlledIdentifiers",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "ProductLabels",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "ProductIdentities",
                schema: "mes");

            migrationBuilder.DropIndex(
                name: "IX_ManufacturingEvents_CausationEventId",
                schema: "mes",
                table: "ManufacturingEvents");

            migrationBuilder.DropColumn(
                name: "CausationEventId",
                schema: "mes",
                table: "ManufacturingEvents");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                schema: "mes",
                table: "ManufacturingEvents");

            migrationBuilder.DropColumn(
                name: "ExecutionSnapshotId",
                schema: "mes",
                table: "ManufacturingEvents");

            migrationBuilder.DropColumn(
                name: "Location",
                schema: "mes",
                table: "ManufacturingEvents");

            migrationBuilder.DropColumn(
                name: "ProductIdentityId",
                schema: "mes",
                table: "ManufacturingEvents");

            migrationBuilder.DropColumn(
                name: "ProductionOrderId",
                schema: "mes",
                table: "ManufacturingEvents");

            migrationBuilder.DropColumn(
                name: "RecordedAtUtc",
                schema: "mes",
                table: "ManufacturingEvents");
        }
    }
}
