using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartLibrary.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeCatalogRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ShelfEntryId",
                table: "Copies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CategoryEntryId",
                table: "Books",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PublisherEntryId",
                table: "Books",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BookAuthor",
                columns: table => new
                {
                    BookId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorEntryId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookAuthor", x => new { x.BookId, x.AuthorEntryId });
                    table.ForeignKey(
                        name: "FK_BookAuthor_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookAuthor_CatalogEntries_AuthorEntryId",
                        column: x => x.AuthorEntryId,
                        principalTable: "CatalogEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Copies_ShelfEntryId",
                table: "Copies",
                column: "ShelfEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_Books_CategoryEntryId",
                table: "Books",
                column: "CategoryEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_Books_PublisherEntryId",
                table: "Books",
                column: "PublisherEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_BookAuthor_AuthorEntryId",
                table: "BookAuthor",
                column: "AuthorEntryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Books_CatalogEntries_CategoryEntryId",
                table: "Books",
                column: "CategoryEntryId",
                principalTable: "CatalogEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Books_CatalogEntries_PublisherEntryId",
                table: "Books",
                column: "PublisherEntryId",
                principalTable: "CatalogEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Copies_CatalogEntries_ShelfEntryId",
                table: "Copies",
                column: "ShelfEntryId",
                principalTable: "CatalogEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Books_CatalogEntries_CategoryEntryId",
                table: "Books");

            migrationBuilder.DropForeignKey(
                name: "FK_Books_CatalogEntries_PublisherEntryId",
                table: "Books");

            migrationBuilder.DropForeignKey(
                name: "FK_Copies_CatalogEntries_ShelfEntryId",
                table: "Copies");

            migrationBuilder.DropTable(
                name: "BookAuthor");

            migrationBuilder.DropIndex(
                name: "IX_Copies_ShelfEntryId",
                table: "Copies");

            migrationBuilder.DropIndex(
                name: "IX_Books_CategoryEntryId",
                table: "Books");

            migrationBuilder.DropIndex(
                name: "IX_Books_PublisherEntryId",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "ShelfEntryId",
                table: "Copies");

            migrationBuilder.DropColumn(
                name: "CategoryEntryId",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "PublisherEntryId",
                table: "Books");
        }
    }
}
