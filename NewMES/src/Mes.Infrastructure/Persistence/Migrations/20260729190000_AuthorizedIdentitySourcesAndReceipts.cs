using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mes.Infrastructure.Persistence.Migrations
{
    /// <summary>登记获授权的身份来源、号段授权和只追加命令回执，阻止任意调用方自行生成产品身份。</summary>
    public partial class AuthorizedIdentitySourcesAndReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IdentitySourceRegistrations",
                schema: "mes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceSystem = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsDemo = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    AuthorizationEvidence = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    AuthorizedCallerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegisteredByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegisteredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentitySourceRegistrations", x => x.Id);
                    table.CheckConstraint("CK_IdentitySourceRegistrations_DemoFlag", "([SourceType] = 'DemoControlledPool' AND [IsDemo] = 1) OR ([SourceType] <> 'DemoControlledPool' AND [IsDemo] = 0)");
                    table.ForeignKey(
                        name: "FK_IdentitySourceRegistrations_UserAccounts_AuthorizedCallerUserId",
                        column: x => x.AuthorizedCallerUserId,
                        principalSchema: "security",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IdentitySourceRegistrations_UserAccounts_RegisteredByUserId",
                        column: x => x.RegisteredByUserId,
                        principalSchema: "security",
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StartWipCommandReceipts",
                schema: "mes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceSystem = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CommandHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProductIdentityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SerialNumber = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProductionOrderNumber = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    OrderStatus = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    StartedQuantity = table.Column<int>(type: "int", nullable: false),
                    IdentitySourceSystem = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    IdentitySourceType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsDemo = table.Column<bool>(type: "bit", nullable: false),
                    NextOperationCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StartWipCommandReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StartWipCommandReceipts_ProductIdentities_ProductIdentityId",
                        column: x => x.ProductIdentityId,
                        principalSchema: "mes",
                        principalTable: "ProductIdentities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StartWipCommandReceipts_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalSchema: "mes",
                        principalTable: "ProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IdentitySourceIdentifierGrants",
                schema: "mes",
                columns: table => new
                {
                    IdentitySourceRegistrationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdentifierType = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentitySourceIdentifierGrants", x => new { x.IdentitySourceRegistrationId, x.IdentifierType });
                    table.ForeignKey(
                        name: "FK_IdentitySourceIdentifierGrants_IdentitySourceRegistrations_IdentitySourceRegistrationId",
                        column: x => x.IdentitySourceRegistrationId,
                        principalSchema: "mes",
                        principalTable: "IdentitySourceRegistrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdentitySourceRegistrations_AuthorizedCallerUserId",
                schema: "mes",
                table: "IdentitySourceRegistrations",
                column: "AuthorizedCallerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_IdentitySourceRegistrations_RegisteredByUserId",
                schema: "mes",
                table: "IdentitySourceRegistrations",
                column: "RegisteredByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_IdentitySourceRegistrations_SourceSystem",
                schema: "mes",
                table: "IdentitySourceRegistrations",
                column: "SourceSystem",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StartWipCommandReceipts_ProductIdentityId_CompletedAtUtc",
                schema: "mes",
                table: "StartWipCommandReceipts",
                columns: ["ProductIdentityId", "CompletedAtUtc"]);

            migrationBuilder.CreateIndex(
                name: "IX_StartWipCommandReceipts_ProductionOrderId",
                schema: "mes",
                table: "StartWipCommandReceipts",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_StartWipCommandReceipts_SourceSystem_IdempotencyKey",
                schema: "mes",
                table: "StartWipCommandReceipts",
                columns: ["SourceSystem", "IdempotencyKey"],
                unique: true);

            // 成功或失败回执都是幂等证据，更新会改变重放结论，因此统一只允许追加。
            migrationBuilder.Sql("""
                CREATE TRIGGER [mes].[TR_StartWipCommandReceipts_AppendOnly]
                ON [mes].[StartWipCommandReceipts]
                AFTER UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51013, 'START_WIP command receipts are append-only.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdentitySourceIdentifierGrants",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "StartWipCommandReceipts",
                schema: "mes");

            migrationBuilder.DropTable(
                name: "IdentitySourceRegistrations",
                schema: "mes");
        }
    }
}
