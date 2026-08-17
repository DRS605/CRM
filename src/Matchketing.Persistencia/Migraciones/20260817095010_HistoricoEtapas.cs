using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchketing.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class HistoricoEtapas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "paso_etapa",
                schema: "embudo",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    oportunidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    etapa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entro_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_paso_etapa", x => x.id);
                    table.ForeignKey(
                        name: "FK_paso_etapa_oportunidad_oportunidad_id",
                        column: x => x.oportunidad_id,
                        principalSchema: "embudo",
                        principalTable: "oportunidad",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_paso_etapa_oportunidad_id",
                schema: "embudo",
                table: "paso_etapa",
                column: "oportunidad_id");

            migrationBuilder.CreateIndex(
                name: "ix_paso_etapa_etapa_oportunidad",
                schema: "embudo",
                table: "paso_etapa",
                columns: new[] { "etapa_id", "oportunidad_id" });

            // `paso_etapa` no lleva empresa_id: cuelga de `oportunidad`, que sí tiene su política, y
            // su aislamiento viene por la clave ajena. Igual que `etapa` con `embudo`.
            //
            // Relleno para lo que ya existía: cada oportunidad viva se anota al menos en su etapa
            // actual, para que los informes no la cuenten como si nunca hubiera pasado por ningún
            // sitio. El histórico anterior a esta migración no se puede reconstruir —no se guardaba—
            // y eso es preferible a inventárselo.
            migrationBuilder.Sql("""
                INSERT INTO embudo.paso_etapa (id, oportunidad_id, etapa_id, entro_en)
                SELECT gen_random_uuid(), o.id, o.etapa_id, o.entro_en_etapa_en
                FROM embudo.oportunidad o;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "paso_etapa",
                schema: "embudo");
        }
    }
}
