using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mes.Infrastructure.Persistence.Migrations
{
    /// <summary>记录固件/配置实际执行版本、摘要、设备证据、重试关系和成功命令唯一性。</summary>
    public partial class FirmwareConfigurationExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FirmwareConfigurationExecutions",
                schema: "mes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductIdentityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExecutionSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequirementCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    OperationCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    RequiredVersion = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ActualVersion = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    RequiredConfigurationPackage = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ActualConfigurationPackage = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    RequiredChecksumAlgorithm = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ActualChecksumAlgorithm = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ExpectedChecksum = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ActualChecksum = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ToolId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ToolVersion = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Result = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OperationCompleted = table.Column<bool>(type: "bit", nullable: false),
                    NextOperationCodeAfter = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    DiagnosticCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    DiagnosticMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReportedDiagnosticCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ReportedDiagnosticMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RetryOfExecutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ManufacturingEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceSystem = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CommandHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CommandHashAlgorithm = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUsername = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmwareConfigurationExecutions", x => x.Id);
                    table.CheckConstraint("CK_FirmwareConfigurationExecutions_Result", "[Result] IN ('Succeeded', 'Failed')");
                    table.CheckConstraint("CK_FirmwareConfigurationExecutions_Time", "[EndedAtUtc] >= [StartedAtUtc]");
                    table.ForeignKey(
                        name: "FK_FirmwareConfigurationExecutions_FirmwareConfigurationExecutions_RetryOfExecutionId",
                        column: x => x.RetryOfExecutionId,
                        principalSchema: "mes",
                        principalTable: "FirmwareConfigurationExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FirmwareConfigurationExecutions_ManufacturingEvents_ManufacturingEventId",
                        column: x => x.ManufacturingEventId,
                        principalSchema: "mes",
                        principalTable: "ManufacturingEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FirmwareConfigurationExecutions_ProductIdentities_ProductIdentityId",
                        column: x => x.ProductIdentityId,
                        principalSchema: "mes",
                        principalTable: "ProductIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FirmwareConfigurationExecutions_ProductionOrderExecutionSnapshots_ExecutionSnapshotId",
                        column: x => x.ExecutionSnapshotId,
                        principalSchema: "mes",
                        principalTable: "ProductionOrderExecutionSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FirmwareConfigurationExecutions_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalSchema: "mes",
                        principalTable: "ProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FirmwareConfigurationExecutions_UserAccounts_ActorUserId",
                        column: x => x.ActorUserId,
                        principalSchema: "security",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareConfigurationExecutions_ActorUserId",
                schema: "mes",
                table: "FirmwareConfigurationExecutions",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareConfigurationExecutions_ExecutionSnapshotId",
                schema: "mes",
                table: "FirmwareConfigurationExecutions",
                column: "ExecutionSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareConfigurationExecutions_ManufacturingEventId",
                schema: "mes",
                table: "FirmwareConfigurationExecutions",
                column: "ManufacturingEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareConfigurationExecutions_ProductIdentityId_RecordedAtUtc",
                schema: "mes",
                table: "FirmwareConfigurationExecutions",
                columns: ["ProductIdentityId", "RecordedAtUtc"]);

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareConfigurationExecutions_ProductIdentityId_RequirementCode",
                schema: "mes",
                table: "FirmwareConfigurationExecutions",
                columns: ["ProductIdentityId", "RequirementCode"],
                unique: true,
                filter: "[Result] = 'Succeeded'");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareConfigurationExecutions_ProductionOrderId",
                schema: "mes",
                table: "FirmwareConfigurationExecutions",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareConfigurationExecutions_RetryOfExecutionId",
                schema: "mes",
                table: "FirmwareConfigurationExecutions",
                column: "RetryOfExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_FirmwareConfigurationExecutions_SourceSystem_IdempotencyKey",
                schema: "mes",
                table: "FirmwareConfigurationExecutions",
                columns: ["SourceSystem", "IdempotencyKey"],
                unique: true);

            // 固件执行是产品谱系事实；失败也必须保留，后续重试通过新行关联而不是覆盖。
            migrationBuilder.Sql("""
                CREATE TRIGGER [mes].[TR_FirmwareConfigurationExecutions_AppendOnly]
                ON [mes].[FirmwareConfigurationExecutions]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51015, 'Firmware configuration executions are append-only.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FirmwareConfigurationExecutions",
                schema: "mes");
        }
    }
}
