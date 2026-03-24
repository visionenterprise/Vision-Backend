using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace vision_backend.Migrations
{
    /// <inheritdoc />
    public partial class AddHierarchyOwnershipAndRoleModuleAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LeaveRequests_Users_TargetPillerId",
                table: "LeaveRequests");

            migrationBuilder.AddColumn<Guid>(
                name: "OwningSuperAdminId",
                table: "Vouchers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetPillerId",
                table: "Vouchers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedSuperAdminId",
                table: "Users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwningSuperAdminId",
                table: "LeaveRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RoleModuleAccessConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    Modules = table.Column<List<string>>(type: "text[]", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleModuleAccessConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_OwningSuperAdminId",
                table: "Vouchers",
                column: "OwningSuperAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_TargetPillerId",
                table: "Vouchers",
                column: "TargetPillerId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_AssignedSuperAdminId",
                table: "Users",
                column: "AssignedSuperAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRequests_OwningSuperAdminId",
                table: "LeaveRequests",
                column: "OwningSuperAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_RoleModuleAccessConfigs_Role",
                table: "RoleModuleAccessConfigs",
                column: "Role",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LeaveRequests_Users_OwningSuperAdminId",
                table: "LeaveRequests",
                column: "OwningSuperAdminId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_LeaveRequests_Users_TargetPillerId",
                table: "LeaveRequests",
                column: "TargetPillerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Users_AssignedSuperAdminId",
                table: "Users",
                column: "AssignedSuperAdminId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Vouchers_Users_OwningSuperAdminId",
                table: "Vouchers",
                column: "OwningSuperAdminId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Vouchers_Users_TargetPillerId",
                table: "Vouchers",
                column: "TargetPillerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LeaveRequests_Users_OwningSuperAdminId",
                table: "LeaveRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_LeaveRequests_Users_TargetPillerId",
                table: "LeaveRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Users_AssignedSuperAdminId",
                table: "Users");

            migrationBuilder.DropForeignKey(
                name: "FK_Vouchers_Users_OwningSuperAdminId",
                table: "Vouchers");

            migrationBuilder.DropForeignKey(
                name: "FK_Vouchers_Users_TargetPillerId",
                table: "Vouchers");

            migrationBuilder.DropTable(
                name: "RoleModuleAccessConfigs");

            migrationBuilder.DropIndex(
                name: "IX_Vouchers_OwningSuperAdminId",
                table: "Vouchers");

            migrationBuilder.DropIndex(
                name: "IX_Vouchers_TargetPillerId",
                table: "Vouchers");

            migrationBuilder.DropIndex(
                name: "IX_Users_AssignedSuperAdminId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_LeaveRequests_OwningSuperAdminId",
                table: "LeaveRequests");

            migrationBuilder.DropColumn(
                name: "OwningSuperAdminId",
                table: "Vouchers");

            migrationBuilder.DropColumn(
                name: "TargetPillerId",
                table: "Vouchers");

            migrationBuilder.DropColumn(
                name: "AssignedSuperAdminId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "OwningSuperAdminId",
                table: "LeaveRequests");

            migrationBuilder.AddForeignKey(
                name: "FK_LeaveRequests_Users_TargetPillerId",
                table: "LeaveRequests",
                column: "TargetPillerId",
                principalTable: "Users",
                principalColumn: "Id");
        }
    }
}
