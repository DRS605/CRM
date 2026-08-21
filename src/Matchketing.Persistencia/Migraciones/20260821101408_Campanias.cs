using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchketing.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Campanias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "campania");

            migrationBuilder.CreateTable(
                name: "campania",
                schema: "campania",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    segmento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plantilla_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lanzada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lanzada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    terminada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    destinatarios = table.Column<int>(type: "integer", nullable: false),
                    encolados = table.Column<int>(type: "integer", nullable: false),
                    excluidos = table.Column<int>(type: "integer", nullable: false),
                    segmento_al_lanzar = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campania", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "envio",
                schema: "campania",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    campania_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contacto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    motivo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    correo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resuelto_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_envio", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "segmento",
                schema: "campania",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: true),
                    provincia = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    origen = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    match_minimo = table.Column<int>(type: "integer", nullable: true),
                    sin_actividad_dias = table.Column<int>(type: "integer", nullable: true),
                    etapa_id = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_segmento", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_campania_empresa_estado",
                schema: "campania",
                table: "campania",
                columns: new[] { "empresa_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_campania_segmento",
                schema: "campania",
                table: "campania",
                column: "segmento_id");

            migrationBuilder.CreateIndex(
                name: "ix_envio_campania_estado",
                schema: "campania",
                table: "envio",
                columns: new[] { "campania_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_envio_unico",
                schema: "campania",
                table: "envio",
                columns: new[] { "campania_id", "contacto_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_segmento_empresa_nombre",
                schema: "campania",
                table: "segmento",
                columns: new[] { "empresa_id", "nombre" });

            // La segunda barrera del aislamiento, la que EF no sabe generar. Las tres tablas la llevan.
            //
            // `campania.envio` es la que más importa de las tres: es una lista de identificadores de
            // contacto, así que sin RLS una consulta mal escrita podría contar a cuánta gente le escribe
            // otra empresa, y con qué resultado. No guarda nombres ni correos —a propósito— pero saber
            // que la empresa de al lado mandó una campaña a 1.800 personas y la mitad no tenía permiso
            // ya es información de la empresa de al lado.
            //
            // `FORCE` además de `ENABLE` porque el dueño de la tabla se salta las políticas si no se le
            // fuerza, y en desarrollo la aplicación se conecta con un rol que es dueño. Ver
            // `docs/despliegue.md`: en producción el rol de la aplicación no es superusuario.
            migrationBuilder.Sql(@"
                ALTER TABLE campania.segmento ENABLE ROW LEVEL SECURITY;
                ALTER TABLE campania.segmento FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_empresa ON campania.segmento
                    USING (empresa_id::text = current_setting('app.empresa_actual', true))
                    WITH CHECK (empresa_id::text = current_setting('app.empresa_actual', true));

                ALTER TABLE campania.campania ENABLE ROW LEVEL SECURITY;
                ALTER TABLE campania.campania FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_empresa ON campania.campania
                    USING (empresa_id::text = current_setting('app.empresa_actual', true))
                    WITH CHECK (empresa_id::text = current_setting('app.empresa_actual', true));

                ALTER TABLE campania.envio ENABLE ROW LEVEL SECURITY;
                ALTER TABLE campania.envio FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_empresa ON campania.envio
                    USING (empresa_id::text = current_setting('app.empresa_actual', true))
                    WITH CHECK (empresa_id::text = current_setting('app.empresa_actual', true));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS aislamiento_empresa ON campania.envio;
                DROP POLICY IF EXISTS aislamiento_empresa ON campania.campania;
                DROP POLICY IF EXISTS aislamiento_empresa ON campania.segmento;");

            migrationBuilder.DropTable(
                name: "campania",
                schema: "campania");

            migrationBuilder.DropTable(
                name: "envio",
                schema: "campania");

            migrationBuilder.DropTable(
                name: "segmento",
                schema: "campania");
        }
    }
}
