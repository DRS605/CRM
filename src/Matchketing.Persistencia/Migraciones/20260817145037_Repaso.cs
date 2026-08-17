using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchketing.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Repaso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "repaso");

            migrationBuilder.CreateTable(
                name: "pospuesta",
                schema: "repaso",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    clave = table.Column<string>(type: "character varying(70)", maxLength: 70, nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    hasta = table.Column<DateOnly>(type: "date", nullable: false),
                    en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pospuesta", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pospuesta_empresa_hasta",
                schema: "repaso",
                table: "pospuesta",
                columns: new[] { "empresa_id", "hasta" });

            migrationBuilder.CreateIndex(
                name: "ix_pospuesta_empresa_usuario_fecha",
                schema: "repaso",
                table: "pospuesta",
                columns: new[] { "empresa_id", "usuario_id", "en" });

            // Aislamiento entre empresas, igual que el resto de las tablas de datos. Ver
            // docs/despliegue.md: sin un rol sin superusuario, esto no hace nada.
            migrationBuilder.Sql("""
                ALTER TABLE repaso.pospuesta ENABLE ROW LEVEL SECURITY;
                ALTER TABLE repaso.pospuesta FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_empresa ON repaso.pospuesta
                    USING (empresa_id::text = current_setting('app.empresa_actual', true))
                    WITH CHECK (empresa_id::text = current_setting('app.empresa_actual', true));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS aislamiento_empresa ON repaso.pospuesta;");

            migrationBuilder.DropTable(
                name: "pospuesta",
                schema: "repaso");
        }
    }
}
