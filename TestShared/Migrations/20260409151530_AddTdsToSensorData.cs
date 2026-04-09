using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TestWorkerService.Migrations
{
    /// <inheritdoc />
    public partial class AddTdsToSensorData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "TDS",
                table: "SensorData",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TDS",
                table: "SensorData");
        }
    }
}
