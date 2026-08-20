using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchketing.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Correo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "correo");

            migrationBuilder.AddColumn<bool>(
                name: "sigue_aperturas",
                schema: "organizacion",
                table: "empresa",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "mensaje",
                schema: "correo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contacto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    para = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    asunto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    cuerpo = table.Column<string>(type: "text", nullable: false),
                    para_que = table.Column<int>(type: "integer", nullable: false),
                    plantilla_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    proximo_intento_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    enviado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultimo_fallo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    token_apertura = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    primera_apertura_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultima_apertura_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    aperturas = table.Column<int>(type: "integer", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mensaje", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plantilla",
                schema: "correo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    asunto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cuerpo = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    para_que = table.Column<int>(type: "integer", nullable: false),
                    creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    usos = table.Column<int>(type: "integer", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plantilla", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mensaje_contacto",
                schema: "correo",
                table: "mensaje",
                columns: new[] { "contacto_id", "creado_en" });

            migrationBuilder.CreateIndex(
                name: "ix_mensaje_pendientes",
                schema: "correo",
                table: "mensaje",
                columns: new[] { "estado", "proximo_intento_en" });

            migrationBuilder.CreateIndex(
                name: "ix_mensaje_token",
                schema: "correo",
                table: "mensaje",
                column: "token_apertura",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plantilla_empresa_nombre",
                schema: "correo",
                table: "plantilla",
                columns: new[] { "empresa_id", "nombre" });

            // La segunda barrera del aislamiento, que EF no sabe generar.
            //
            // `correo.mensaje` es de las tablas más sensibles del sistema: guarda el texto exacto de lo
            // que se le ha escrito a una persona. Sin RLS quedaría defendida solo por el filtro de EF, y
            // eso es una sola barrera para un dato que no se puede recuperar si se filtra.
            foreach (var tabla in new[] { "plantilla", "mensaje" })
            {
                migrationBuilder.Sql($@"
                    ALTER TABLE correo.{tabla} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE correo.{tabla} FORCE ROW LEVEL SECURITY;
                    CREATE POLICY aislamiento_empresa ON correo.{tabla}
                        USING (empresa_id::text = current_setting('app.empresa_actual', true))
                        WITH CHECK (empresa_id::text = current_setting('app.empresa_actual', true));");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var tabla in new[] { "plantilla", "mensaje" })
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS aislamiento_empresa ON correo.{tabla};");
            }

            migrationBuilder.DropTable(
                name: "mensaje",
                schema: "correo");

            migrationBuilder.DropTable(
                name: "plantilla",
                schema: "correo");

            migrationBuilder.DropColumn(
                name: "sigue_aperturas",
                schema: "organizacion",
                table: "empresa");
        }
    }
}
