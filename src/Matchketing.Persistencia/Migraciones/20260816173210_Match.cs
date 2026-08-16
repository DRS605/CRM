using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchketing.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Match : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "match");

            migrationBuilder.AddColumn<string>(
                name: "zonas",
                schema: "identidad",
                table: "membresia",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "puntuacion_match",
                schema: "match",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contacto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match = table.Column<int>(type: "integer", nullable: true),
                    encaje = table.Column<int>(type: "integer", nullable: false),
                    momento = table.Column<int>(type: "integer", nullable: false),
                    motivos = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    sin_historico = table.Column<bool>(type: "boolean", nullable: false),
                    calculada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_puntuacion_match", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "senal",
                schema: "match",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contacto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    ocurrida_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_senal", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_puntuacion_empresa_contacto",
                schema: "match",
                table: "puntuacion_match",
                columns: new[] { "empresa_id", "contacto_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_senal_empresa_contacto_fecha",
                schema: "match",
                table: "senal",
                columns: new[] { "empresa_id", "contacto_id", "ocurrida_en" });

            foreach (var tabla in new[] { "senal", "puntuacion_match" })
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE match.{tabla} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE match.{tabla} FORCE ROW LEVEL SECURITY;
                    CREATE POLICY aislamiento_empresa ON match.{tabla}
                        USING (empresa_id::text = current_setting('app.empresa_actual', true))
                        WITH CHECK (empresa_id::text = current_setting('app.empresa_actual', true));
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var tabla in new[] { "senal", "puntuacion_match" })
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS aislamiento_empresa ON match.{tabla};");
                migrationBuilder.Sql($"ALTER TABLE match.{tabla} DISABLE ROW LEVEL SECURITY;");
            }

            migrationBuilder.DropTable(
                name: "puntuacion_match",
                schema: "match");

            migrationBuilder.DropTable(
                name: "senal",
                schema: "match");

            migrationBuilder.DropColumn(
                name: "zonas",
                schema: "identidad",
                table: "membresia");
        }
    }
}
