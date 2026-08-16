using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchketing.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Contactos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "contactos");

            migrationBuilder.CreateTable(
                name: "cuenta",
                schema: "contactos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    nif = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    sector = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    provincia = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    tamano = table.Column<int>(type: "integer", nullable: true),
                    web = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cuenta", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contacto",
                schema: "contactos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    telefono = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    cargo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    cuenta_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origen = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    propietario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    fusionado_en_id = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contacto", x => x.id);
                    table.ForeignKey(
                        name: "FK_contacto_cuenta_cuenta_id",
                        column: x => x.cuenta_id,
                        principalSchema: "contactos",
                        principalTable: "cuenta",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "actividad",
                schema: "contactos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contacto_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<int>(type: "integer", nullable: false),
                    sentido = table.Column<int>(type: "integer", nullable: false),
                    cuerpo = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    resultado = table.Column<int>(type: "integer", nullable: true),
                    autor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ocurrida_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_actividad", x => x.id);
                    table.ForeignKey(
                        name: "FK_actividad_contacto_contacto_id",
                        column: x => x.contacto_id,
                        principalSchema: "contactos",
                        principalTable: "contacto",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_actividad_contacto_id",
                schema: "contactos",
                table: "actividad",
                column: "contacto_id");

            migrationBuilder.CreateIndex(
                name: "ix_actividad_empresa_contacto_fecha",
                schema: "contactos",
                table: "actividad",
                columns: new[] { "empresa_id", "contacto_id", "ocurrida_en" });

            migrationBuilder.CreateIndex(
                name: "IX_contacto_cuenta_id",
                schema: "contactos",
                table: "contacto",
                column: "cuenta_id");

            migrationBuilder.CreateIndex(
                name: "ix_contacto_empresa_email",
                schema: "contactos",
                table: "contacto",
                columns: new[] { "empresa_id", "email" });

            migrationBuilder.CreateIndex(
                name: "ix_contacto_empresa_telefono",
                schema: "contactos",
                table: "contacto",
                columns: new[] { "empresa_id", "telefono" });

            migrationBuilder.CreateIndex(
                name: "ix_cuenta_empresa_nombre",
                schema: "contactos",
                table: "cuenta",
                columns: new[] { "empresa_id", "nombre" });

            // Segunda barrera del aislamiento entre empresas, por debajo del filtro global de EF:
            // aunque una consulta se escapara del filtro, PostgreSQL no devolvería filas de otra
            // empresa. FORCE hace que la política también se aplique al dueño de la tabla.
            //
            // Aviso: un rol SUPERUSER (o con BYPASSRLS) se salta las políticas. En producción la
            // aplicación debe conectarse con un rol normal; con el usuario `postgres` de un equipo
            // de desarrollo, la barrera efectiva es la de EF Core.
            foreach (var tabla in new[] { "cuenta", "contacto", "actividad" })
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE contactos.{tabla} ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE contactos.{tabla} FORCE ROW LEVEL SECURITY;
                    CREATE POLICY aislamiento_empresa ON contactos.{tabla}
                        USING (empresa_id::text = current_setting('app.empresa_actual', true))
                        WITH CHECK (empresa_id::text = current_setting('app.empresa_actual', true));
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var tabla in new[] { "cuenta", "contacto", "actividad" })
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS aislamiento_empresa ON contactos.{tabla};");
                migrationBuilder.Sql($"ALTER TABLE contactos.{tabla} DISABLE ROW LEVEL SECURITY;");
            }

            migrationBuilder.DropTable(
                name: "actividad",
                schema: "contactos");

            migrationBuilder.DropTable(
                name: "contacto",
                schema: "contactos");

            migrationBuilder.DropTable(
                name: "cuenta",
                schema: "contactos");
        }
    }
}
