using System.Security.Cryptography;
using FluentAssertions;
using Matchketing.Avisos.Dominio;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Resultados;
using Xunit;

namespace Matchketing.Avisos.Tests;

/// <summary>
/// El cifrado de un aviso push, comprobado contra una salida conocida.
///
/// **De dónde sale el vector.** Un servicio de push real no se puede llamar desde aquí, así que la
/// única forma honesta de saber si esto cifra bien era cifrar con entradas fijas y que **otra
/// implementación lo descifrara**. Se hizo con `http_ece` de npm —la librería que usa media internet
/// para esto— sobre Node: se generaron las claves ahí, se cifró en C# con esas mismas claves y esa
/// misma sal, y Node recuperó el texto original.
///
/// El resultado de aquella prueba está pegado abajo. No es un adorno: si alguien invierte el orden de
/// las dos claves públicas en el `info`, o se deja el 0x02 del final, o cambia el nonce, esto se pone
/// rojo. Y esos tres fallos **no dan ningún error en producción**: el aviso simplemente no llega, y no
/// hay forma de enterarse.
/// </summary>
public sealed class PruebasCifradoWebPush
{
    private const string UaPublica = "BM6oFunqnW-q5Rz-laNO3Mao2nF9eQ7cLPaW6ltwuhLqSdgz0awOs05RnQPmw-Koucpiqg71PjrZVmLkxjujuuU";
    private const string Auth = "v96B8cq6_hyHop4iU0iZKg";
    private const string AsPrivada = "gTVHQg_qHeioVnM4_UTOfebm-0RuVf1pfOzmINwAPjk";
    private const string AsPublica = "BJuLOs50oycgCnV_RdJiRpI2W2lyGje_iPvSMZ5mtAp__gUIA9YvpcvKdfSrFg5vPDgCzWw0qU8u-uLNxOMKVh0";
    private const string Sal = "g6be4vJZkmPjfyLBJQSiLQ";
    private const string Mensaje = "Te quedan 11 decisiones del repaso.";

    /// <summary>Lo que descifró correctamente `http_ece` sobre Node con esas mismas entradas.</summary>
    private const string CuerpoEsperado =
        "g6be4vJZkmPjfyLBJQSiLQAAEABBBJuLOs50oycgCnV_RdJiRpI2W2lyGje_iPvSMZ5mtAp__gUIA9YvpcvKdfSr" +
        "Fg5vPDgCzWw0qU8u-uLNxOMKVh2ydj_0YnBT9go9gFc0z_Pc4xVheFAMg5JJbk6qQgtzjLCwbmDBwasWAMKfnP2b8b5zs1BR";

    private static Resultado<byte[]> Cifrar(string mensaje = Mensaje, string publica = UaPublica, string auth = Auth)
    {
        using var efimera = CifradoWebPush.ClaveDe(AsPrivada, AsPublica);
        return CifradoWebPush.Cifrar(mensaje, publica, auth, Base64Url.Descodificar(Sal), efimera);
    }

    [Fact]
    public void Con_las_mismas_entradas_produce_el_cuerpo_que_descifra_otra_implementacion()
    {
        var cuerpo = Cifrar();

        cuerpo.Exito.Should().BeTrue();
        Base64Url.Codificar(cuerpo.Valor).Should().Be(CuerpoEsperado);
    }

    [Fact]
    public void La_cabecera_lleva_la_sal_el_tamano_de_registro_y_la_clave_del_servidor()
    {
        // El navegador lee estos 86 bytes para saber con qué descifrar. Un byte fuera de sitio y el
        // mensaje se descarta sin decir nada.
        var cuerpo = Cifrar().Valor;

        cuerpo[..16].Should().Equal(Base64Url.Descodificar(Sal), "los 16 primeros bytes son la sal");
        cuerpo[16..20].Should().Equal([0x00, 0x00, 0x10, 0x00], "4096 en big-endian");
        cuerpo[20].Should().Be(65, "la longitud de la clave pública del servidor");
        cuerpo[21..86].Should().Equal(Base64Url.Descodificar(AsPublica));
    }

