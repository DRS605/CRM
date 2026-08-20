using Matchketing.Webhooks.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Matchketing.Persistencia.Configuraciones;

public sealed class ConfiguracionSuscripcionWebhook : IEntityTypeConfiguration<SuscripcionWebhook>
{
    public void Configure(EntityTypeBuilder<SuscripcionWebhook> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("suscripcion", "webhooks");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.EmpresaId).HasColumnName("empresa_id");
        b.Property(s => s.Url).HasColumnName("url").HasMaxLength(SuscripcionWebhook.LongitudMaximaUrl).IsRequired();
        b.Property(s => s.Secreto).HasColumnName("secreto").HasMaxLength(80).IsRequired();
        b.Property(s => s.Descripcion).HasColumnName("descripcion").HasMaxLength(160).IsRequired();
        b.Property(s => s.Activa).HasColumnName("activa");
        b.Property(s => s.MotivoApagado).HasColumnName("motivo_apagado").HasMaxLength(300);
        b.Property(s => s.FallosSeguidos).HasColumnName("fallos_seguidos");
        b.Property(s => s.CreadaEn).HasColumnName("creada_en");
        b.Property(s => s.UltimaEntregaEn).HasColumnName("ultima_entrega_en");

        // Los tipos van en **una columna de texto separada por comas**, no en una tabla aparte. Son
        // cinco valores como mucho, nunca se consultan por separado, y una tabla de unión costaría un
        // `JOIN` en el camino caliente: el de cada cambio de negocio que puede emitir un evento.
        //
        // Y se guardan con su nombre público («oportunidad.ganada») en vez de con el número. Ocupa más,
        // pero cuando un webhook no dispara lo primero que se hace es mirar la fila, y `3,5` obliga a
        // ir a buscar el enumerado a mano. El nombre ya es contrato público, así que no va a cambiar.
        b.Property<List<TipoEvento>>("tipos")
            .HasColumnName("tipos")
            .HasMaxLength(200)
            .IsRequired()
            .HasConversion(
                new ValueConverter<List<TipoEvento>, string>(
                    v => string.Join(',', v.Select(TiposEvento.Texto)),
                    v => Interpretar(v)),

                // Sin comparador, EF compara la lista por referencia: como es la misma instancia
                // siempre, cambiar los eventos de una suscripción no se detectaría y el `UPDATE` no se
                // emitiría. Es un fallo silencioso de los que solo se ven en producción.
                new ValueComparer<List<TipoEvento>>(
                    (a, x) => a != null && x != null && a.SequenceEqual(x),
                    v => v.Aggregate(0, (acumulado, t) => HashCode.Combine(acumulado, t.GetHashCode())),
                    v => v.ToList()));

        b.Ignore(s => s.Tipos);
        b.Ignore(s => s.Eventos);

        b.HasIndex(s => new { s.EmpresaId, s.Activa }).HasDatabaseName("ix_webhook_empresa_activa");
    }

    /// <summary>
    /// Un nombre que no se reconozca se descarta en vez de reventar al leer la fila. Solo puede pasar
    /// si alguien tocó la columna a mano o si se retirase un evento del catálogo, y en los dos casos es
    /// mejor una suscripción con un evento de menos que una empresa que no puede abrir Ajustes.
    /// </summary>
    private static List<TipoEvento> Interpretar(string guardado) =>
        guardado.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(TiposEvento.De)
            .Where(t => t is not null)
            .Select(t => t!.Value)
            .ToList();
}

public sealed class ConfiguracionEntregaWebhook : IEntityTypeConfiguration<Entrega>
{
    public void Configure(EntityTypeBuilder<Entrega> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("entrega", "webhooks");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id");
        b.Property(e => e.EmpresaId).HasColumnName("empresa_id");
        b.Property(e => e.SuscripcionId).HasColumnName("suscripcion_id");
        b.Property(e => e.Tipo).HasColumnName("tipo").HasConversion<int>();
        b.Property(e => e.Cuerpo).HasColumnName("cuerpo").IsRequired();
        b.Property(e => e.Estado).HasColumnName("estado").HasConversion<int>();
        b.Property(e => e.Intentos).HasColumnName("intentos");
        b.Property(e => e.CreadaEn).HasColumnName("creada_en");
        b.Property(e => e.ProximoIntentoEn).HasColumnName("proximo_intento_en");
        b.Property(e => e.EntregadaEn).HasColumnName("entregada_en");
        b.Property(e => e.UltimoCodigo).HasColumnName("ultimo_codigo");
        b.Property(e => e.UltimoFallo).HasColumnName("ultimo_fallo").HasMaxLength(300);

        // El índice del trabajo de entrega: sale cada minuto y solo le interesan las pendientes que
        // ya les toca. Sin él, esa consulta acabaría leyendo la tabla entera —que es la que más crece
        // de todo el sistema— una vez por minuto y para siempre.
        b.HasIndex(e => new { e.Estado, e.ProximoIntentoEn }).HasDatabaseName("ix_entrega_pendientes");
        b.HasIndex(e => new { e.SuscripcionId, e.CreadaEn }).HasDatabaseName("ix_entrega_suscripcion");

        b.Ignore(e => e.Eventos);
    }
}
