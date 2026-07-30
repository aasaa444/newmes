using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mes.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VersionedTestSpecificationAndRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TestRuns",
                schema: "mes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductIdentityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExecutionSnapshotId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SpecificationCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    SpecificationVersion = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    SpecificationDefinitionHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OperationCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    DeviceId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    DeviceVersion = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    FixtureId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    FixtureVersion = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    RawReportReference = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Result = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DiagnosticCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    DiagnosticMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RetryOfTestRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OperationCompleted = table.Column<bool>(type: "bit", nullable: false),
                    NextOperationCodeAfter = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestRuns", x => x.Id);
                    table.CheckConstraint("CK_TestRuns_Result", "[Result] IN ('Succeeded', 'Failed')");
                    table.CheckConstraint("CK_TestRuns_Time", "[EndedAtUtc] >= [StartedAtUtc]");
                    table.ForeignKey(
                        name: "FK_TestRuns_ManufacturingEvents_ManufacturingEventId",
                        column: x => x.ManufacturingEventId,
                        principalSchema: "mes",
                        principalTable: "ManufacturingEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestRuns_ProductIdentities_ProductIdentityId",
                        column: x => x.ProductIdentityId,
                        principalSchema: "mes",
                        principalTable: "ProductIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestRuns_ProductionOrderExecutionSnapshots_ExecutionSnapshotId",
                        column: x => x.ExecutionSnapshotId,
                        principalSchema: "mes",
                        principalTable: "ProductionOrderExecutionSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestRuns_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalSchema: "mes",
                        principalTable: "ProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestRuns_TestRuns_RetryOfTestRunId",
                        column: x => x.RetryOfTestRunId,
                        principalSchema: "mes",
                        principalTable: "TestRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestRuns_UserAccounts_ActorUserId",
                        column: x => x.ActorUserId,
                        principalSchema: "security",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TestSpecificationVersions",
                schema: "mes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OperationCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Applicability = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    DefinitionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DefinitionHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DefinitionHashAlgorithm = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedByUsername = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ApprovalEvidenceReference = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestSpecificationVersions", x => x.Id);
                    table.CheckConstraint("CK_TestSpecificationVersions_Approval", "([IsApproved] = 0 AND [ApprovedAtUtc] IS NULL AND [ApprovedByUserId] IS NULL AND [ApprovedByUsername] IS NULL AND [ApprovalEvidenceReference] IS NULL) OR ([IsApproved] = 1 AND [ApprovedAtUtc] IS NOT NULL AND [ApprovedByUserId] IS NOT NULL AND [ApprovedByUsername] IS NOT NULL AND [ApprovalEvidenceReference] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TestSpecificationVersions_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalSchema: "mes",
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestSpecificationVersions_UserAccounts_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalSchema: "security",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestSpecificationVersions_UserAccounts_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "security",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TestMeasurements",
                schema: "mes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TestRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DataType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Required = table.Column<bool>(type: "bit", nullable: false),
                    RawValue = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    DecimalPlaces = table.Column<int>(type: "int", nullable: true),
                    LowerLimit = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    UpperLimit = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    ExpectedText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ExpectedBoolean = table.Column<bool>(type: "bit", nullable: true),
                    NumericValue = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    BooleanValue = table.Column<bool>(type: "bit", nullable: true),
                    Result = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DiagnosticCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    DiagnosticMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReportedDiagnosticCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ReportedDiagnosticMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestMeasurements", x => x.Id);
                    table.CheckConstraint("CK_TestMeasurements_Result", "[Result] IN ('Passed', 'Failed')");
                    table.ForeignKey(
                        name: "FK_TestMeasurements_TestRuns_TestRunId",
                        column: x => x.TestRunId,
                        principalSchema: "mes",
                        principalTable: "TestRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestMeasurements_TestRunId_ItemCode",
                schema: "mes",
                table: "TestMeasurements",
                columns: ["TestRunId", "ItemCode"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestRuns_ActorUserId",
                schema: "mes",
                table: "TestRuns",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TestRuns_ExecutionSnapshotId",
                schema: "mes",
                table: "TestRuns",
                column: "ExecutionSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_TestRuns_ManufacturingEventId",
                schema: "mes",
                table: "TestRuns",
                column: "ManufacturingEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestRuns_ProductIdentityId_RecordedAtUtc",
                schema: "mes",
                table: "TestRuns",
                columns: ["ProductIdentityId", "RecordedAtUtc"]);

            migrationBuilder.CreateIndex(
                name: "IX_TestRuns_ProductIdentityId_SpecificationCode_SpecificationVersion",
                schema: "mes",
                table: "TestRuns",
                columns: ["ProductIdentityId", "SpecificationCode", "SpecificationVersion"],
                unique: true,
                filter: "[Result] = 'Succeeded'");

            migrationBuilder.CreateIndex(
                name: "IX_TestRuns_ProductionOrderId",
                schema: "mes",
                table: "TestRuns",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_TestRuns_RetryOfTestRunId",
                schema: "mes",
                table: "TestRuns",
                column: "RetryOfTestRunId");

            migrationBuilder.CreateIndex(
                name: "IX_TestRuns_SourceSystem_IdempotencyKey",
                schema: "mes",
                table: "TestRuns",
                columns: ["SourceSystem", "IdempotencyKey"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestSpecificationVersions_ApprovedByUserId",
                schema: "mes",
                table: "TestSpecificationVersions",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TestSpecificationVersions_Code_Version",
                schema: "mes",
                table: "TestSpecificationVersions",
                columns: ["Code", "Version"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestSpecificationVersions_CreatedByUserId",
                schema: "mes",
                table: "TestSpecificationVersions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TestSpecificationVersions_MaterialId_OperationCode_IsApproved",
                schema: "mes",
                table: "TestSpecificationVersions",
                columns: ["MaterialId", "OperationCode", "IsApproved"]);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER [mes].[TR_TestSpecificationVersions_ControlledApproval]
                ON [mes].[TestSpecificationVersions]
                INSTEAD OF INSERT, UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;

                    IF NOT EXISTS (SELECT 1 FROM deleted)
                    BEGIN
                        IF EXISTS
                        (
                            SELECT 1
                            FROM inserted
                            WHERE [IsApproved] <> 0
                               OR [ApprovedAtUtc] IS NOT NULL
                               OR [ApprovedByUserId] IS NOT NULL
                               OR [ApprovedByUsername] IS NOT NULL
                               OR [ApprovalEvidenceReference] IS NOT NULL
                        )
                            THROW 51012, 'Test specification versions must be inserted as drafts.', 1;

                        INSERT INTO [mes].[TestSpecificationVersions]
                        (
                            [Id], [Code], [Version], [MaterialId], [OperationCode],
                            [Applicability], [DefinitionJson], [DefinitionHash],
                            [DefinitionHashAlgorithm], [IsApproved], [CreatedAtUtc],
                            [CreatedByUserId], [ApprovedAtUtc], [ApprovedByUserId],
                            [ApprovedByUsername], [ApprovalEvidenceReference]
                        )
                        SELECT
                            [Id], [Code], [Version], [MaterialId], [OperationCode],
                            [Applicability], [DefinitionJson], [DefinitionHash],
                            [DefinitionHashAlgorithm], [IsApproved], [CreatedAtUtc],
                            [CreatedByUserId], [ApprovedAtUtc], [ApprovedByUserId],
                            [ApprovedByUsername], [ApprovalEvidenceReference]
                        FROM inserted;
                        RETURN;
                    END

                    IF NOT EXISTS (SELECT 1 FROM inserted)
                        THROW 51011, 'Test specification versions cannot be deleted.', 1;

                    IF (SELECT COUNT(*) FROM inserted) <> (SELECT COUNT(*) FROM deleted)
                       OR EXISTS
                       (
                           SELECT 1
                           FROM inserted AS i
                           INNER JOIN deleted AS d ON d.[Id] = i.[Id]
                           WHERE d.[IsApproved] <> 0
                              OR i.[IsApproved] <> 1
                              OR d.[Code] <> i.[Code]
                              OR d.[Version] <> i.[Version]
                              OR d.[MaterialId] <> i.[MaterialId]
                              OR d.[OperationCode] <> i.[OperationCode]
                              OR d.[Applicability] <> i.[Applicability]
                              OR d.[DefinitionJson] <> i.[DefinitionJson]
                              OR d.[DefinitionHash] <> i.[DefinitionHash]
                              OR d.[DefinitionHashAlgorithm] <> i.[DefinitionHashAlgorithm]
                              OR d.[CreatedAtUtc] <> i.[CreatedAtUtc]
                              OR d.[CreatedByUserId] <> i.[CreatedByUserId]
                              OR d.[ApprovedAtUtc] IS NOT NULL
                              OR d.[ApprovedByUserId] IS NOT NULL
                              OR d.[ApprovedByUsername] IS NOT NULL
                              OR d.[ApprovalEvidenceReference] IS NOT NULL
                              OR i.[ApprovedAtUtc] IS NULL
                              OR i.[ApprovedByUserId] IS NULL
                              OR i.[ApprovedByUsername] IS NULL
                              OR i.[ApprovalEvidenceReference] IS NULL
                       )
                        THROW 51011, 'Only the initial approval transition is allowed for a test specification version.', 1;

                    UPDATE target
                    SET [IsApproved] = 1,
                        [ApprovedAtUtc] = i.[ApprovedAtUtc],
                        [ApprovedByUserId] = i.[ApprovedByUserId],
                        [ApprovedByUsername] = i.[ApprovedByUsername],
                        [ApprovalEvidenceReference] = i.[ApprovalEvidenceReference]
                    FROM [mes].[TestSpecificationVersions] AS target
                    INNER JOIN inserted AS i ON i.[Id] = target.[Id];
                END
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER [mes].[TR_TestRuns_AppendOnly]
                ON [mes].[TestRuns]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000, 'Test runs are append-only.', 1;
                END
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER [mes].[TR_TestMeasurements_AppendOnly]
                ON [mes].[TestMeasurements]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000, 'Test measurements are append-only.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TestMeasurements",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "TestSpecificationVersions",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "TestRuns",
                schema: "mes");
        }
    }
}
