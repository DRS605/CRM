using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchketing.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Tareas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tareas");

            migrationBuilder.CreateTable(
                name: "tarea",
                schema: "tareas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    contacto_id = table.Column<Guid>(type: "uuid", nullable: true),
                    oportunidad_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vence_el = table.Column<DateOnly>(type: "date", nullable: false),
                    responsable_id = table.Column<Guid>(type: "uuid", nullable: true),
                    origen = table.Column<int>(type: "integer", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    veces_aplazada = table.Column<int>(type: "integer", nullable: false),
                    cerrada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tarea", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tarea_empresa_contacto",
                schema: "tareas",
                table: "tarea",
                columns: new[] { "empresa_id", "contacto_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tarea_empresa_estado_vence",
                schema: "tareas",
                table: "tarea",
                columns: new[] { "empresa_id", "estado", "vence_el" });

            migrationBuilder.Sql("""
                ALTER TABLE tareas.tarea ENABLE ROW LEVEL SECURITY;
                ALTER TABLE tareas.tarea FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_empresa ON tareas.tarea
                    USING (empresa_id::text = current_setting('app.empresa_actual', true))
                    WITH CHECK (empresa_id::text = current_setting('app.empresa_actual', true));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS aislamiento_empresa ON tareas.tarea;");
            migrationBuilder.Sql("ALTER TABLE tareas.tarea DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "tarea",
                schema: "tareas");
        }
    }
}