    [Fact]
    public void Cada_mensaje_lleva_su_propia_sal_y_su_propia_clave()
    {
        // Sin esto no hay cifrado que valga: repetir la sal con la misma clave rompe AES-GCM, y una
        // clave efímera que no cambia no es efímera.
        var uno = CifradoWebPush.Cifrar(Mensaje, UaPublica, Auth).Valor;
        var otro = CifradoWebPush.Cifrar(Mensaje, UaPublica, Auth).Valor;

        uno[..16].Should().NotEqual(otro[..16], "la sal");
        uno[21..86].Should().NotEqual(otro[21..86], "la clave del servidor");
    }

    [Fact]
    public void El_texto_cifrado_ocupa_un_byte_mas_por_el_delimitador_y_dieciseis_por_la_etiqueta()
    {
        var cuerpo = Cifrar("hola").Valor;

        // 86 de cabecera + 4 de texto + 1 del delimitador de último registro + 16 de la etiqueta GCM.
        cuerpo.Length.Should().Be(86 + 4 + 1 + 16);
    }

    [Theory]
    [InlineData(null, "suscripcion.p256dh_invalida")]
    [InlineData("", "suscripcion.p256dh_invalida")]
    [InlineData("no-es-base64!!", "suscripcion.p256dh_invalida")]
    [InlineData("QQ", "suscripcion.p256dh_invalida")]
    public void Una_clave_de_navegador_que_no_vale_se_rechaza(string? publica, string codigo)
    {
        CifradoWebPush.Cifrar(Mensaje, publica, Auth).Error!.Codigo.Should().Be(codigo);
    }

    [Fact]
    public void Un_punto_comprimido_se_rechaza()
    {
        // Los puntos comprimidos empiezan por 0x02 o 0x03. El navegador siempre manda el sin comprimir,
        // pero si algún día llega otra cosa es mejor decirlo que cifrar algo que nadie podrá leer.
        var comprimido = new byte[65];
        comprimido[0] = 0x02;

        CifradoWebPush.Cifrar(Mensaje, Base64Url.Codificar(comprimido), Auth)
            .Error!.Codigo.Should().Be("suscripcion.p256dh_invalida");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("demasiado-corto")]
    public void Un_secreto_de_autenticacion_que_no_mide_dieciseis_bytes_se_rechaza(string? auth)
    {
        CifradoWebPush.Cifrar(Mensaje, UaPublica, auth).Error!.Codigo.Should().Be("suscripcion.auth_invalida");
    }

    [Fact]
    public void Un_mensaje_demasiado_grande_se_rechaza_antes_de_cifrarlo()
    {
        // Muchos servicios devuelven 413 y el aviso se pierde. Es mejor no salir de casa.
        var largo = new string('a', CifradoWebPush.MaximoBytesMensaje + 1);

        CifradoWebPush.Cifrar(largo, UaPublica, Auth).Error!.Codigo.Should().Be("aviso.mensaje_largo");
    }

    [Fact]
    public void Cabe_un_aviso_de_tamano_normal()
    {
        var aviso = """{"titulo":"Tu repaso de la semana","cuerpo":"11 decisiones, unos dos minutos.","ruta":"/?ir=repaso"}""";

        CifradoWebPush.Cifrar(aviso, UaPublica, Auth).Exito.Should().BeTrue();
    }

    [Fact]
    public void La_clave_efimera_inyectada_no_se_descarta_por_dentro()
    {
        // Quien la pasa la posee. Si `Cifrar` la desechara, la segunda llamada explotaría, y eso solo
        // se vería en un bucle que mande dos avisos.
        using var efimera = CifradoWebPush.ClaveDe(AsPrivada, AsPublica);

        CifradoWebPush.Cifrar(Mensaje, UaPublica, Auth, null, efimera).Exito.Should().BeTrue();
        CifradoWebPush.Cifrar(Mensaje, UaPublica, Auth, null, efimera).Exito.Should().BeTrue();
    }
}
