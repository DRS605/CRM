using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchketing.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Objetivos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "objetivos");

            migrationBuilder.CreateTable(
                name: "objetivo",
                schema: "objetivos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mes = table.Column<DateOnly>(type: "date", nullable: false),
                    importe = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    fijado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_objetivo", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_objetivo_mes",
                schema: "objetivos",
                table: "objetivo",
                columns: new[] { "empresa_id", "mes" });

            migrationBuilder.CreateIndex(
                name: "ix_objetivo_unico",
                schema: "objetivos",
                table: "objetivo",
                columns: new[] { "empresa_id", "usuario_id", "mes" },
                unique: true);

            // La segunda barrera, la que EF no sabe generar. Aquí no hay datos personales —un
            // identificador y un importe— pero sí hay algo que no puede salir de la empresa: cuánto se
            // le pide a cada comercial y cuánto lleva. Es exactamente la información que no se comparte
            // ni dentro de la propia empresa sin permiso, mucho menos con la de al lado.
            migrationBuilder.Sql(@"
                ALTER TABLE objetivos.objetivo ENABLE ROW LEVEL SECURITY;
                ALTER TABLE objetivos.objetivo FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_empresa ON objetivos.objetivo
                    USING (empresa_id::text = current_setting('app.empresa_actual', true))
                    WITH CHECK (empresa_id::text = current_setting('app.empresa_actual', true));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS aislamiento_empresa ON objetivos.objetivo;");

            migrationBuilder.DropTable(
                name: "objetivo",
                schema: "objetivos");
        }
    }
}
