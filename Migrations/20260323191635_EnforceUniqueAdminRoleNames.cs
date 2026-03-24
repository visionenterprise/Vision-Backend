using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace vision_backend.Migrations
{
    /// <inheritdoc />
    public partial class EnforceUniqueAdminRoleNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AdminRoles_Name_CreatedBy",
                table: "AdminRoles");

            migrationBuilder.Sql(@"
                WITH ranked_roles AS (
                    SELECT
                        ""Id"",
                        ""Name"",
                        ROW_NUMBER() OVER (PARTITION BY LOWER(TRIM(""Name"")) ORDER BY ""CreatedAt"", ""Id"") AS rn,
                        FIRST_VALUE(""Id"") OVER (PARTITION BY LOWER(TRIM(""Name"")) ORDER BY ""CreatedAt"", ""Id"") AS keep_id
                    FROM ""AdminRoles""
                ),
                duplicate_roles AS (
                    SELECT ""Id"", keep_id
                    FROM ranked_roles
                    WHERE rn > 1
                )
                UPDATE ""Users"" u
                SET ""AdminRoleId"" = d.keep_id
                FROM duplicate_roles d
                WHERE u.""AdminRoleId"" = d.""Id"";

                WITH ranked_roles AS (
                    SELECT
                        ""Id"",
                        ""Name"",
                        ROW_NUMBER() OVER (PARTITION BY LOWER(TRIM(""Name"")) ORDER BY ""CreatedAt"", ""Id"") AS rn,
                        FIRST_VALUE(""Id"") OVER (PARTITION BY LOWER(TRIM(""Name"")) ORDER BY ""CreatedAt"", ""Id"") AS keep_id
                    FROM ""AdminRoles""
                ),
                duplicate_roles AS (
                    SELECT ""Id"", keep_id
                    FROM ranked_roles
                    WHERE rn > 1
                )
                UPDATE ""RolePermissions"" rp
                SET ""RoleId"" = d.keep_id
                FROM duplicate_roles d
                WHERE rp.""RoleId"" = d.""Id""
                  AND NOT EXISTS (
                      SELECT 1
                      FROM ""RolePermissions"" existing
                      WHERE existing.""RoleId"" = d.keep_id
                        AND existing.""PermissionId"" = rp.""PermissionId""
                  );

                WITH ranked_roles AS (
                    SELECT
                        ""Id"",
                        ""Name"",
                        ROW_NUMBER() OVER (PARTITION BY LOWER(TRIM(""Name"")) ORDER BY ""CreatedAt"", ""Id"") AS rn,
                        FIRST_VALUE(""Id"") OVER (PARTITION BY LOWER(TRIM(""Name"")) ORDER BY ""CreatedAt"", ""Id"") AS keep_id
                    FROM ""AdminRoles""
                ),
                duplicate_roles AS (
                    SELECT ""Id"", keep_id
                    FROM ranked_roles
                    WHERE rn > 1
                )
                DELETE FROM ""RolePermissions"" rp
                USING duplicate_roles d
                WHERE rp.""RoleId"" = d.""Id"";

                WITH ranked_roles AS (
                    SELECT
                        ""Id"",
                        ""Name"",
                        ROW_NUMBER() OVER (PARTITION BY LOWER(TRIM(""Name"")) ORDER BY ""CreatedAt"", ""Id"") AS rn,
                        FIRST_VALUE(""Id"") OVER (PARTITION BY LOWER(TRIM(""Name"")) ORDER BY ""CreatedAt"", ""Id"") AS keep_id
                    FROM ""AdminRoles""
                ),
                duplicate_roles AS (
                    SELECT ""Id"", keep_id
                    FROM ranked_roles
                    WHERE rn > 1
                )
                DELETE FROM ""AdminRoles"" ar
                USING duplicate_roles d
                WHERE ar.""Id"" = d.""Id"";
            ");

            migrationBuilder.CreateIndex(
                name: "IX_AdminRoles_Name",
                table: "AdminRoles",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AdminRoles_Name",
                table: "AdminRoles");

            migrationBuilder.CreateIndex(
                name: "IX_AdminRoles_Name_CreatedBy",
                table: "AdminRoles",
                columns: new[] { "Name", "CreatedBy" },
                unique: true);
        }
    }
}
