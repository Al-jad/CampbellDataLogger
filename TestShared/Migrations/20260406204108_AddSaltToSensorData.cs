using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TestWorkerService.Migrations
{
    /// <inheritdoc />
    public partial class AddSaltToSensorData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Salt",
                table: "SensorData",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Salt",
                table: "SensorData");
        }
    }
}
