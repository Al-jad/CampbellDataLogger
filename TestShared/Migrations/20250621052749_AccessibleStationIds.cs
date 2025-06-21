using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TestWorkerService.Migrations
{
    /// <inheritdoc />
    public partial class AccessibleStationIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<long>>(
                name: "AccessibleStationIds",
                table: "AspNetUsers",
                type: "bigint[]",
                nullable: false,
                defaultValueSql: "ARRAY[]::bigint[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccessibleStationIds",
                table: "AspNetUsers");
        }
    }
}
