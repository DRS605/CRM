using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchketing.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class CamposPropios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "campos");

            migrationBuilder.CreateTable(
                name: "campo",
                schema: "campos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ambito = table.Column<int>(type: "integer", nullable: false),
                    nombre = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    clave = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    opciones = table.Column<string[]>(type: "text[]", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campo", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "valor",
                schema: "campos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    campo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ambito = table.Column<int>(type: "integer", nullable: false),
                    entidad_id = table.Column<Guid>(type: "uuid", nullable: false),
                    texto = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_valor", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_campo_ambito_orden",
                schema: "campos",
                table: "campo",
                columns: new[] { "empresa_id", "ambito", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_campo_clave_unica",
                schema: "campos",
                table: "campo",
                columns: new[] { "empresa_id", "ambito", "clave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_valor_entidad",
                schema: "campos",
                table: "valor",
                columns: new[] { "empresa_id", "entidad_id" });

            migrationBuilder.CreateIndex(
                name: "ix_valor_unico",
                schema: "campos",
                table: "valor",
                columns: new[] { "campo_id", "entidad_id" },
                unique: true);

            // La segunda barrera, la que EF no sabe generar. En `campos.valor` hay más que en casi
            // ninguna otra tabla del sistema: es donde una empresa mete el dato que este CRM no tiene, y
            // eso puede ser un DNI, una matrícula, un número de póliza o una dirección. No se sabe qué
            // hay dentro, y por eso hay que tratarlo como lo más sensible, no como lo menos.
            //
            // La definición también va aislada, y no por el dato: los nombres de los campos que se
            // inventa una empresa dicen a qué se dedica y cómo trabaja.
            migrationBuilder.Sql(@"
                ALTER TABLE campos.campo ENABLE ROW LEVEL SECURITY;
                ALTER TABLE campos.campo FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_empresa ON campos.campo
                    USING (empresa_id::text = current_setting('app.empresa_actual', true))
                    WITH CHECK (empresa_id::text = current_setting('app.empresa_actual', true));

                ALTER TABLE campos.valor ENABLE ROW LEVEL SECURITY;
                ALTER TABLE campos.valor FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_empresa ON campos.valor
                    USING (empresa_id::text = current_setting('app.empresa_actual', true))
                    WITH CHECK (empresa_id::text = current_setting('app.empresa_actual', true));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "campo",
                schema: "campos");

            migrationBuilder.DropTable(
                name: "valor",
                schema: "campos");
        }
    }
}
