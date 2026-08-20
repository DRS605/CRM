using Matchketing.Correo.Dominio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Matchketing.Persistencia.Configuraciones;

public sealed class ConfiguracionPlantilla : IEntityTypeConfiguration<Plantilla>
{
    public void Configure(EntityTypeBuilder<Plantilla> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("plantilla", "correo");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).HasColumnName("id");
        b.Property(p => p.EmpresaId).HasColumnName("empresa_id");
        b.Property(p => p.Nombre).HasColumnName("nombre").HasMaxLength(Plantilla.LongitudMaximaNombre).IsRequired();
        b.Property(p => p.Asunto).HasColumnName("asunto").HasMaxLength(Plantilla.LongitudMaximaAsunto).IsRequired();
        b.Property(p => p.Cuerpo).HasColumnName("cuerpo").HasMaxLength(Plantilla.LongitudMaximaCuerpo).IsRequired();
        b.Property(p => p.ParaQue).HasColumnName("para_que").HasConversion<int>();
        b.Property(p => p.Usos).HasColumnName("usos");
        b.Property(p => p.CreadaEn).HasColumnName("creada_en");

        b.HasIndex(p => new { p.EmpresaId, p.Nombre }).HasDatabaseName("ix_plantilla_empresa_nombre");

        b.Ignore(p => p.Eventos);
    }
}

public sealed class ConfiguracionCorreoEnviado : IEntityTypeConfiguration<Matchketing.Correo.Dominio.Correo>
{
    public void Configure(EntityTypeBuilder<Matchketing.Correo.Dominio.Correo> b)
    {
        ArgumentNullException.ThrowIfNull(b);

        b.ToTable("mensaje", "correo");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).HasColumnName("id");
        b.Property(c => c.EmpresaId).HasColumnName("empresa_id");
        b.Property(c => c.ContactoId).HasColumnName("contacto_id");
        b.Property(c => c.UsuarioId).HasColumnName("usuario_id");
        b.Property(c => c.Para).HasColumnName("para").HasMaxLength(320).IsRequired();
        b.Property(c => c.Asunto).HasColumnName("asunto").HasMaxLength(Matchketing.Correo.Dominio.Correo.LongitudMaximaAsunto).IsRequired();
        b.Property(c => c.Cuerpo).HasColumnName("cuerpo").IsRequired();
        b.Property(c => c.ParaQue).HasColumnName("para_que").HasConversion<int>();
        b.Property(c => c.PlantillaId).HasColumnName("plantilla_id");
        b.Property(c => c.Estado).HasColumnName("estado").HasConversion<int>();
        b.Property(c => c.Intentos).HasColumnName("intentos");
        b.Property(c => c.CreadoEn).HasColumnName("creado_en");
        b.Property(c => c.ProximoIntentoEn).HasColumnName("proximo_intento_en");
        b.Property(c => c.EnviadoEn).HasColumnName("enviado_en");
        b.Property(c => c.UltimoFallo).HasColumnName("ultimo_fallo").HasMaxLength(300);
        b.Property(c => c.TokenApertura).HasColumnName("token_apertura").HasMaxLength(64).IsRequired();
        b.Property(c => c.PrimeraAperturaEn).HasColumnName("primera_apertura_en");
        b.Property(c => c.UltimaAperturaEn).HasColumnName("ultima_apertura_en");
        b.Property(c => c.Aperturas).HasColumnName("aperturas");

        // Único en todo el sistema: es lo que va en la URL del píxel, y esa petición llega **sin
        // sesión**, así que se busca por el token y por nada más.
        b.HasIndex(c => c.TokenApertura).IsUnique().HasDatabaseName("ix_mensaje_token");

        // El índice del trabajo de envío: cada minuto, y solo le interesan los que ya les toca.
        b.HasIndex(c => new { c.Estado, c.ProximoIntentoEn }).HasDatabaseName("ix_mensaje_pendientes");
        b.HasIndex(c => new { c.ContactoId, c.CreadoEn }).HasDatabaseName("ix_mensaje_contacto");

        b.Ignore(c => c.Eventos);
    }
}
