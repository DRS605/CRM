using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchketing.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Equipo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invitacion",
                schema: "identidad",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    rol = table.Column<int>(type: "integer", nullable: false),
                    invitado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    huella_token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    caduca_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    aceptada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retirada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invitacion", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invitacion_empresa",
                schema: "identidad",
                table: "invitacion",
                columns: new[] { "empresa_id", "caduca_en" });

            migrationBuilder.CreateIndex(
                name: "ix_invitacion_huella",
                schema: "identidad",
                table: "invitacion",
                column: "huella_token",
                unique: true);

            // La segunda barrera del aislamiento, que EF no sabe generar. `identidad.invitacion` sí la
            // lleva, al contrario que `identidad.membresia`: una invitación es de una empresa concreta,
            // y dentro hay un correo de una persona.
            migrationBuilder.Sql(@"
                ALTER TABLE identidad.invitacion ENABLE ROW LEVEL SECURITY;
                ALTER TABLE identidad.invitacion FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_empresa ON identidad.invitacion
                    USING (empresa_id::text = current_setting('app.empresa_actual', true))
                    WITH CHECK (empresa_id::text = current_setting('app.empresa_actual', true));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS aislamiento_empresa ON identidad.invitacion;");

            migrationBuilder.DropTable(
                name: "invitacion",
                schema: "identidad");
        }
    }
}
