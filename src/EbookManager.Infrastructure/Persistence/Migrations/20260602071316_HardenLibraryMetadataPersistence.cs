using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EbookManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenLibraryMetadataPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Order",
                table: "BookTags",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE "BookTags"
                SET "Order" = (
                    SELECT COUNT(*)
                    FROM "BookTags" AS "Earlier"
                    WHERE "Earlier"."BookId" = "BookTags"."BookId"
                      AND "Earlier"."TagId" < "BookTags"."TagId"
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_BookTags_BookId_Order",
                table: "BookTags",
                columns: new[] { "BookId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Books_NormalizedTitle",
                table: "Books",
                column: "NormalizedTitle");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BookTags_BookId_Order",
                table: "BookTags");

            migrationBuilder.DropIndex(
                name: "IX_Books_NormalizedTitle",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "Order",
                table: "BookTags");
        }
    }
}
