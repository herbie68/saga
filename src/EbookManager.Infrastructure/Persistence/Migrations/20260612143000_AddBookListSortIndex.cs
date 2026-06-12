using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EbookManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(LibraryDbContext))]
    [Migration("20260612143000_AddBookListSortIndex")]
    public partial class AddBookListSortIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Books_NormalizedTitle_Id",
                table: "Books",
                columns: new[] { "NormalizedTitle", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Books_NormalizedTitle_Id",
                table: "Books");
        }
    }
}
