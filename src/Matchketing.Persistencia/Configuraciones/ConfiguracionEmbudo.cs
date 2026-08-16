using Matchketing.Embudo.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchketing.Persistencia.Configuraciones;

public sealed class ConfiguracionEmbudo : IEntityTypeConfiguration<Embudo.Dominio.Embudo>
{
    public void Configure(EntityTypeBuilder<Embudo.Dominio.Embudo> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("embudo", "embudo");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id");
        b.Property(e => e.EmpresaId).HasColumnName("empresa_id");
        b.Property(e => e.Nombre).HasColumnName("nombre").HasMaxLength(Embudo.Dominio.Embudo.LongitudMaximaNombre).IsRequired();
        b.Property(e => e.PorDefecto).HasColumnName("por_defecto");
        b.Property(e => e.CreadoEn).HasColumnName("creado_en");
        b.HasIndex(e => new { e.EmpresaId, e.PorDefecto }).HasDatabaseName("ix_embudo_empresa_defecto");

        // Las etapas son parte del agregado: se cargan y se guardan con él.
        b.HasMany(e => e.Etapas).WithOne().HasForeignKey(x => x.EmbudoId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(e => e.Etapas).HasField("etapas").UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(e => e.Eventos);
    }
}

public sealed class ConfiguracionEtapa : IEntityTypeConfiguration<Etapa>
{
    public void Configure(EntityTypeBuilder<Etapa> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("etapa", "embudo");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).HasColumnName("id");
        b.Property(e => e.EmbudoId).HasColumnName("embudo_id");
        b.Property(e => e.Nombre).HasColumnName("nombre").HasMaxLength(Etapa.LongitudMaximaNombre).IsRequired();
        b.Property(e => e.Orden).HasColumnName("orden");
        b.Property(e => e.Probabilidad).HasColumnName("probabilidad");
        b.Property(e => e.DiasAviso).HasColumnName("dias_aviso");
        b.HasIndex(e => new { e.EmbudoId, e.Orden }).HasDatabaseName("ix_etapa_embudo_orden");
    }
}

public sealed class ConfiguracionOportunidad : IEntityTypeConfiguration<Oportunidad>
{
    public void Configure(EntityTypeBuilder<Oportunidad> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("oportunidad", "embudo");
        b.HasKey(o => o.Id);
        b.Property(o => o.Id).HasColumnName("id");
        b.Property(o => o.EmpresaId).HasColumnName("empresa_id");
        b.Property(o => o.ContactoId).HasColumnName("contacto_id");
        b.Property(o => o.CuentaId).HasColumnName("cuenta_id");
        b.Property(o => o.Titulo).HasColumnName("titulo").HasMaxLength(Oportunidad.LongitudMaximaTitulo).IsRequired();
        b.Property(o => o.Importe).HasColumnName("importe").HasPrecision(14, 2);
        b.Property(o => o.EmbudoId).HasColumnName("embudo_id");
        b.Property(o => o.EtapaId).HasColumnName("etapa_id");
        b.Property(o => o.EntroEnEtapaEn).HasColumnName("entro_en_etapa_en");
        b.Property(o => o.PrevistaCierre).HasColumnName("prevista_cierre");
        b.Property(o => o.PropietarioId).HasColumnName("propietario_id");
        b.Property(o => o.Motivo).HasColumnName("motivo").HasConversion<int?>();
        b.Property(o => o.DetalleMotivo).HasColumnName("detalle_motivo").HasMaxLength(Oportunidad.LongitudMaximaDetalle);
        b.Property(o => o.CerradaEn).HasColumnName("cerrada_en");
        b.Property(o => o.CreadoEn).HasColumnName("creado_en");
        b.Property(o => o.ActualizadoEn).HasColumnName("actualizado_en");

        // El estado se deriva de CerradaEn y Motivo (O2): no hay columna que pueda descuadrarse.
        b.Ignore(o => o.Estado);
        b.Ignore(o => o.Eventos);

        b.HasIndex(o => new { o.EmpresaId, o.EtapaId }).HasDatabaseName("ix_oportunidad_empresa_etapa");
        b.HasIndex(o => new { o.EmpresaId, o.ContactoId }).HasDatabaseName("ix_oportunidad_empresa_contacto");
        b.HasIndex(o => new { o.EmpresaId, o.CerradaEn }).HasDatabaseName("ix_oportunidad_empresa_cerrada");
        b.HasOne<Etapa>().WithMany().HasForeignKey(o => o.EtapaId).OnDelete(DeleteBehavior.Restrict);
    }
}
