using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EbookManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImportRunContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IncludeSubdirectories",
                table: "ImportRuns",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "ImportRuns",
                type: "TEXT",
                nullable: false,
                defaultValue: "FileImport");

            migrationBuilder.AddColumn<string>(
                name: "SourcePath",
                table: "ImportRuns",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IncludeSubdirectories",
                table: "ImportRuns");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "ImportRuns");

            migrationBuilder.DropColumn(
                name: "SourcePath",
                table: "ImportRuns");
        }
    }
}
