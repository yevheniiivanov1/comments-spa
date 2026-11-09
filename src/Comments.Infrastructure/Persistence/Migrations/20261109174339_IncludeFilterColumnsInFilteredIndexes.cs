using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Comments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IncludeFilterColumnsInFilteredIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Pending",
                table: "OutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_Comments_TopLevel_CreatedAt",
                table: "Comments");

            migrationBuilder.DropIndex(
                name: "IX_Comments_TopLevel_Email",
                table: "Comments");

            migrationBuilder.DropIndex(
                name: "IX_Comments_TopLevel_UserName",
                table: "Comments");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Pending",
                table: "OutboxMessages",
                column: "Id",
                filter: "[ProcessedAt] IS NULL")
                .Annotation("SqlServer:Include", new[] { "ProcessedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_TopLevel_CreatedAt",
                table: "Comments",
                columns: new[] { "CreatedAt", "Id" },
                filter: "[ParentId] IS NULL")
                .Annotation("SqlServer:Include", new[] { "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_TopLevel_Email",
                table: "Comments",
                columns: new[] { "Email", "Id" },
                filter: "[ParentId] IS NULL")
                .Annotation("SqlServer:Include", new[] { "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_TopLevel_UserName",
                table: "Comments",
                columns: new[] { "UserName", "Id" },
                filter: "[ParentId] IS NULL")
                .Annotation("SqlServer:Include", new[] { "ParentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Pending",
                table: "OutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_Comments_TopLevel_CreatedAt",
                table: "Comments");

            migrationBuilder.DropIndex(
                name: "IX_Comments_TopLevel_Email",
                table: "Comments");

            migrationBuilder.DropIndex(
                name: "IX_Comments_TopLevel_UserName",
                table: "Comments");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Pending",
                table: "OutboxMessages",
                column: "Id",
                filter: "[ProcessedAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_TopLevel_CreatedAt",
                table: "Comments",
                columns: new[] { "CreatedAt", "Id" },
                filter: "[ParentId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_TopLevel_Email",
                table: "Comments",
                columns: new[] { "Email", "Id" },
                filter: "[ParentId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_TopLevel_UserName",
                table: "Comments",
                columns: new[] { "UserName", "Id" },
                filter: "[ParentId] IS NULL");
        }
    }
}
