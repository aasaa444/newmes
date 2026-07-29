using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mes.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialLegacyBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ActorUserName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SubjectType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SubjectId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ErpOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BusinessKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErpOutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Materials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IsFinishedGood = table.Column<bool>(type: "bit", nullable: false),
                    IsKeyComponent = table.Column<bool>(type: "bit", nullable: false),
                    RequiresSerialNumber = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Materials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductionLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CanViewOpsOverview = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Boms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinishedMaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Boms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Boms_Materials_FinishedMaterialId",
                        column: x => x.FinishedMaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinishedGoodsInventories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityOnHand = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinishedGoodsInventories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinishedGoodsInventories_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LineSideInventories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityOnHand = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineSideInventories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LineSideInventories_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcessRoutes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    FinishedMaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessRoutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessRoutes_Materials_FinishedMaterialId",
                        column: x => x.FinishedMaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BomLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComponentMaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuantityPer = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BomLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BomLines_Boms_BomId",
                        column: x => x.BomId,
                        principalTable: "Boms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BomLines_Materials_ComponentMaterialId",
                        column: x => x.ComponentMaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcessSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcessRouteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IsQualityStep = table.Column<bool>(type: "bit", nullable: false),
                    ReworkToSequence = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessSteps_ProcessRoutes_ProcessRouteId",
                        column: x => x.ProcessRouteId,
                        principalTable: "ProcessRoutes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    FinishedMaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlannedQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CompletedQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ScrappedQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ProcessRouteId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FrozenRouteVersion = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    BomId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FrozenBomVersion = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    InProcessSerialCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Boms_BomId",
                        column: x => x.BomId,
                        principalTable: "Boms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrders_Materials_FinishedMaterialId",
                        column: x => x.FinishedMaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrders_ProcessRoutes_ProcessRouteId",
                        column: x => x.ProcessRouteId,
                        principalTable: "ProcessRoutes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkStations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProductionLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BoundProcessStepId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkStations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkStations_ProcessSteps_BoundProcessStepId",
                        column: x => x.BoundProcessStepId,
                        principalTable: "ProcessSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkStations_ProductionLines_ProductionLineId",
                        column: x => x.ProductionLineId,
                        principalTable: "ProductionLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductSerials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SerialNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrentProcessStepId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductSerials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductSerials_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderIssueLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssuedQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PendingQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ConsumedQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderIssueLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderIssueLines_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderIssueLines_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ComponentBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductSerialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComponentMaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComponentSerialNo = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProcessStepId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BoundAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OperatorUserName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComponentBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComponentBindings_Materials_ComponentMaterialId",
                        column: x => x.ComponentMaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ComponentBindings_ProductSerials_ProductSerialId",
                        column: x => x.ProductSerialId,
                        principalTable: "ProductSerials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SerialPassRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductSerialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcessStepId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkStationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Result = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OperatorUserName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Remark = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SerialPassRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SerialPassRecords_ProcessSteps_ProcessStepId",
                        column: x => x.ProcessStepId,
                        principalTable: "ProcessSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SerialPassRecords_ProductSerials_ProductSerialId",
                        column: x => x.ProductSerialId,
                        principalTable: "ProductSerials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SerialPassRecords_WorkStations_WorkStationId",
                        column: x => x.WorkStationId,
                        principalTable: "WorkStations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OccurredAt",
                table: "AuditEntries",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_BomLines_BomId",
                table: "BomLines",
                column: "BomId");

            migrationBuilder.CreateIndex(
                name: "IX_BomLines_ComponentMaterialId",
                table: "BomLines",
                column: "ComponentMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_Boms_FinishedMaterialId",
                table: "Boms",
                column: "FinishedMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_ComponentBindings_ComponentMaterialId",
                table: "ComponentBindings",
                column: "ComponentMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_ComponentBindings_ComponentSerialNo",
                table: "ComponentBindings",
                column: "ComponentSerialNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComponentBindings_ProductSerialId",
                table: "ComponentBindings",
                column: "ProductSerialId");

            migrationBuilder.CreateIndex(
                name: "IX_FinishedGoodsInventories_MaterialId",
                table: "FinishedGoodsInventories",
                column: "MaterialId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LineSideInventories_MaterialId",
                table: "LineSideInventories",
                column: "MaterialId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Materials_Code",
                table: "Materials",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessRoutes_Code",
                table: "ProcessRoutes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessRoutes_FinishedMaterialId",
                table: "ProcessRoutes",
                column: "FinishedMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessSteps_ProcessRouteId_Sequence",
                table: "ProcessSteps",
                columns: new[] { "ProcessRouteId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionLines_Code",
                table: "ProductionLines",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductSerials_SerialNo",
                table: "ProductSerials",
                column: "SerialNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductSerials_WorkOrderId",
                table: "ProductSerials",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SerialPassRecords_ProcessStepId",
                table: "SerialPassRecords",
                column: "ProcessStepId");

            migrationBuilder.CreateIndex(
                name: "IX_SerialPassRecords_ProductSerialId",
                table: "SerialPassRecords",
                column: "ProductSerialId");

            migrationBuilder.CreateIndex(
                name: "IX_SerialPassRecords_WorkStationId",
                table: "SerialPassRecords",
                column: "WorkStationId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_UserName",
                table: "Users",
                column: "UserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderIssueLines_MaterialId",
                table: "WorkOrderIssueLines",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderIssueLines_WorkOrderId_MaterialId",
                table: "WorkOrderIssueLines",
                columns: new[] { "WorkOrderId", "MaterialId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_BomId",
                table: "WorkOrders",
                column: "BomId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_FinishedMaterialId",
                table: "WorkOrders",
                column: "FinishedMaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_OrderNo",
                table: "WorkOrders",
                column: "OrderNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_ProcessRouteId",
                table: "WorkOrders",
                column: "ProcessRouteId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkStations_BoundProcessStepId",
                table: "WorkStations",
                column: "BoundProcessStepId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkStations_Code",
                table: "WorkStations",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkStations_ProductionLineId",
                table: "WorkStations",
                column: "ProductionLineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntries");

            migrationBuilder.DropTable(
                name: "BomLines");

            migrationBuilder.DropTable(
                name: "ComponentBindings");

            migrationBuilder.DropTable(
                name: "ErpOutboxMessages");

            migrationBuilder.DropTable(
                name: "FinishedGoodsInventories");

            migrationBuilder.DropTable(
                name: "LineSideInventories");

            migrationBuilder.DropTable(
                name: "SerialPassRecords");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "WorkOrderIssueLines");

            migrationBuilder.DropTable(
                name: "ProductSerials");

            migrationBuilder.DropTable(
                name: "WorkStations");

            migrationBuilder.DropTable(
                name: "WorkOrders");

            migrationBuilder.DropTable(
                name: "ProcessSteps");

            migrationBuilder.DropTable(
                name: "ProductionLines");

            migrationBuilder.DropTable(
                name: "Boms");

            migrationBuilder.DropTable(
                name: "ProcessRoutes");

            migrationBuilder.DropTable(
                name: "Materials");
        }
    }
}
