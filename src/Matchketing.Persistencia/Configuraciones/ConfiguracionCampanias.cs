using Matchketing.Campanias.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchketing.Persistencia.Configuraciones;

public sealed class ConfiguracionSegmento : IEntityTypeConfiguration<Segmento>
{
    public void Configure(EntityTypeBuilder<Segmento> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("segmento", "campania");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.EmpresaId).HasColumnName("empresa_id");
        b.Property(s => s.Nombre).HasColumnName("nombre").HasMaxLength(Segmento.LongitudMaximaNombre).IsRequired();
        b.Property(s => s.CreadoEn).HasColumnName("creado_en");
        b.Property(s => s.ActualizadoEn).HasColumnName("actualizado_en");

        // Los criterios van en **columnas y no en un jsonb**. Un jsonb habría sido menos código, y habría
        // costado justo lo que hace falta: la consulta que resuelve un segmento filtra por estos valores,
        // y filtrar dentro de un jsonb no usa índice ni deja que el compilador avise si mañana se
        // renombra un criterio. Seis columnas nulables son seis columnas nulables.
        b.OwnsOne(s => s.Criterios, c =>
        {
            c.Property(x => x.Estado).HasColumnName("estado").HasConversion<int?>();
            c.Property(x => x.Provincia).HasColumnName("provincia").HasMaxLength(CriteriosSegmento.LongitudMaximaTexto);
            c.Property(x => x.Origen).HasColumnName("origen").HasMaxLength(CriteriosSegmento.LongitudMaximaTexto);
            c.Property(x => x.MatchMinimo).HasColumnName("match_minimo");
            c.Property(x => x.SinActividadDias).HasColumnName("sin_actividad_dias");
            c.Property(x => x.EtapaId).HasColumnName("etapa_id");
        });

        b.HasIndex(s => new { s.EmpresaId, s.Nombre }).HasDatabaseName("ix_segmento_empresa_nombre");

        b.Ignore(s => s.Eventos);
    }
}

public sealed class ConfiguracionCampania : IEntityTypeConfiguration<Campania>
{
    public void Configure(EntityTypeBuilder<Campania> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("campania", "campania");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).HasColumnName("id");
        b.Property(c => c.EmpresaId).HasColumnName("empresa_id");
        b.Property(c => c.Nombre).HasColumnName("nombre").HasMaxLength(Campania.LongitudMaximaNombre).IsRequired();
        b.Property(c => c.SegmentoId).HasColumnName("segmento_id");
        b.Property(c => c.PlantillaId).HasColumnName("plantilla_id");
        b.Property(c => c.Estado).HasColumnName("estado").HasConversion<int>();
        b.Property(c => c.CreadaEn).HasColumnName("creada_en");
        b.Property(c => c.LanzadaEn).HasColumnName("lanzada_en");
        b.Property(c => c.LanzadaPor).HasColumnName("lanzada_por");
        b.Property(c => c.TerminadaEn).HasColumnName("terminada_en");
        b.Property(c => c.Destinatarios).HasColumnName("destinatarios");
        b.Property(c => c.Encolados).HasColumnName("encolados");
        b.Property(c => c.Excluidos).HasColumnName("excluidos");
        b.Property(c => c.SegmentoAlLanzar).HasColumnName("segmento_al_lanzar").HasMaxLength(400);

        // `Pendientes`, `EsBorrador` y `Cerrada` se calculan de lo demás. Guardarlos sería tener dos
        // fuentes para el mismo hecho, y la que se desincroniza es siempre la guardada.
        b.Ignore(c => c.Pendientes);
        b.Ignore(c => c.EsBorrador);
        b.Ignore(c => c.Cerrada);

        b.HasIndex(c => new { c.EmpresaId, c.Estado }).HasDatabaseName("ix_campania_empresa_estado");
        b.HasIndex(c => c.SegmentoId).HasDatabaseName("ix_campania_segmento");

        b.Ignore(c => c.Eventos);
    }
}

public sealed class ConfiguracionEnvioCampania : IEntityTypeConfiguration<EnvioCampania>
{
    public void Configure(EntityTypeBuilder<EnvioCampania> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("envio", "campania");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id");
        b.Property(e => e.EmpresaId).HasColumnName("empresa_id");
        b.Property(e => e.CampaniaId).HasColumnName("campania_id");
        b.Property(e => e.ContactoId).HasColumnName("contacto_id");
        b.Property(e => e.Estado).HasColumnName("estado").HasConversion<int>();
        b.Property(e => e.Motivo).HasColumnName("motivo").HasMaxLength(EnvioCampania.LongitudMaximaMotivo);
        b.Property(e => e.CorreoId).HasColumnName("correo_id");
        b.Property(e => e.ResueltoEn).HasColumnName("resuelto_en");

        // El índice del trabajo que va encolando: los pendientes de una campaña, cada minuto.
        b.HasIndex(e => new { e.CampaniaId, e.Estado }).HasDatabaseName("ix_envio_campania_estado");

        // Y este es una regla, no una optimización: **una persona no puede estar dos veces en la misma
        // campaña**. La guarda del dominio evita el doble encolado si dos pasadas se solapan; esta evita
        // que se escriba la fila repetida, que es el otro camino al mismo correo duplicado.
        b.HasIndex(e => new { e.CampaniaId, e.ContactoId }).IsUnique().HasDatabaseName("ix_envio_unico");

        b.Ignore(e => e.Eventos);
    }
}
