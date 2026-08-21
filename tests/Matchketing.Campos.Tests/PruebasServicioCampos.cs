using FluentAssertions;
using Matchketing.Campos.Aplicacion;
using Matchketing.Campos.Dominio;
using Xunit;

namespace Matchketing.Campos.Tests;

public sealed class PruebasServicioCampos
{
    private static readonly Guid Empresa = Guid.NewGuid();

    private readonly RelojFijo reloj = new(new DateTimeOffset(2026, 8, 22, 10, 0, 0, TimeSpan.Zero));
    private readonly RepositorioEnMemoria repositorio = new();
    private readonly EntidadesDePrueba entidades = new();

    private readonly Guid contacto = Guid.NewGuid();
    private readonly Guid cuenta = Guid.NewGuid();

    public PruebasServicioCampos()
    {
        entidades.Contactos.Add(contacto);
        entidades.Cuentas.Add(cuenta);
    }

    private ServicioCampos Servicio(Guid? empresa = null) =>
        new(repositorio, entidades, new ContextoDePrueba(empresa ?? Empresa), reloj);

    private async Task<CampoPropio> Campo(
        string nombre, TipoCampo tipo = TipoCampo.Texto, IReadOnlyList<string>? opciones = null,
        Ambito ambito = Ambito.Contacto)
    {
        var r = await Servicio().CrearAsync(ambito, nombre, tipo, opciones);
        r.Exito.Should().BeTrue(string.Join(" ", r.Error?.Codigo, r.Error?.Mensaje));
        return r.Valor;
    }

    // ---------- La definición ----------

    [Fact]
    public async Task Los_campos_se_numeran_solos_en_el_orden_en_que_se_crean()
    {
        // Quien define un campo no quiere elegir su posición: lo pone y aparece al final. Reordenar es
        // otra operación y es opcional.
        var uno = await Campo("Nº de póliza");
        var dos = await Campo("Potencia contratada");

        uno.Orden.Should().Be(0);
        dos.Orden.Should().Be(1);
    }

