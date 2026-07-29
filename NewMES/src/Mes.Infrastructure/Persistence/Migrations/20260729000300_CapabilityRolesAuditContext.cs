using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Mes.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MesDbContext))]
[Migration(MesMigrationIds.CapabilityRolesAuditContext)]
public sealed class CapabilityRolesAuditContext : Migration
{
    private const string Roles =
        "'Planner', 'ProcessEngineer', 'Operator', 'LineSupervisor', "
        + "'QualityEngineer', 'MaterialHandler', 'SystemAdministrator', 'OperationsManager'";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "audit");

        migrationBuilder.AddColumn<string>(
            name: "PasswordHash",
            schema: "security",
            table: "UserAccounts",
            type: "nvarchar(512)",
            maxLength: 512,
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "PrimaryRole",
            schema: "security",
            table: "UserAccounts",
            type: "nvarchar(40)",
            maxLength: 40,
            nullable: true);
        migrationBuilder.AddCheckConstraint(
            name: "CK_UserAccounts_PasswordHash",
            schema: "security",
            table: "UserAccounts",
            sql: "[PasswordHash] IS NULL OR LEN([PasswordHash]) > 0");
        migrationBuilder.AddCheckConstraint(
            name: "CK_UserAccounts_PrimaryRole",
            schema: "security",
            table: "UserAccounts",
            sql: $"[PrimaryRole] IS NULL OR [PrimaryRole] IN ({Roles})");

        migrationBuilder.CreateTable(
            name: "BusinessAuditRecords",
            schema: "audit",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(
                    type: "datetimeoffset",
                    nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ActorUsername = table.Column<string>(
                    type: "nvarchar(80)",
                    maxLength: 80,
                    nullable: false),
                ActorRolesSnapshot = table.Column<string>(
                    type: "nvarchar(400)",
                    maxLength: 400,
                    nullable: false),
                AuthorizedRole = table.Column<string>(
                    type: "nvarchar(40)",
                    maxLength: 40,
                    nullable: true),
                Capability = table.Column<string>(
                    type: "nvarchar(80)",
                    maxLength: 80,
                    nullable: true),
                Action = table.Column<string>(
                    type: "nvarchar(80)",
                    maxLength: 80,
                    nullable: false),
                BusinessObjectType = table.Column<string>(
                    type: "nvarchar(80)",
                    maxLength: 80,
                    nullable: false),
                BusinessObjectId = table.Column<string>(
                    type: "nvarchar(160)",
                    maxLength: 160,
                    nullable: false),
                Result = table.Column<string>(
                    type: "nvarchar(16)",
                    maxLength: 16,
                    nullable: false),
                ReasonCode = table.Column<string>(
                    type: "nvarchar(80)",
                    maxLength: 80,
                    nullable: true),
                CorrelationId = table.Column<string>(
                    type: "nvarchar(64)",
                    maxLength: 64,
                    nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BusinessAuditRecords", value => value.Id);
                table.CheckConstraint(
                    "CK_BusinessAuditRecords_AuthorizedRole",
                    $"[AuthorizedRole] IS NULL OR [AuthorizedRole] IN ({Roles})");
                table.CheckConstraint(
                    "CK_BusinessAuditRecords_Result",
                    "[Result] IN ('Succeeded', 'Denied')");
                table.ForeignKey(
                    name: "FK_BusinessAuditRecords_UserAccounts_ActorUserId",
                    column: value => value.ActorUserId,
                    principalSchema: "security",
                    principalTable: "UserAccounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(
            name: "IX_BusinessAuditRecords_ActorUserId",
            schema: "audit",
            table: "BusinessAuditRecords",
            column: "ActorUserId");
        migrationBuilder.CreateIndex(
            name: "IX_BusinessAuditRecords_OccurredAtUtc",
            schema: "audit",
            table: "BusinessAuditRecords",
            column: "OccurredAtUtc");
        migrationBuilder.CreateIndex(
            name: "IX_BusinessAuditRecords_BusinessObjectType_BusinessObjectId_OccurredAtUtc",
            schema: "audit",
            table: "BusinessAuditRecords",
            columns: ["BusinessObjectType", "BusinessObjectId", "OccurredAtUtc"]);

        migrationBuilder.CreateTable(
            name: "UserAccountRoles",
            schema: "security",
            columns: table => new
            {
                UserAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Role = table.Column<string>(
                    type: "nvarchar(40)",
                    maxLength: 40,
                    nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_UserAccountRoles",
                    value => new { value.UserAccountId, value.Role });
                table.CheckConstraint(
                    "CK_UserAccountRoles_Role",
                    $"[Role] IN ({Roles})");
                table.ForeignKey(
                    name: "FK_UserAccountRoles_UserAccounts_UserAccountId",
                    column: value => value.UserAccountId,
                    principalSchema: "security",
                    principalTable: "UserAccounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.Sql("""
            CREATE TRIGGER [audit].[TR_BusinessAuditRecords_AppendOnly]
            ON [audit].[BusinessAuditRecords]
            INSTEAD OF UPDATE, DELETE
            AS
            BEGIN
                SET NOCOUNT ON;
                THROW 51002, 'Business audit records are append-only.', 1;
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "BusinessAuditRecords", schema: "audit");
        migrationBuilder.DropTable(name: "UserAccountRoles", schema: "security");
        migrationBuilder.DropCheckConstraint(
            name: "CK_UserAccounts_PasswordHash",
            schema: "security",
            table: "UserAccounts");
        migrationBuilder.DropCheckConstraint(
            name: "CK_UserAccounts_PrimaryRole",
            schema: "security",
            table: "UserAccounts");
        migrationBuilder.DropColumn(
            name: "PasswordHash",
            schema: "security",
            table: "UserAccounts");
        migrationBuilder.DropColumn(
            name: "PrimaryRole",
            schema: "security",
            table: "UserAccounts");
    }
}
