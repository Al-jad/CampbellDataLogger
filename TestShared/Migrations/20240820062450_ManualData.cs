using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TestWorkerService.Migrations
{
    /// <inheritdoc />
    public partial class ManualData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Stations",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ManualData",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TimeStamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StationId = table.Column<long>(type: "bigint", nullable: false),
                    PH = table.Column<double>(type: "double precision", nullable: false),
                    Temp = table.Column<double>(type: "double precision", nullable: false),
                    DO2 = table.Column<double>(type: "double precision", nullable: false),
                    BOD5 = table.Column<double>(type: "double precision", nullable: false),
                    PO4 = table.Column<double>(type: "double precision", nullable: false),
                    NO3 = table.Column<double>(type: "double precision", nullable: false),
                    Ca = table.Column<double>(type: "double precision", nullable: false),
                    Mg = table.Column<double>(type: "double precision", nullable: false),
                    TH = table.Column<double>(type: "double precision", nullable: false),
                    K = table.Column<double>(type: "double precision", nullable: false),
                    Na = table.Column<double>(type: "double precision", nullable: false),
                    SO4 = table.Column<double>(type: "double precision", nullable: false),
                    CL = table.Column<double>(type: "double precision", nullable: false),
                    TDS = table.Column<double>(type: "double precision", nullable: false),
                    EC = table.Column<double>(type: "double precision", nullable: false),
                    Alk = table.Column<double>(type: "double precision", nullable: false),
                    Acid = table.Column<double>(type: "double precision", nullable: false),
                    OnG = table.Column<double>(type: "double precision", nullable: false),
                    WQI = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManualData", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManualData_Stations_StationId",
                        column: x => x.StationId,
                        principalTable: "Stations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ManualData_StationId",
                table: "ManualData",
                column: "StationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ManualData");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Stations");
        }
    }
}
