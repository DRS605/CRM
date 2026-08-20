using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchketing.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Automatizacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "automatizacion");

            migrationBuilder.CreateTable(
                name: "ejecucion",
                schema: "automatizacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    regla_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sujeto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contacto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    que_hizo = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    cuando_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ejecucion", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "regla",
                schema: "automatizacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    disparador = table.Column<int>(type: "integer", nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ultima_vez_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    veces = table.Column<int>(type: "integer", nullable: false),
                    acciones = table.Column<string>(type: "text", nullable: false),
                    condiciones = table.Column<string>(type: "text", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_regla", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ejecucion_regla",
                schema: "automatizacion",
                table: "ejecucion",
                columns: new[] { "regla_id", "cuando_en" });

            migrationBuilder.CreateIndex(
                name: "ix_ejecucion_una_vez",
                schema: "automatizacion",
                table: "ejecucion",
                columns: new[] { "regla_id", "sujeto_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_regla_activas",
                schema: "automatizacion",
                table: "regla",
                columns: new[] { "empresa_id", "activa", "disparador" });

            // La segunda barrera del aislamiento, que EF no sabe generar.
            foreach (var tabla in new[] { "regla", "ejecucion" })
            {
                migrationBuilder.Sql($@"
                    ALTER TABLE automatizacion.{tabla} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE automatizacion.{tabla} FORCE ROW LEVEL SECURITY;
                    CREATE POLICY aislamiento_empresa ON automatizacion.{tabla}
                        USING (empresa_id::text = current_setting('app.empresa_actual', true))
                        WITH CHECK (empresa_id::text = current_setting('app.empresa_actual', true));");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var tabla in new[] { "regla", "ejecucion" })
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS aislamiento_empresa ON automatizacion.{tabla};");
            }

            migrationBuilder.DropTable(
                name: "ejecucion",
                schema: "automatizacion");

            migrationBuilder.DropTable(
                name: "regla",
                schema: "automatizacion");
        }
    }
}
