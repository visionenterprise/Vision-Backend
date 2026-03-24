using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace vision_backend.Migrations
{
    /// <inheritdoc />
    public partial class AddLeaveQuotaAndSeedAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AnnualQuotaDays",
                table: "LeaveTypes",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnnualQuotaDays",
                table: "LeaveTypes");
        }
    }
}
