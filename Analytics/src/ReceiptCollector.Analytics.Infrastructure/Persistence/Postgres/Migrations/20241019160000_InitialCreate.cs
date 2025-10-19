using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ReceiptCollector.Analytics.Infrastructure.Persistence.Postgres.Migrations;

[DbContext(typeof(ReceiptDbContext))]
[Migration("20241019160000_InitialCreate")]
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "users",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_users", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "receipts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                Merchant = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                TotalAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                PurchasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_receipts", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "commodities",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Quantity = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                UnitPrice = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                Nds = table.Column<int>(type: "integer", nullable: false),
                NdsSum = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                CategoryId = table.Column<int>(type: "integer", nullable: true),
                CategoryName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_commodities", x => x.Id);
                table.ForeignKey(
                    name: "FK_commodities_receipts_ReceiptId",
                    column: x => x.ReceiptId,
                    principalTable: "receipts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_commodities_ReceiptId",
            table: "commodities",
            column: "ReceiptId");

        migrationBuilder.CreateIndex(
            name: "IX_users_ExternalId",
            table: "users",
            column: "ExternalId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "commodities");

        migrationBuilder.DropTable(
            name: "receipts");

        migrationBuilder.DropTable(
            name: "users");
    }
}