    [Fact]
    public async Task El_orden_del_siguiente_sale_del_maximo_y_no_de_la_cuenta()
    {
        // Si saliera de la cuenta, borrar uno de en medio haría que el siguiente empatara con uno que ya
        // existe, y dos campos con el mismo orden se pintan en un orden que cambia de una recarga a otra.
        var uno = await Campo("Uno");
        var dos = await Campo("Dos");
        var tres = await Campo("Tres");

        (await Servicio().BorrarAsync(dos.Id)).Exito.Should().BeTrue();
        var cuatro = await Campo("Cuatro");

        cuatro.Orden.Should().Be(3);
        new[] { uno.Orden, tres.Orden, cuatro.Orden }.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Diez_campos_por_ambito_y_el_once_se_rechaza()
    {
        for (var i = 0; i < CampoPropio.MaximoPorAmbito; i++)
        {
            await Campo("Campo " + i);
        }

        var r = await Servicio().CrearAsync(Ambito.Contacto, "Uno más", TipoCampo.Texto, null);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("campo.demasiados");
        r.Error!.Mensaje.Should().Contain("quita uno", "hay que decir qué hacer, no solo que no se puede");
    }

    [Fact]
    public async Task El_tope_es_por_ambito_y_no_del_total()
    {
        // Diez en contactos y diez en cuentas. Compartir el tope habría hecho que definir campos de
        // contacto gastara los de cuenta, que no es lo que nadie espera de «diez por objeto».
        for (var i = 0; i < CampoPropio.MaximoPorAmbito; i++)
        {
            await Campo("Campo " + i);
        }

        (await Servicio().CrearAsync(Ambito.Cuenta, "Sector CNAE", TipoCampo.Texto, null))
            .Exito.Should().BeTrue();
    }

    [Fact]
    public async Task Dos_campos_con_la_misma_clave_no_caben_aunque_el_nombre_sea_distinto()
    {
        // «Nº de póliza» y «N de poliza» son nombres distintos y la misma clave. Dejarlos pasar daría dos
        // columnas iguales en el CSV y dos filas casi iguales en la ficha.
        await Campo("Nº de póliza");

        var r = await Servicio().CrearAsync(Ambito.Contacto, "N de poliza", TipoCampo.Texto, null);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("campo.repetido");
    }

    [Fact]
    public async Task La_misma_clave_en_los_dos_ambitos_si_cabe()
    {
        // «Provincia» de un contacto y «Provincia» de una cuenta son dos datos distintos, y son dos
        // pantallas distintas: no hay ambigüedad posible.
        await Campo("Provincia");

        (await Servicio().CrearAsync(Ambito.Cuenta, "Provincia", TipoCampo.Texto, null))
            .Exito.Should().BeTrue();
    }

    [Fact]
    public async Task Sin_empresa_activa_no_se_crea_nada()
    {
        var r = await Servicio(empresa: Guid.Empty).CrearAsync(Ambito.Contacto, "Algo", TipoCampo.Texto, null);

        // Guid.Empty sí es una empresa; el caso es no tener ninguna.
        r.Exito.Should().BeTrue();

        var sin = new ServicioCampos(repositorio, entidades, new ContextoDePrueba(null), reloj);
        (await sin.CrearAsync(Ambito.Contacto, "Otra cosa", TipoCampo.Texto, null))
            .Error!.Codigo.Should().Be("empresa.sin_seleccionar");
    }

    [Fact]
    public async Task La_definicion_dice_cuantas_fichas_tienen_cada_campo_relleno()
    {
        // Es el número que hace falta para decidir si quitar un campo cuesta algo. Sin él, borrar es a
        // ciegas.
        var poliza = await Campo("Nº de póliza");
        await Campo("Potencia");
        var otroContacto = Guid.NewGuid();
        entidades.Contactos.Add(otroContacto);

        (await Servicio().FijarAsync(poliza.Id, contacto, "A-1")).Exito.Should().BeTrue();
        (await Servicio().FijarAsync(poliza.Id, otroContacto, "A-2")).Exito.Should().BeTrue();

        var definicion = await Servicio().DefinicionAsync();

        definicion.Should().HaveCount(2);
        definicion.First(c => c.Clave == "n_de_poliza").Rellenos.Should().Be(2);
        definicion.First(c => c.Clave == "potencia").Rellenos.Should().Be(0);
    }

    [Fact]
    public async Task Renombrar_un_campo_que_no_existe_lo_dice()
    {
        (await Servicio().RenombrarAsync(Guid.NewGuid(), "Lo que sea"))
            .Error!.Codigo.Should().Be("campo.no_encontrado");
    }

    // ---------- Reordenar ----------

    [Fact]
    public async Task Reordenar_coloca_los_campos_en_el_orden_que_llega()
    {
        var a = await Campo("A");
        var b = await Campo("B");
        var c = await Campo("C");

        (await Servicio().ReordenarAsync(Ambito.Contacto, [c.Id, a.Id, b.Id])).Exito.Should().BeTrue();

        c.Orden.Should().Be(0);
        a.Orden.Should().Be(1);
        b.Orden.Should().Be(2);
    }

    [Fact]
    public async Task Una_lista_de_orden_incompleta_se_rechaza_entera()
    {
        // Con una lista parcial, los que faltasen se quedarían con el orden viejo y el resultado sería un
        // orden que nadie pidió. Es mejor no hacer nada.
        var a = await Campo("A");
        var b = await Campo("B");

        var r = await Servicio().ReordenarAsync(Ambito.Contacto, [b.Id]);

        r.Error!.Codigo.Should().Be("campo.orden_incompleto");
        a.Orden.Should().Be(0);
        b.Orden.Should().Be(1);
    }

    [Fact]
    public async Task Un_campo_repetido_en_la_lista_de_orden_se_rechaza()
    {
        var a = await Campo("A");
        await Campo("B");

        (await Servicio().ReordenarAsync(Ambito.Contacto, [a.Id, a.Id]))
            .Error!.Codigo.Should().Be("campo.orden_incompleto");
    }

    [Fact]
    public async Task No_se_puede_colar_en_el_orden_un_campo_del_otro_ambito()
    {
        var contactoCampo = await Campo("A");
        var cuentaCampo = await Campo("Sector", ambito: Ambito.Cuenta);

        (await Servicio().ReordenarAsync(Ambito.Contacto, [contactoCampo.Id, cuentaCampo.Id]))
            .Error!.Codigo.Should().Be("campo.orden_incompleto");
    }

    // ---------- Opciones en uso ----------

    [Fact]
    public async Task No_se_quita_una_opcion_que_alguien_esta_usando()
    {
        // **La comprobación que salva el dato.** Quitar «Gas natural» con tres contactos que la tienen
        // dejaría tres valores fuera de la lista: la ficha los enseñaría sin poder cambiarlos y cualquier
        // recuento futuro tendría un grupo fantasma.
        var tipo = await Campo("Tipo de instalación", TipoCampo.Lista, ["Gas natural", "Eléctrica"]);
        (await Servicio().FijarAsync(tipo.Id, contacto, "Gas natural")).Exito.Should().BeTrue();

        var r = await Servicio().CambiarOpcionesAsync(tipo.Id, ["Eléctrica", "Aerotermia"]);

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("campo.opciones_en_uso");
        r.Error!.Mensaje.Should().Contain("un contacto o cuenta", "con uno se dice en singular");
        tipo.Opciones.Should().BeEquivalentTo(["Gas natural", "Eléctrica"]);
    }

    [Fact]
    public async Task El_mensaje_dice_cuantas_fichas_hay_que_arreglar()
    {
        var tipo = await Campo("Tipo", TipoCampo.Lista, ["Gas", "Eléctrica"]);
        var otro = Guid.NewGuid();
        entidades.Contactos.Add(otro);
        (await Servicio().FijarAsync(tipo.Id, contacto, "Gas")).Exito.Should().BeTrue();
        (await Servicio().FijarAsync(tipo.Id, otro, "Gas")).Exito.Should().BeTrue();

        var r = await Servicio().CambiarOpcionesAsync(tipo.Id, ["Eléctrica", "Aerotermia"]);

        r.Error!.Mensaje.Should().Contain("2 fichas", "saber cuántas son decide si merece la pena");
    }

    [Fact]
    public async Task Anadir_una_opcion_sin_quitar_ninguna_no_molesta_a_nadie()
    {
        var tipo = await Campo("Tipo", TipoCampo.Lista, ["Gas", "Eléctrica"]);
        (await Servicio().FijarAsync(tipo.Id, contacto, "Gas")).Exito.Should().BeTrue();

        (await Servicio().CambiarOpcionesAsync(tipo.Id, ["Gas", "Eléctrica", "Aerotermia"]))
            .Exito.Should().BeTrue();

        tipo.Opciones.Should().HaveCount(3);
    }

    // ---------- Borrar ----------

    [Fact]
    public async Task Borrar_un_campo_se_lleva_sus_valores_y_dice_cuantos()
    {
        // Dejarlos habría sido más prudente en apariencia y peor de verdad: sin el campo no se sabe qué
        // significaban ni de qué tipo eran, así que serían datos personales que nadie puede leer ni borrar.
        var poliza = await Campo("Nº de póliza");
        var potencia = await Campo("Potencia");
        var otro = Guid.NewGuid();
        entidades.Contactos.Add(otro);

        (await Servicio().FijarAsync(poliza.Id, contacto, "A-1")).Exito.Should().BeTrue();
        (await Servicio().FijarAsync(poliza.Id, otro, "A-2")).Exito.Should().BeTrue();
        (await Servicio().FijarAsync(potencia.Id, contacto, "4,6")).Exito.Should().BeTrue();

        var r = await Servicio().BorrarAsync(poliza.Id);

        r.Exito.Should().BeTrue();
        r.Valor.Should().Be(2);
        repositorio.Valores.Should().ContainSingle(v => v.CampoId == potencia.Id);
        repositorio.Campos.Should().ContainSingle();
    }

    // ---------- Los valores ----------

    [Fact]
    public async Task La_ficha_ensena_todos_los_campos_definidos_aunque_esten_vacios()
    {
        // Si solo salieran los rellenos, un campo recién definido no aparecería en ninguna ficha y no
        // habría forma de rellenarlo por primera vez.
        var poliza = await Campo("Nº de póliza");
        await Campo("Potencia");
        (await Servicio().FijarAsync(poliza.Id, contacto, "A-1")).Exito.Should().BeTrue();

        var ficha = await Servicio().DeLaFichaAsync(Ambito.Contacto, contacto);

        ficha.Should().HaveCount(2);
        ficha[0].Valor.Should().Be("A-1");
        ficha[1].Valor.Should().BeNull();
    }

    [Fact]
    public async Task La_ficha_viene_en_el_orden_de_los_campos()
    {
        var a = await Campo("A");
        var b = await Campo("B");
        (await Servicio().ReordenarAsync(Ambito.Contacto, [b.Id, a.Id])).Exito.Should().BeTrue();

        (await Servicio().DeLaFichaAsync(Ambito.Contacto, contacto))
            .Select(c => c.Nombre).Should().Equal("B", "A");
    }

    [Fact]
    public async Task La_ficha_de_un_contacto_no_ensena_los_campos_de_las_cuentas()
    {
        await Campo("Sector CNAE", ambito: Ambito.Cuenta);

        (await Servicio().DeLaFichaAsync(Ambito.Contacto, contacto)).Should().BeEmpty();
    }

    [Fact]
    public async Task Fijar_dos_veces_cambia_el_valor_y_no_crea_otro()
    {
        // Quien rellena una casilla no sabe ni le importa si ya había algo. Dos operaciones distintas le
        // habrían obligado a saberlo, y la segunda fila dejaría dos valores del mismo campo en la ficha.
        var poliza = await Campo("Nº de póliza");

        (await Servicio().FijarAsync(poliza.Id, contacto, "A-1")).Exito.Should().BeTrue();
        (await Servicio().FijarAsync(poliza.Id, contacto, "A-2")).Exito.Should().BeTrue();

        repositorio.Valores.Should().ContainSingle();
        repositorio.Valores[0].Texto.Should().Be("A-2");
    }

    [Fact]
    public async Task Vaciar_una_casilla_borra_la_fila()
    {
        // Es lo que espera quien borra el contenido de una casilla, y es lo que evita tener dos formas de
        // decir «no hay dato».
        var poliza = await Campo("Nº de póliza");
        (await Servicio().FijarAsync(poliza.Id, contacto, "A-1")).Exito.Should().BeTrue();

        (await Servicio().FijarAsync(poliza.Id, contacto, "   ")).Exito.Should().BeTrue();

        repositorio.Valores.Should().BeEmpty();
    }

    [Fact]
    public async Task Vaciar_una_casilla_que_ya_estaba_vacia_no_es_un_error()
    {
        var poliza = await Campo("Nº de póliza");

        (await Servicio().FijarAsync(poliza.Id, contacto, null)).Exito.Should().BeTrue();

        repositorio.Valores.Should().BeEmpty();
    }

    [Fact]
    public async Task No_se_cuelga_un_valor_de_un_contacto_que_no_existe()
    {
        // Sin esto quedarían filas que no se ven en ninguna ficha, que no se borran con nadie y que salen
        // en la exportación de la empresa sin dueño.
        var poliza = await Campo("Nº de póliza");

        var r = await Servicio().FijarAsync(poliza.Id, Guid.NewGuid(), "A-1");

        r.Fallido.Should().BeTrue();
        r.Error!.Codigo.Should().Be("campo.entidad_no_encontrada");
        r.Error!.Mensaje.Should().Contain("contacto");
        repositorio.Valores.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_campo_de_cuenta_no_se_rellena_pasandole_un_contacto()
    {
        // El ámbito del campo manda: se comprueba contra la tabla que le toca, no contra las dos.
        var sector = await Campo("Sector CNAE", ambito: Ambito.Cuenta);

        var r = await Servicio().FijarAsync(sector.Id, contacto, "4321");

        r.Error!.Codigo.Should().Be("campo.entidad_no_encontrada");
        r.Error!.Mensaje.Should().Contain("cuenta");
    }

    [Fact]
    public async Task Un_valor_invalido_no_se_guarda_y_dice_por_que()
    {
        var potencia = await Campo("Potencia", TipoCampo.Numero);

        (await Servicio().FijarAsync(potencia.Id, contacto, "cuatro y pico"))
            .Error!.Codigo.Should().Be("valor.no_es_numero");

        repositorio.Valores.Should().BeEmpty();
    }

    [Fact]
    public async Task El_valor_se_guarda_normalizado_y_asi_se_puede_agrupar()
    {
        var tipo = await Campo("Tipo", TipoCampo.Lista, ["Gas natural", "Eléctrica"]);
        var potencia = await Campo("Potencia", TipoCampo.Numero);

        (await Servicio().FijarAsync(tipo.Id, contacto, "  gas NATURAL ")).Exito.Should().BeTrue();
        (await Servicio().FijarAsync(potencia.Id, contacto, "4,6")).Exito.Should().BeTrue();

        var ficha = await Servicio().DeLaFichaAsync(Ambito.Contacto, contacto);

        ficha.First(c => c.CampoId == tipo.Id).Valor.Should().Be("Gas natural");
        ficha.First(c => c.CampoId == potencia.Id).Valor.Should().Be("4.6");
    }

    [Fact]
    public async Task Fijar_un_campo_que_no_existe_lo_dice()
    {
        (await Servicio().FijarAsync(Guid.NewGuid(), contacto, "algo"))
            .Error!.Codigo.Should().Be("campo.no_encontrado");
    }
}
