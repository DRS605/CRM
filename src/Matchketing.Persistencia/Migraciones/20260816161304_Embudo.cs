using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchketing.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Embudo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "embudo");

            migrationBuilder.CreateTable(
                name: "embudo",
                schema: "embudo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    por_defecto = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_embudo", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "etapa",
                schema: "embudo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    embudo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    probabilidad = table.Column<int>(type: "integer", nullable: false),
                    dias_aviso = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_etapa", x => x.id);
                    table.ForeignKey(
                        name: "FK_etapa_embudo_embudo_id",
                        column: x => x.embudo_id,
                        principalSchema: "embudo",
                        principalTable: "embudo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "oportunidad",
                schema: "embudo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contacto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuenta_id = table.Column<Guid>(type: "uuid", nullable: true),
                    titulo = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    importe = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    embudo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    etapa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entro_en_etapa_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    prevista_cierre = table.Column<DateOnly>(type: "date", nullable: true),
                    propietario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo = table.Column<int>(type: "integer", nullable: true),
                    detalle_motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    cerrada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oportunidad", x => x.id);
                    table.ForeignKey(
                        name: "FK_oportunidad_etapa_etapa_id",
                        column: x => x.etapa_id,
                        principalSchema: "embudo",
                        principalTable: "etapa",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_embudo_empresa_defecto",
                schema: "embudo",
                table: "embudo",
                columns: new[] { "empresa_id", "por_defecto" });

            migrationBuilder.CreateIndex(
                name: "ix_etapa_embudo_orden",
                schema: "embudo",
                table: "etapa",
                columns: new[] { "embudo_id", "orden" });

            migrationBuilder.CreateIndex(
                name: "IX_oportunidad_etapa_id",
                schema: "embudo",
                table: "oportunidad",
                column: "etapa_id");

            migrationBuilder.CreateIndex(
                name: "ix_oportunidad_empresa_cerrada",
                schema: "embudo",
                table: "oportunidad",
                columns: new[] { "empresa_id", "cerrada_en" });

            migrationBuilder.CreateIndex(
                name: "ix_oportunidad_empresa_contacto",
                schema: "embudo",
                table: "oportunidad",
                columns: new[] { "empresa_id", "contacto_id" });

            migrationBuilder.CreateIndex(
                name: "ix_oportunidad_empresa_etapa",
                schema: "embudo",
                table: "oportunidad",
                columns: new[] { "empresa_id", "etapa_id" });

            // Misma doble barrera que en contactos. `etapa` no lleva empresa_id: cuelga de `embudo`
            // y su aislamiento viene por la clave ajena, así que no necesita política propia.
            foreach (var tabla in new[] { "embudo", "oportunidad" })
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE embudo.{tabla} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE embudo.{tabla} FORCE ROW LEVEL SECURITY;
                    CREATE POLICY aislamiento_empresa ON embudo.{tabla}
                        USING (empresa_id::text = current_setting('app.empresa_actual', true))
                        WITH CHECK (empresa_id::text = current_setting('app.empresa_actual', true));
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var tabla in new[] { "embudo", "oportunidad" })
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS aislamiento_empresa ON embudo.{tabla};");
                migrationBuilder.Sql($"ALTER TABLE embudo.{tabla} DISABLE ROW LEVEL SECURITY;");
            }

            migrationBuilder.DropTable(
                name: "oportunidad",
                schema: "embudo");

            migrationBuilder.DropTable(
                name: "etapa",
                schema: "embudo");

            migrationBuilder.DropTable(
                name: "embudo",
                schema: "embudo");
        }
    }
}
