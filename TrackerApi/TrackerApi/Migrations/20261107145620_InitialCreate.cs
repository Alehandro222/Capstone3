using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TrackerApi.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Computers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    LastHeartbeatAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Computers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WaitlistEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaitlistEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssistTickets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ComputerId = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistTickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssistTickets_Computers_ComputerId",
                        column: x => x.ComputerId,
                        principalTable: "Computers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Passes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 6, nullable: false),
                    ComputerId = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Age = table.Column<int>(type: "INTEGER", nullable: false),
                    Sex = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IsWalkIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Passes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Passes_Computers_ComputerId",
                        column: x => x.ComputerId,
                        principalTable: "Computers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Peripherals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ComputerId = table.Column<int>(type: "INTEGER", nullable: true),
                    IsInUse = table.Column<bool>(type: "INTEGER", nullable: false),
                    InUseSince = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Peripherals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Peripherals_Computers_ComputerId",
                        column: x => x.ComputerId,
                        principalTable: "Computers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ProblemReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ComputerId = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProblemReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProblemReports_Computers_ComputerId",
                        column: x => x.ComputerId,
                        principalTable: "Computers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PassSectors",
                columns: table => new
                {
                    PassId = table.Column<int>(type: "INTEGER", nullable: false),
                    Sector = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PassSectors", x => new { x.PassId, x.Sector });
                    table.ForeignKey(
                        name: "FK_PassSectors_Passes_PassId",
                        column: x => x.PassId,
                        principalTable: "Passes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ComputerId = table.Column<int>(type: "INTEGER", nullable: false),
                    PassId = table.Column<int>(type: "INTEGER", nullable: true),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ScheduledEndAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExtensionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndReason = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sessions_Computers_ComputerId",
                        column: x => x.ComputerId,
                        principalTable: "Computers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sessions_Passes_PassId",
                        column: x => x.PassId,
                        principalTable: "Passes",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "Computers",
                columns: new[] { "Id", "LastHeartbeatAt", "Name" },
                values: new object[,]
                {
                    { 1, null, "PC-01" },
                    { 2, null, "PC-02" },
                    { 3, null, "PC-03" },
                    { 4, null, "PC-04" },
                    { 5, null, "PC-05" },
                    { 6, null, "PC-06" },
                    { 7, null, "PC-07" },
                    { 8, null, "PC-08" },
                    { 9, null, "PC-09" },
                    { 10, null, "PC-10" },
                    { 11, null, "PC-11" },
                    { 12, null, "PC-12" }
                });

            migrationBuilder.InsertData(
                table: "Peripherals",
                columns: new[] { "Id", "ComputerId", "InUseSince", "IsInUse", "Name", "Type" },
                values: new object[,]
                {
                    { 5, null, null, false, "Drawing Tablet", "DrawingTablet" },
                    { 6, null, null, false, "Desk Microphone", "Microphone" },
                    { 1, 1, null, false, "USB Mouse", "Mouse" },
                    { 2, 1, null, false, "Keyboard", "Keyboard" },
                    { 3, 1, null, false, "Headset A", "Headset" },
                    { 4, 2, null, false, "USB Mouse", "Mouse" }
                });

            migrationBuilder.InsertData(
                table: "ProblemReports",
                columns: new[] { "Id", "ComputerId", "CreatedAt", "Description", "ResolutionNotes", "ResolvedAt", "Status" },
                values: new object[] { 1, 3, new DateTime(2026, 11, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Monitor flickers and turns off.", null, null, "Open" });

            migrationBuilder.CreateIndex(
                name: "IX_AssistTickets_ComputerId",
                table: "AssistTickets",
                column: "ComputerId");

            migrationBuilder.CreateIndex(
                name: "IX_Passes_Code",
                table: "Passes",
                column: "Code",
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_Passes_ComputerId",
                table: "Passes",
                column: "ComputerId",
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_Passes_TokenHash",
                table: "Passes",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Peripherals_ComputerId",
                table: "Peripherals",
                column: "ComputerId");

            migrationBuilder.CreateIndex(
                name: "IX_ProblemReports_ComputerId",
                table: "ProblemReports",
                column: "ComputerId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_ComputerId",
                table: "Sessions",
                column: "ComputerId",
                unique: true,
                filter: "\"EndedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_PassId",
                table: "Sessions",
                column: "PassId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WaitlistEntries_TokenHash",
                table: "WaitlistEntries",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssistTickets");

            migrationBuilder.DropTable(
                name: "PassSectors");

            migrationBuilder.DropTable(
                name: "Peripherals");

            migrationBuilder.DropTable(
                name: "ProblemReports");

            migrationBuilder.DropTable(
                name: "Sessions");

            migrationBuilder.DropTable(
                name: "WaitlistEntries");

            migrationBuilder.DropTable(
                name: "Passes");

            migrationBuilder.DropTable(
                name: "Computers");
        }
    }
}
