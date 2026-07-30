using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Mes.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MesDbContext))]
[Migration(MesMigrationIds.InitialFoundation)]
/// <summary>建立安全与 MES 基础架构，以及用户、物料和生产订单的最小关系模型。</summary>
public sealed class InitialFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "mes");
        migrationBuilder.EnsureSchema(name: "security");

        migrationBuilder.CreateTable(
            name: "Materials",
            schema: "mes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Code = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                TraceabilityMode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Materials", value => value.Id);
                table.CheckConstraint(
                    "CK_Materials_TraceabilityMode",
                    "[TraceabilityMode] IN ('None', 'Lot', 'Serial')");
            });

        migrationBuilder.CreateTable(
            name: "UserAccounts",
            schema: "security",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Username = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                DisplayName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                IsActive = table.Column<bool>(type: "bit", nullable: false),
            },
            constraints: table => table.PrimaryKey("PK_UserAccounts", value => value.Id));

        migrationBuilder.CreateTable(
            name: "ProductionOrders",
            schema: "mes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OrderNumber = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PlannedQuantity = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProductionOrders", value => value.Id);
                table.CheckConstraint("CK_ProductionOrders_PlannedQuantity", "[PlannedQuantity] > 0");
                table.CheckConstraint(
                    "CK_ProductionOrders_Status",
                    "[Status] IN ('Created', 'Released', 'Closed', 'Cancelled')");
                table.ForeignKey(
                    name: "FK_ProductionOrders_Materials_MaterialId",
                    column: value => value.MaterialId,
                    principalSchema: "mes",
                    principalTable: "Materials",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Materials_Code",
            schema: "mes",
            table: "Materials",
            column: "Code",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_ProductionOrders_MaterialId",
            schema: "mes",
            table: "ProductionOrders",
            column: "MaterialId");
        migrationBuilder.CreateIndex(
            name: "IX_ProductionOrders_OrderNumber",
            schema: "mes",
            table: "ProductionOrders",
            column: "OrderNumber",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_UserAccounts_Username",
            schema: "security",
            table: "UserAccounts",
            column: "Username",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ProductionOrders", schema: "mes");
        migrationBuilder.DropTable(name: "UserAccounts", schema: "security");
        migrationBuilder.DropTable(name: "Materials", schema: "mes");
    }
}
