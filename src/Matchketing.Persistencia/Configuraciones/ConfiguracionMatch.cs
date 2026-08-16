using Matchketing.Match.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchketing.Persistencia.Configuraciones;

public sealed class ConfiguracionSenal : IEntityTypeConfiguration<Senal>
{
    public void Configure(EntityTypeBuilder<Senal> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("senal", "match");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");
        b.Property(s => s.EmpresaId).HasColumnName("empresa_id");
        b.Property(s => s.ContactoId).HasColumnName("contacto_id");
        b.Property(s => s.Tipo).HasColumnName("tipo").HasConversion<int>();
        b.Property(s => s.OcurridaEn).HasColumnName("ocurrida_en");
        b.HasIndex(s => new { s.EmpresaId, s.ContactoId, s.OcurridaEn }).HasDatabaseName("ix_senal_empresa_contacto_fecha");
        b.Ignore(s => s.Eventos);
    }
}

public sealed class ConfiguracionPuntuacionMatch : IEntityTypeConfiguration<PuntuacionMatch>
{
    public void Configure(EntityTypeBuilder<PuntuacionMatch> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("puntuacion_match", "match");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).HasColumnName("id");
        b.Property(p => p.EmpresaId).HasColumnName("empresa_id");
        b.Property(p => p.ContactoId).HasColumnName("contacto_id");
        b.Property(p => p.Match).HasColumnName("match");
        b.Property(p => p.Encaje).HasColumnName("encaje");
        b.Property(p => p.Momento).HasColumnName("momento");
        b.Property(p => p.Motivos).HasColumnName("motivos").HasMaxLength(1000);
        b.Property(p => p.SinHistorico).HasColumnName("sin_historico");
        b.Property(p => p.CalculadaEn).HasColumnName("calculada_en");
        b.HasIndex(p => new { p.EmpresaId, p.ContactoId }).IsUnique().HasDatabaseName("ix_puntuacion_empresa_contacto");
        b.Ignore(p => p.Eventos);
        b.Ignore(p => p.ListaMotivos);
    }
}
