using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchketing.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Webhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "webhooks");

            migrationBuilder.CreateTable(
                name: "entrega",
                schema: "webhooks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    suscripcion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    cuerpo = table.Column<string>(type: "text", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    proximo_intento_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    entregada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultimo_codigo = table.Column<int>(type: "integer", nullable: true),
                    ultimo_fallo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entrega", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "suscripcion",
                schema: "webhooks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    secreto = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    motivo_apagado = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    fallos_seguidos = table.Column<int>(type: "integer", nullable: false),
                    creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ultima_entrega_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tipos = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suscripcion", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entrega_pendientes",
                schema: "webhooks",
                table: "entrega",
                columns: new[] { "estado", "proximo_intento_en" });

            migrationBuilder.CreateIndex(
                name: "ix_entrega_suscripcion",
                schema: "webhooks",
                table: "entrega",
                columns: new[] { "suscripcion_id", "creada_en" });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_empresa_activa",
                schema: "webhooks",
                table: "suscripcion",
                columns: new[] { "empresa_id", "activa" });

            // La segunda barrera del aislamiento, que EF no sabe generar. Sin esto, las dos tablas
            // quedarían defendidas solo por el filtro global de EF: todo seguiría funcionando y las
            // pruebas seguirían pasando, y ese es exactamente el problema.
            //
            // `FORCE` además de `ENABLE` porque el dueño de la tabla se salta las políticas si no se
            // le fuerza, y en desarrollo el dueño y la aplicación suelen ser el mismo rol.
            foreach (var tabla in new[] { "suscripcion", "entrega" })
            {
                migrationBuilder.Sql($@"
                    ALTER TABLE webhooks.{tabla} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE webhooks.{tabla} FORCE ROW LEVEL SECURITY;
                    CREATE POLICY aislamiento_empresa ON webhooks.{tabla}
                        USING (empresa_id::text = current_setting('app.empresa_actual', true))
                        WITH CHECK (empresa_id::text = current_setting('app.empresa_actual', true));");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var tabla in new[] { "suscripcion", "entrega" })
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS aislamiento_empresa ON webhooks.{tabla};");
            }

            migrationBuilder.DropTable(
                name: "entrega",
                schema: "webhooks");

            migrationBuilder.DropTable(
                name: "suscripcion",
                schema: "webhooks");
        }
    }
}
