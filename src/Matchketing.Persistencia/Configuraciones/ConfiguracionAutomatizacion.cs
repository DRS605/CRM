using System.Text.Json;
using Matchketing.Automatizacion.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Matchketing.Persistencia.Configuraciones;

public sealed class ConfiguracionRegla : IEntityTypeConfiguration<Regla>
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public void Configure(EntityTypeBuilder<Regla> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("regla", "automatizacion");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).HasColumnName("id");
        b.Property(r => r.EmpresaId).HasColumnName("empresa_id");
        b.Property(r => r.Nombre).HasColumnName("nombre").HasMaxLength(Regla.LongitudMaximaNombre).IsRequired();
        b.Property(r => r.Disparador).HasColumnName("disparador").HasConversion<int>();
        b.Property(r => r.Activa).HasColumnName("activa");
        b.Property(r => r.CreadaEn).HasColumnName("creada_en");
        b.Property(r => r.UltimaVezEn).HasColumnName("ultima_vez_en");
        b.Property(r => r.Veces).HasColumnName("veces");

        // Las condiciones y las acciones van en **una columna JSON cada una**, no en dos tablas hijas.
        //
        // Son como mucho tres y cuatro filas por regla, nunca se consultan por separado —una condición
        // sola no significa nada— y siempre se leen con su regla. Dos tablas más serían dos migraciones,
        // dos políticas de RLS, dos `Include` en cada lectura y un `JOIN` en el camino caliente, para
        // guardar siete filas. Lo que sí se pierde es poder preguntar «qué reglas usan esta plantilla»
        // en SQL; el día que haga falta, esto es lo primero que hay que cambiar.
        Guardar<Condicion>(b, "condiciones", Regla.MaximoCondiciones);
        Guardar<Accion>(b, "acciones", Regla.MaximoAcciones);

        b.Ignore(r => r.Condiciones);
        b.Ignore(r => r.Acciones);
        b.Ignore(r => r.Eventos);

        b.HasIndex(r => new { r.EmpresaId, r.Activa, r.Disparador }).HasDatabaseName("ix_regla_activas");
    }

    /// <summary>
    /// Enlaza el campo privado con una columna de texto JSON.
    ///
    /// El comparador no es opcional: sin él EF compara la lista por referencia, y como es siempre la misma
    /// instancia, cambiar las condiciones de una regla no se detectaría y el `UPDATE` no se emitiría. Es
    /// un fallo silencioso que solo se ve en producción.
    /// </summary>
    private static void Guardar<T>(EntityTypeBuilder<Regla> b, string campo, int cuantos)
    {
        b.Property<List<T>>(campo)
            .HasColumnName(campo)
            .IsRequired()
            .HasConversion(
                new ValueConverter<List<T>, string>(
                    v => JsonSerializer.Serialize(v, Json),
                    v => JsonSerializer.Deserialize<List<T>>(v, Json) ?? new List<T>()),
                new ValueComparer<List<T>>(
                    (a, x) => a != null && x != null && a.SequenceEqual(x),
                    v => v.Aggregate(0, (acumulado, t) => HashCode.Combine(acumulado, t!.GetHashCode())),
                    v => v.ToList()));
    }
}

public sealed class ConfiguracionEjecucion : IEntityTypeConfiguration<Ejecucion>
{
    public void Configure(EntityTypeBuilder<Ejecucion> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("ejecucion", "automatizacion");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id");
        b.Property(e => e.EmpresaId).HasColumnName("empresa_id");
        b.Property(e => e.ReglaId).HasColumnName("regla_id");
        b.Property(e => e.SujetoId).HasColumnName("sujeto_id");
        b.Property(e => e.ContactoId).HasColumnName("contacto_id");
        b.Property(e => e.QueHizo).HasColumnName("que_hizo").HasMaxLength(600).IsRequired();
        b.Property(e => e.CuandoEn).HasColumnName("cuando_en");

        // **La garantía de «una sola vez por sujeto», y está aquí y no en un `if`.**
        //
        // Un `if` antes de insertar no protege de dos procesos guardando a la vez, y el precio de
        // equivocarse es mandar dos correos o crear dos tareas, que no se puede deshacer. El índice único
        // sí lo garantiza: el segundo INSERT falla y no hay segundo correo.
        b.HasIndex(e => new { e.ReglaId, e.SujetoId }).IsUnique().HasDatabaseName("ix_ejecucion_una_vez");
        b.HasIndex(e => new { e.ReglaId, e.CuandoEn }).HasDatabaseName("ix_ejecucion_regla");

        b.Ignore(e => e.Eventos);
    }
}
