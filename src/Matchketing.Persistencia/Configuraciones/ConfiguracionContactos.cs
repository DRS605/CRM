using Matchketing.Contactos.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchketing.Persistencia.Configuraciones;

public sealed class ConfiguracionCuenta : IEntityTypeConfiguration<Cuenta>
{
    public void Configure(EntityTypeBuilder<Cuenta> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("cuenta", "contactos");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).HasColumnName("id");
        b.Property(c => c.EmpresaId).HasColumnName("empresa_id");
        b.Property(c => c.Nombre).HasColumnName("nombre").HasMaxLength(Cuenta.LongitudMaximaNombre).IsRequired();
        b.Property(c => c.Nif).HasColumnName("nif").HasMaxLength(20);
        b.Property(c => c.Sector).HasColumnName("sector").HasMaxLength(80);
        b.Property(c => c.Provincia).HasColumnName("provincia").HasMaxLength(60);
        b.Property(c => c.Tamano).HasColumnName("tamano");
        b.Property(c => c.Web).HasColumnName("web").HasMaxLength(200);
        b.Property(c => c.Activa).HasColumnName("activa");
        b.Property(c => c.CreadoEn).HasColumnName("creado_en");
        b.Property(c => c.ActualizadoEn).HasColumnName("actualizado_en");
        b.HasIndex(c => new { c.EmpresaId, c.Nombre }).HasDatabaseName("ix_cuenta_empresa_nombre");
        b.Ignore(c => c.Eventos);
    }
}

public sealed class ConfiguracionContacto : IEntityTypeConfiguration<Contacto>
{
    public void Configure(EntityTypeBuilder<Contacto> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("contacto", "contactos");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).HasColumnName("id");
        b.Property(c => c.EmpresaId).HasColumnName("empresa_id");
        b.Property(c => c.Nombre).HasColumnName("nombre").HasMaxLength(Contacto.LongitudMaximaNombre).IsRequired();
        b.Property(c => c.Email).HasColumnName("email").HasMaxLength(254);
        b.Property(c => c.Telefono).HasColumnName("telefono").HasMaxLength(20);
        b.Property(c => c.Cargo).HasColumnName("cargo").HasMaxLength(Contacto.LongitudMaximaCargo);
        b.Property(c => c.CuentaId).HasColumnName("cuenta_id");
        b.Property(c => c.Origen).HasColumnName("origen").HasMaxLength(Contacto.LongitudMaximaOrigen).IsRequired();
        b.Property(c => c.PropietarioId).HasColumnName("propietario_id");
        b.Property(c => c.Estado).HasColumnName("estado").HasConversion<int>();
        b.Property(c => c.Activo).HasColumnName("activo");
        b.Property(c => c.FusionadoEnId).HasColumnName("fusionado_en_id");
        b.Property(c => c.CreadoEn).HasColumnName("creado_en");
        b.Property(c => c.ActualizadoEn).HasColumnName("actualizado_en");

        // Índices de deduplicación: no son únicos a propósito. Un duplicado se detecta y se
        // propone, no se rechaza; rechazarlo obligaría al usuario a resolverlo antes de guardar.
        b.HasIndex(c => new { c.EmpresaId, c.Email }).HasDatabaseName("ix_contacto_empresa_email");
        b.HasIndex(c => new { c.EmpresaId, c.Telefono }).HasDatabaseName("ix_contacto_empresa_telefono");

        b.HasOne<Cuenta>().WithMany().HasForeignKey(c => c.CuentaId).OnDelete(DeleteBehavior.SetNull);
        b.Ignore(c => c.Eventos);
    }
}

public sealed class ConfiguracionActividad : IEntityTypeConfiguration<Actividad>
{
    public void Configure(EntityTypeBuilder<Actividad> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("actividad", "contactos");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).HasColumnName("id");
        b.Property(a => a.EmpresaId).HasColumnName("empresa_id");
        b.Property(a => a.ContactoId).HasColumnName("contacto_id");
        b.Property(a => a.Tipo).HasColumnName("tipo").HasConversion<int>();
        b.Property(a => a.Sentido).HasColumnName("sentido").HasConversion<int>();
        b.Property(a => a.Cuerpo).HasColumnName("cuerpo").HasMaxLength(Actividad.LongitudMaximaCuerpo).IsRequired();
        b.Property(a => a.Resultado).HasColumnName("resultado").HasConversion<int?>();
        b.Property(a => a.AutorId).HasColumnName("autor_id");
        b.Property(a => a.OcurridaEn).HasColumnName("ocurrida_en");
        b.HasIndex(a => new { a.EmpresaId, a.ContactoId, a.OcurridaEn }).HasDatabaseName("ix_actividad_empresa_contacto_fecha");
        b.HasOne<Contacto>().WithMany().HasForeignKey(a => a.ContactoId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(a => a.Eventos);
    }
}
