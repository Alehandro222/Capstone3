using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrackerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddKioskPairing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KioskKeyHash",
                table: "Computers",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PairedAt",
                table: "Computers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PairingCodeExpiresAt",
                table: "Computers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PairingCodeHash",
                table: "Computers",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Computers",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "KioskKeyHash", "PairedAt", "PairingCodeExpiresAt", "PairingCodeHash" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "Computers",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "KioskKeyHash", "PairedAt", "PairingCodeExpiresAt", "PairingCodeHash" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "Computers",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "KioskKeyHash", "PairedAt", "PairingCodeExpiresAt", "PairingCodeHash" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "Computers",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "KioskKeyHash", "PairedAt", "PairingCodeExpiresAt", "PairingCodeHash" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "Computers",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "KioskKeyHash", "PairedAt", "PairingCodeExpiresAt", "PairingCodeHash" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "Computers",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "KioskKeyHash", "PairedAt", "PairingCodeExpiresAt", "PairingCodeHash" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "Computers",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "KioskKeyHash", "PairedAt", "PairingCodeExpiresAt", "PairingCodeHash" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "Computers",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "KioskKeyHash", "PairedAt", "PairingCodeExpiresAt", "PairingCodeHash" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "Computers",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "KioskKeyHash", "PairedAt", "PairingCodeExpiresAt", "PairingCodeHash" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "Computers",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "KioskKeyHash", "PairedAt", "PairingCodeExpiresAt", "PairingCodeHash" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "Computers",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "KioskKeyHash", "PairedAt", "PairingCodeExpiresAt", "PairingCodeHash" },
                values: new object[] { null, null, null, null });

            migrationBuilder.UpdateData(
                table: "Computers",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "KioskKeyHash", "PairedAt", "PairingCodeExpiresAt", "PairingCodeHash" },
                values: new object[] { null, null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KioskKeyHash",
                table: "Computers");

            migrationBuilder.DropColumn(
                name: "PairedAt",
                table: "Computers");

            migrationBuilder.DropColumn(
                name: "PairingCodeExpiresAt",
                table: "Computers");

            migrationBuilder.DropColumn(
                name: "PairingCodeHash",
                table: "Computers");
        }
    }
}
