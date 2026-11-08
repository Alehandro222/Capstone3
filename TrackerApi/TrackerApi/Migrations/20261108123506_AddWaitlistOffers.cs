using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrackerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddWaitlistOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "OfferExpiresAt",
                table: "WaitlistEntries",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OfferExpiresAt",
                table: "WaitlistEntries");
        }
    }
}
