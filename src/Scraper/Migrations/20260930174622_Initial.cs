using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scraper.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_entries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EntityKey = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    BeforeJson = table.Column<string>(type: "TEXT", nullable: true),
                    AfterJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "blocket_listings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    PriceSek = table.Column<int>(type: "INTEGER", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", nullable: false),
                    Location = table.Column<string>(type: "TEXT", nullable: false),
                    Latitude = table.Column<double>(type: "REAL", nullable: false),
                    Longitude = table.Column<double>(type: "REAL", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    ImageUrl = table.Column<string>(type: "TEXT", nullable: false),
                    SellerType = table.Column<string>(type: "TEXT", nullable: true),
                    TradeType = table.Column<string>(type: "TEXT", nullable: false),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blocket_listings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "quotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    Author = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "quote_tags",
                columns: table => new
                {
                    QuoteId = table.Column<int>(type: "INTEGER", nullable: false),
                    Tag = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quote_tags", x => new { x.QuoteId, x.Tag });
                    table.ForeignKey(
                        name: "FK_quote_tags_quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_quotes_Text_Author",
                table: "quotes",
                columns: new[] { "Text", "Author" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entries");

            migrationBuilder.DropTable(
                name: "blocket_listings");

            migrationBuilder.DropTable(
                name: "quote_tags");

            migrationBuilder.DropTable(
                name: "quotes");
        }
    }
}
