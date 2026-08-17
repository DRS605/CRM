using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Matchketing.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Cumplimiento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "auditoria");

            migrationBuilder.AddColumn<int>(
                name: "meses_retencion_leads",
                schema: "organizacion",
                table: "empresa",
                type: "integer",
                nullable: false,
                defaultValue: 24);

            migrationBuilder.CreateTable(
                name: "registro",
                schema: "auditoria",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    empresa_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entidad = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    entidad_id = table.Column<Guid>(type: "uuid", nullable: true),
                    accion = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    detalle = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_registro", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_empresa_entidad",
                schema: "auditoria",
                table: "registro",
                columns: new[] { "empresa_id", "entidad_id" });

            migrationBuilder.CreateIndex(
                name: "ix_auditoria_empresa_fecha",
                schema: "auditoria",
                table: "registro",
                columns: new[] { "empresa_id", "en" });

            // Aislamiento entre empresas, igual que el resto de las tablas de datos.
            migrationBuilder.Sql("""
                ALTER TABLE auditoria.registro ENABLE ROW LEVEL SECURITY;
                ALTER TABLE auditoria.registro FORCE ROW LEVEL SECURITY;
                CREATE POLICY aislamiento_empresa ON auditoria.registro
                    USING (empresa_id::text = current_setting('app.empresa_actual', true))
                    WITH CHECK (empresa_id::text = current_setting('app.empresa_actual', true));
                """);

            // **Append-only en la base de datos, no solo en el código.**
            //
            // Que la entidad de C# no tenga forma de modificarse está muy bien mientras todo el mundo
            // pase por la entidad. Un `UPDATE auditoria.registro SET detalle = ...` desde una consola,
            // o un futuro caso de uso que use `ExecuteUpdate` sin pensarlo, se lo saltarían sin
            // enterarse. Y un registro de auditoría que se puede editar no es un registro de
            // auditoría: la regla se cumple donde no se puede eludir.
            //
            // Se deja fuera al **propietario de la tabla**, y a propósito: el borrado de una empresa
            // tiene que poder llevarse su auditoría, y las migraciones tienen que poder tocar la
            // tabla. La aplicación se conecta con un rol distinto y sin privilegios de propietario
            // (ver docs/despliegue.md), que es exactamente para quien está puesta la regla.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION auditoria.solo_anadir() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF pg_has_role(current_user, (
                            SELECT tableowner FROM pg_tables
                            WHERE schemaname = 'auditoria' AND tablename = 'registro'), 'MEMBER') THEN
                        RETURN COALESCE(NEW, OLD);
                    END IF;

                    RAISE EXCEPTION 'auditoria.registro solo admite INSERT: es un registro de auditoría.';
                END;
                $$;

                CREATE TRIGGER solo_anadir
                    BEFORE UPDATE OR DELETE ON auditoria.registro
                    FOR EACH ROW EXECUTE FUNCTION auditoria.solo_anadir();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS solo_anadir ON auditoria.registro;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS auditoria.solo_anadir();");
            migrationBuilder.Sql("DROP POLICY IF EXISTS aislamiento_empresa ON auditoria.registro;");

            migrationBuilder.DropTable(
                name: "registro",
                schema: "auditoria");

            migrationBuilder.DropColumn(
                name: "meses_retencion_leads",
                schema: "organizacion",
                table: "empresa");
        }
    }
}
