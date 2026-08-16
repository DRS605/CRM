using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchketing.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Captacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "cumplimiento");

            migrationBuilder.EnsureSchema(
                name: "captacion");

            migrationBuilder.CreateTable(
                name: "consentimiento",
                schema: "cumplimiento",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contacto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    finalidad = table.Column<int>(type: "integer", nullable: false),
                    base_legal = table.Column<int>(type: "integer", nullable: false),
                    canal = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    texto_aceptado = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ip = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    agente = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    otorgado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retirado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consentimiento", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "envio_formulario",
                schema: "captacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    formulario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    datos = table.Column<string>(type: "jsonb", nullable: false),
                    ip = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    agente = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    contacto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recibido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_envio_formulario", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "formulario",
                schema: "captacion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    clave = table.Column<string>(type: "character varying(22)", maxLength: 22, nullable: false),
                    texto_consentimiento = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    pide_telefono = table.Column<bool>(type: "boolean", nullable: false),
                    pide_empresa = table.Column<bool>(type: "boolean", nullable: false),
                    pide_mensaje = table.Column<bool>(type: "boolean", nullable: false),
                    pagina_gracias = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    origen = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_formulario", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consentimiento_empresa_contacto",
                schema: "cumplimiento",
                table: "consentimiento",
                columns: new[] { "empresa_id", "contacto_id", "finalidad" });

            migrationBuilder.CreateIndex(
                name: "ix_envio_empresa_formulario_fecha",
                schema: "captacion",
                table: "envio_formulario",
                columns: new[] { "empresa_id", "formulario_id", "recibido_en" });

            migrationBuilder.CreateIndex(
                name: "ix_formulario_clave",
                schema: "captacion",
                table: "formulario",
                column: "clave",
                unique: true);

            // `captacion.formulario` se queda **sin RLS a propósito**: hay que poder leerlo antes de
            // saber de qué empresa es, porque quien rellena el formulario en una web no está
            // autenticado y la empresa se deduce justo de esa lectura. Lo que lo protege es la clave
            // aleatoria de 22 caracteres, y el filtro global de EF sigue aplicando a todo el acceso
            // autenticado. Las tablas que se escriben *después* de conocer la empresa sí llevan RLS.
            migrationBuilder.Sql("""
                ALTER TABLE captacion.envio_formulario ENABLE ROW LEVEL SECURITY;
                ALTER TABLE captacion.envio_formulario FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_empresa ON captacion.envio_formulario
                    USING (empresa_id::text = current_setting('app.empresa_actual', true))
                    WITH CHECK (empresa_id::text = current_setting('app.empresa_actual', true));

                ALTER TABLE cumplimiento.consentimiento ENABLE ROW LEVEL SECURITY;
                ALTER TABLE cumplimiento.consentimiento FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_empresa ON cumplimiento.consentimiento
                    USING (empresa_id::text = current_setting('app.empresa_actual', true))
                    WITH CHECK (empresa_id::text = current_setting('app.empresa_actual', true));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS aislamiento_empresa ON captacion.envio_formulario;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS aislamiento_empresa ON cumplimiento.consentimiento;");

            migrationBuilder.DropTable(
                name: "consentimiento",
                schema: "cumplimiento");

            migrationBuilder.DropTable(
                name: "envio_formulario",
                schema: "captacion");

            migrationBuilder.DropTable(
                name: "formulario",
                schema: "captacion");
        }
    }
}
