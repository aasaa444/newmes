using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mes.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// 引入独立不合格记录和产品质量保留，使失败证据、隔离状态与后续处置决定保持不同概念。
    /// </summary>
    public partial class NonconformanceQualityHold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QualityHolds",
                schema: "mes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductIdentityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    StartReasonCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    StartReason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ReleaseCondition = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedByUsername = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ManufacturingEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityHolds", x => x.Id);
                    table.CheckConstraint("CK_QualityHolds_Status", "[Status] IN ('Active', 'Released')");
                    table.ForeignKey(
                        name: "FK_QualityHolds_ManufacturingEvents_ManufacturingEventId",
                        column: x => x.ManufacturingEventId,
                        principalSchema: "mes",
                        principalTable: "ManufacturingEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QualityHolds_ProductIdentities_ProductIdentityId",
                        column: x => x.ProductIdentityId,
                        principalSchema: "mes",
                        principalTable: "ProductIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QualityHolds_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalSchema: "mes",
                        principalTable: "ProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QualityHolds_UserAccounts_StartedByUserId",
                        column: x => x.StartedByUserId,
                        principalSchema: "security",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NonconformanceRecords",
                schema: "mes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProductIdentityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExecutionSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QualityHoldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    DetectedOperationCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DefectCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Phenomenon = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    EvidenceReference = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    RelatedTestRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceSystem = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CommandHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CommandHashAlgorithm = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ReportedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportedByUsername = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    DetectedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ManufacturingEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NonconformanceRecords", x => x.Id);
                    table.CheckConstraint("CK_NonconformanceRecords_Status", "[Status] IN ('Open', 'Dispositioned')");
                    table.ForeignKey(
                        name: "FK_NonconformanceRecords_ManufacturingEvents_ManufacturingEventId",
                        column: x => x.ManufacturingEventId,
                        principalSchema: "mes",
                        principalTable: "ManufacturingEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NonconformanceRecords_ProductIdentities_ProductIdentityId",
                        column: x => x.ProductIdentityId,
                        principalSchema: "mes",
                        principalTable: "ProductIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NonconformanceRecords_ProductionOrderExecutionSnapshots_ExecutionSnapshotId",
                        column: x => x.ExecutionSnapshotId,
                        principalSchema: "mes",
                        principalTable: "ProductionOrderExecutionSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NonconformanceRecords_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalSchema: "mes",
                        principalTable: "ProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NonconformanceRecords_QualityHolds_QualityHoldId",
                        column: x => x.QualityHoldId,
                        principalSchema: "mes",
                        principalTable: "QualityHolds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NonconformanceRecords_TestRuns_RelatedTestRunId",
                        column: x => x.RelatedTestRunId,
                        principalSchema: "mes",
                        principalTable: "TestRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NonconformanceRecords_UserAccounts_ReportedByUserId",
                        column: x => x.ReportedByUserId,
                        principalSchema: "security",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NonconformanceRecords_ExecutionSnapshotId",
                schema: "mes",
                table: "NonconformanceRecords",
                column: "ExecutionSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_NonconformanceRecords_ManufacturingEventId",
                schema: "mes",
                table: "NonconformanceRecords",
                column: "ManufacturingEventId");

            migrationBuilder.CreateIndex(
                name: "IX_NonconformanceRecords_ProductIdentityId",
                schema: "mes",
                table: "NonconformanceRecords",
                column: "ProductIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_NonconformanceRecords_ProductionOrderId",
                schema: "mes",
                table: "NonconformanceRecords",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_NonconformanceRecords_QualityHoldId",
                schema: "mes",
                table: "NonconformanceRecords",
                column: "QualityHoldId");

            migrationBuilder.CreateIndex(
                name: "IX_NonconformanceRecords_ReferenceNumber",
                schema: "mes",
                table: "NonconformanceRecords",
                column: "ReferenceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NonconformanceRecords_RelatedTestRunId",
                schema: "mes",
                table: "NonconformanceRecords",
                column: "RelatedTestRunId",
                unique: true,
                filter: "[RelatedTestRunId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NonconformanceRecords_ReportedByUserId",
                schema: "mes",
                table: "NonconformanceRecords",
                column: "ReportedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_NonconformanceRecords_SourceSystem_IdempotencyKey",
                schema: "mes",
                table: "NonconformanceRecords",
                columns: ["SourceSystem", "IdempotencyKey"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QualityHolds_ManufacturingEventId",
                schema: "mes",
                table: "QualityHolds",
                column: "ManufacturingEventId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityHolds_ProductIdentityId",
                schema: "mes",
                table: "QualityHolds",
                column: "ProductIdentityId",
                unique: true,
                filter: "[Status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_QualityHolds_ProductionOrderId",
                schema: "mes",
                table: "QualityHolds",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_QualityHolds_StartedByUserId",
                schema: "mes",
                table: "QualityHolds",
                column: "StartedByUserId");

            // Ticket 11 的不合格与保留都是已确认事实；处置和解除将在后续迁移中以受控转换开放。
            migrationBuilder.Sql(
                """
                CREATE TRIGGER [mes].[TR_NonconformanceRecords_AppendOnly]
                ON [mes].[NonconformanceRecords]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51016, 'Nonconformance records are append-only.', 1;
                END
                """);
            migrationBuilder.Sql(
                """
                CREATE TRIGGER [mes].[TR_QualityHolds_AppendOnly]
                ON [mes].[QualityHolds]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51017, 'Quality holds cannot be changed without a controlled disposition.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NonconformanceRecords",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "QualityHolds",
                schema: "mes");
        }
    }
}
