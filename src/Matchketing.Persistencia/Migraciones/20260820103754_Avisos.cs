using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchketing.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Avisos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "avisos");

            migrationBuilder.CreateTable(
                name: "suscripcion",
                schema: "avisos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    clave_publica = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    secreto = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ultimo_aviso_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suscripcion", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_suscripcion_empresa_usuario",
                schema: "avisos",
                table: "suscripcion",
                columns: new[] { "empresa_id", "usuario_id" });

            // Aislamiento entre empresas, igual que el resto de las tablas de datos.
            migrationBuilder.Sql("""
                ALTER TABLE avisos.suscripcion ENABLE ROW LEVEL SECURITY;
                ALTER TABLE avisos.suscripcion FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_empresa ON avisos.suscripcion
                    USING (empresa_id::text = current_setting('app.empresa_actual', true))
                    WITH CHECK (empresa_id::text = current_setting('app.empresa_actual', true));
                """);

            migrationBuilder.CreateIndex(
                name: "ix_suscripcion_endpoint",
                schema: "avisos",
                table: "suscripcion",
                column: "endpoint",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS aislamiento_empresa ON avisos.suscripcion;");

            migrationBuilder.DropTable(
                name: "suscripcion",
                schema: "avisos");
        }
    }
}
