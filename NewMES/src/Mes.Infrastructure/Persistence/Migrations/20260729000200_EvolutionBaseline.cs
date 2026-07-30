using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Mes.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MesDbContext))]
[Migration(MesMigrationIds.EvolutionBaseline)]
/// <summary>加入 ERP 来源、并发版本和只追加制造事件，形成可演进数据库的第一条历史证据链。</summary>
public sealed class EvolutionBaseline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SourceReference",
            schema: "mes",
            table: "ProductionOrders",
            type: "nvarchar(160)",
            maxLength: 160,
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "SourceSystem",
            schema: "mes",
            table: "ProductionOrders",
            type: "nvarchar(80)",
            maxLength: 80,
            nullable: true);
        migrationBuilder.AddColumn<byte[]>(
            name: "Version",
            schema: "mes",
            table: "ProductionOrders",
            type: "rowversion",
            rowVersion: true,
            nullable: false);

        migrationBuilder.CreateTable(
            name: "ManufacturingEvents",
            schema: "mes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EventType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                AggregateType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                AggregateId = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                Actor = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                CorrectsEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ManufacturingEvents", value => value.Id);
                table.ForeignKey(
                    name: "FK_ManufacturingEvents_ManufacturingEvents_CorrectsEventId",
                    column: value => value.CorrectsEventId,
                    principalSchema: "mes",
                    principalTable: "ManufacturingEvents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(
            name: "IX_ManufacturingEvents_AggregateType_AggregateId_OccurredAtUtc",
            schema: "mes",
            table: "ManufacturingEvents",
            columns: ["AggregateType", "AggregateId", "OccurredAtUtc"]);
        migrationBuilder.CreateIndex(
            name: "IX_ManufacturingEvents_CorrectsEventId",
            schema: "mes",
            table: "ManufacturingEvents",
            column: "CorrectsEventId");

        // 数据库级禁止修改和删除，确保非 EF 写入路径也只能追加纠正事件。
        migrationBuilder.Sql("""
            CREATE TRIGGER [mes].[TR_ManufacturingEvents_AppendOnly]
            ON [mes].[ManufacturingEvents]
            INSTEAD OF UPDATE, DELETE
            AS
            BEGIN
                SET NOCOUNT ON;
                THROW 51001, 'Manufacturing events are append-only; append a correcting event instead.', 1;
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ManufacturingEvents", schema: "mes");
        migrationBuilder.DropColumn(name: "SourceReference", schema: "mes", table: "ProductionOrders");
        migrationBuilder.DropColumn(name: "SourceSystem", schema: "mes", table: "ProductionOrders");
        migrationBuilder.DropColumn(name: "Version", schema: "mes", table: "ProductionOrders");
    }
}
