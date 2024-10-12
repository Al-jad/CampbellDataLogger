using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TestWorkerService.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomFieldsToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SensorData_Stations_StationId",
                table: "SensorData");

            migrationBuilder.AlterColumn<long>(
                name: "StationId",
                table: "SensorData",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<List<string>>(
                name: "AccessibleCities",
                table: "AspNetUsers",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]
                );

            migrationBuilder.AddColumn<bool>(
                name: "IsAdmin",
                table: "AspNetUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddForeignKey(
                name: "FK_SensorData_Stations_StationId",
                table: "SensorData",
                column: "StationId",
                principalTable: "Stations",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SensorData_Stations_StationId",
                table: "SensorData");

            migrationBuilder.DropColumn(
                name: "AccessibleCities",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "IsAdmin",
                table: "AspNetUsers");

            migrationBuilder.AlterColumn<long>(
                name: "StationId",
                table: "SensorData",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SensorData_Stations_StationId",
                table: "SensorData",
                column: "StationId",
                principalTable: "Stations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
