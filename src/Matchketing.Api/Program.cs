using System.Globalization;
using System.Text;
using System.Threading.RateLimiting;
using Matchketing.Api.Comun;
using Matchketing.Api.Endpoints;
using Matchketing.Api.Trabajos;
using Matchketing.Auditoria.Aplicacion;
using Matchketing.Avisos.Aplicacion;
using Matchketing.Avisos.Dominio;
using Matchketing.Webhooks.Aplicacion;
using Matchketing.Correo.Aplicacion;
using Matchketing.Automatizacion.Aplicacion;
using Matchketing.Campanias.Aplicacion;
using Matchketing.Objetivos.Aplicacion;
using Matchketing.Campos.Aplicacion;
using Matchketing.Contactos.Aplicacion;
using Matchketing.Cumplimiento.Aplicacion;
using Matchketing.Embudo.Aplicacion;
using Matchketing.Captacion.Aplicacion;
using Matchketing.Match.Aplicacion;
using Matchketing.Tareas.Aplicacion;
using Matchketing.Identidad.Aplicacion;
using Matchketing.Informes.Aplicacion;
using Matchketing.Nucleo.Comun;
using Matchketing.Nucleo.Tiempo;
using Matchketing.Organizacion.Aplicacion;
using Matchketing.Persistencia;
using Matchketing.Persistencia.Repositorios;
using Matchketing.Repaso.Aplicacion;
using Matchketing.Persistencia.Seguridad;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// ---------------------------------------------------------------------------------------------------
// Modo sonda. `dotnet Matchketing.Api.dll --comprobar-salud` pregunta a `/salud` y sale con 0 o con 1.
//
// Existe para que el contenedor pueda comprobarse a sí mismo **sin instalar nada**. La alternativa era
// un `apt-get install curl` en la imagen final: un gestor de paquetes, una lista de repositorios y una
// dependencia de red en la construcción, todo para hacer una petición HTTP desde un proceso que ya
// sabe hacer peticiones HTTP.
if (args.Contains("--comprobar-salud", StringComparer.Ordinal))
{
    // El puerto sale de donde escucha la aplicación, así que cambiarlo no rompe la sonda.
    var donde = (Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://localhost:8080")
        .Split(';', StringSplitOptions.RemoveEmptyEntries)[0]
        .Replace("+", "localhost", StringComparison.Ordinal)
        .Replace("*", "localhost", StringComparison.Ordinal)
        .TrimEnd('/');

    using var sonda = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    try
    {
        var respuesta = await sonda.GetAsync(new Uri(donde + "/salud")).ConfigureAwait(false);
        var cuerpo = await respuesta.Content.ReadAsStringAsync().ConfigureAwait(false);
        Console.WriteLine(cuerpo);
        return respuesta.IsSuccessStatusCode ? 0 : 1;
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        Console.Error.WriteLine("sin respuesta de " + donde + "/salud: " + ex.Message);
        return 1;
    }
}

var constructor = WebApplication.CreateBuilder(args);

// **Toda** la configuración se lee de forma diferida, dentro de las fábricas del contenedor, y nunca
// aquí arriba con `constructor.Configuration[...]`.
//
// No es estilo: leerla aquí ejecuta la lectura antes de que se hayan añadido las fuentes que aporta
// quien hospeda la aplicación. `WebApplicationFactory` de los tests de integración añade la suya
// —con la base `matchketing_test`— cuando esta línea ya se ha ejecutado, así que una cadena capturada
// en una variable local se queda con el valor por defecto. Eso es exactamente lo que pasaba: los
// tests borraban y recreaban la base de **desarrollo** en cada ejecución, y la variable
// `MATCHKETING_TEST_CONEXION` que documenta el README no servía para nada.
//
// Hay un test que lo vigila: `La_api_de_pruebas_usa_la_base_de_pruebas`.
static string CadenaDeConexion(IConfiguration config) =>
    config.GetConnectionString("Matchketing")
    ?? "Host=localhost;Port=5432;Database=matchketing;Username=postgres;Password=postgres";

constructor.Services.AddHttpContextAccessor();
constructor.Services.AddSingleton<IReloj, RelojSistema>();
constructor.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return new AjustesJwt(
        config["Jwt:Clave"] ?? "clave-de-desarrollo-no-usar-en-produccion-0123456789",
        config["Jwt:Emisor"] ?? "matchketing",
        config["Jwt:Audiencia"] ?? "matchketing",
        int.TryParse(config["Jwt:MinutosVigencia"], out var minutos) ? minutos : 480);
});

// El secreto de los enlaces de baja es **distinto** al del JWT a propósito: los tokens de sesión
// caducan en horas y su clave puede rotarse sin avisar a nadie; los enlaces de baja tienen que
// seguir funcionando dentro de años. Compartir la clave habría atado las dos rotaciones y la primera
// rotación del JWT habría matado todos los enlaces de baja emitidos.
constructor.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return new AjustesBaja(
        config["Baja:Secreto"] ?? "secreto-de-desarrollo-para-enlaces-de-baja-0123456789",
        config["Baja:UrlBase"] ?? "https://app.matchketing.es");
});
constructor.Services.AddScoped<ContextoEmpresaHttp>();
constructor.Services.AddScoped<IContextoEmpresa>(sp => sp.GetRequiredService<ContextoEmpresaHttp>());
constructor.Services.AddScoped<IContextoEmpresaPublico>(sp => sp.GetRequiredService<ContextoEmpresaHttp>());
constructor.Services.AddScoped<IGeneradorTokens, GeneradorJwt>();
constructor.Services.AddSingleton<IHasherContrasena, HasherContrasena>();

constructor.Services.AddScoped<InterceptorEmpresa>();
constructor.Services.AddDbContext<ContextoMatchketing>((sp, o) => o
    .UseNpgsql(CadenaDeConexion(sp.GetRequiredService<IConfiguration>()))
    .AddInterceptors(sp.GetRequiredService<InterceptorEmpresa>()));
constructor.Services.AddScoped<IUnidadDeTrabajo>(sp => sp.GetRequiredService<ContextoMatchketing>());
constructor.Services.AddScoped<IRepositorioUsuarios, RepositorioUsuarios>();
constructor.Services.AddScoped<IRepositorioMembresias, RepositorioMembresias>();
constructor.Services.AddScoped<IRepositorioEmpresas, RepositorioEmpresas>();
constructor.Services.AddScoped<IRepositorioInvitaciones, RepositorioInvitaciones>();
constructor.Services.AddScoped<IConsultaPersonas, ConsultaPersonas>();
constructor.Services.AddScoped<ServicioIdentidad>();
constructor.Services.AddScoped<ServicioEquipo>();
constructor.Services.AddScoped<ServicioEmpresas>();
constructor.Services.AddScoped<IRepositorioContactos, RepositorioContactos>();
constructor.Services.AddScoped<IRepositorioCuentas, RepositorioCuentas>();
constructor.Services.AddScoped<IRepositorioActividades, RepositorioActividades>();
constructor.Services.AddScoped<IConsultaContactos, ConsultaContactos>();
constructor.Services.AddScoped<ServicioContactos>();
constructor.Services.AddScoped<ServicioDuplicados>();
constructor.Services.AddScoped<ImportarContactos>();
constructor.Services.AddScoped<IRepositorioEmbudos, RepositorioEmbudos>();
constructor.Services.AddScoped<IRepositorioOportunidades, RepositorioOportunidades>();
constructor.Services.AddScoped<IConsultaEmbudo, ConsultaEmbudo>();
constructor.Services.AddScoped<ServicioEmbudo>();
constructor.Services.AddScoped<IRepositorioTareas, RepositorioTareas>();
constructor.Services.AddScoped<IConsultaHoy, ConsultaHoy>();
constructor.Services.AddScoped<ServicioTareas>();
constructor.Services.AddScoped<IRepositorioSenales, RepositorioSenales>();
constructor.Services.AddScoped<IRepositorioPuntuaciones, RepositorioPuntuaciones>();
constructor.Services.AddScoped<IConsultaMatch, ConsultaMatch>();
constructor.Services.AddScoped<ServicioMatch>();
constructor.Services.AddScoped<IRepositorioFormularios, RepositorioFormularios>();
constructor.Services.AddScoped<IRepositorioEnvios, RepositorioEnvios>();
constructor.Services.AddScoped<ServicioFormularios>();
constructor.Services.AddScoped<IConsultaInformes, ConsultaInformes>();
constructor.Services.AddScoped<ServicioInformes>();
constructor.Services.AddScoped<IRegistradorAuditoria, RegistradorAuditoria>();
constructor.Services.AddScoped<IRepositorioConsentimientos, RepositorioConsentimientos>();
constructor.Services.AddScoped<IAlmacenPersonal, AlmacenPersonal>();
constructor.Services.AddScoped<IAjustesRetencion, AjustesRetencion>();
constructor.Services.AddScoped<ServicioCumplimiento>();
constructor.Services.AddScoped<IConsultaRepaso, ConsultaRepaso>();
constructor.Services.AddScoped<IRepositorioPospuestas, RepositorioPospuestas>();
constructor.Services.AddScoped<IAccionesRepaso, AccionesRepaso>();
constructor.Services.AddScoped<ServicioRepaso>();
constructor.Services.AddScoped<IRepositorioSuscripciones, RepositorioSuscripciones>();
constructor.Services.AddScoped<IConsultaPendientes, ConsultaPendientes>();
constructor.Services.AddScoped<ServicioAvisos>();
constructor.Services.AddScoped<IRepositorioWebhooks, RepositorioWebhooks>();
constructor.Services.AddScoped<ServicioWebhooks>();
constructor.Services.AddScoped<IRepositorioCorreo, RepositorioCorreo>();
constructor.Services.AddScoped<IConsultaDatosDelEnvio, ConsultaDatosDelEnvio>();
constructor.Services.AddScoped<IPermisoDeEnvio, PermisoDeEnvio>();
constructor.Services.AddScoped<IApuntaEnCronologia, ApuntaEnCronologia>();
constructor.Services.AddScoped<IEnviaCorreo, EnviaCorreoSmtp>();
constructor.Services.AddScoped<IEnlaceDeBaja, EnlaceDeBajaFirmado>();
constructor.Services.AddScoped<ServicioCorreo>();
constructor.Services.AddScoped<IRepositorioReglas, RepositorioReglas>();
constructor.Services.AddScoped<IConsultaHechos, ConsultaHechos>();
constructor.Services.AddScoped<IAccionesAutomatizacion, AccionesAutomatizacion>();
constructor.Services.AddScoped<ServicioAutomatizacion>();
constructor.Services.AddScoped<IRepositorioCampanias, RepositorioCampanias>();
constructor.Services.AddScoped<IBuscaContactosDelSegmento, ConsultaSegmentos>();
constructor.Services.AddScoped<IConsultaEnviosDeCampania, ConsultaCampanias>();

// El mismo objeto sirve los dos puertos: encolar un correo de campaña y leer si la plantilla es
// comercial. Se registra una vez y se pide dos veces, para que las dos vistas del gancho con el módulo
// de correo sean literalmente la misma instancia y no puedan discrepar.
constructor.Services.AddScoped<AccionesCampanias>();
constructor.Services.AddScoped<IEncolaCorreoDeCampania>(sp => sp.GetRequiredService<AccionesCampanias>());
constructor.Services.AddScoped<IPlantillaDeCampania>(sp => sp.GetRequiredService<AccionesCampanias>());
constructor.Services.AddScoped<ServicioCampanias>();
constructor.Services.AddScoped<IRepositorioObjetivos, RepositorioObjetivos>();
constructor.Services.AddScoped<IConsultaLogrado, ConsultaLogrado>();
constructor.Services.AddScoped<IConsultaEquipoObjetivos, ConsultaEquipoObjetivos>();
constructor.Services.AddScoped<ServicioObjetivos>();

constructor.Services.AddScoped<IRepositorioCampos, RepositorioCampos>();
constructor.Services.AddScoped<IExisteLaEntidad, ConsultaExisteLaEntidad>();
constructor.Services.AddScoped<ServicioCampos>();

// El servidor de correo. Si no está configurado, la aplicación arranca igual y los correos se quedan
// como fallidos con el motivo escrito: caerse al arrancar por no poder mandar un correo sería peor que
// no poder mandarlo. Se lee de forma diferida, como todo lo demás.
constructor.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var ajustes = new AjustesSmtp(
        config["Smtp:Servidor"],
        int.TryParse(config["Smtp:Puerto"], System.Globalization.CultureInfo.InvariantCulture, out var puerto) ? puerto : 587,
        config["Smtp:Usuario"],
        config["Smtp:Contrasena"],
        config["Smtp:Remitente"],
        config["Smtp:NombreRemitente"],
        !bool.TryParse(config["Smtp:Ssl"], out var ssl) || ssl);

    if (!ajustes.Configurado)
    {
        sp.GetRequiredService<ILoggerFactory>().CreateLogger("Correo").LogWarning(
            "Sin servidor de correo configurado (Smtp:Servidor y Smtp:Remitente). Los correos se " +
            "encolarán y quedarán como fallidos. Todo lo demás funciona.");
    }

    return ajustes;
});

// Las claves VAPID. En desarrollo se generan al arrancar, y eso es correcto **solo** en desarrollo:
// cada reinicio invalida las suscripciones existentes. En producción vienen de la configuración, y si
// faltan la aplicación arranca igual pero sin avisos; caerse por no poder mandar un aviso semanal
// sería peor que no mandarlo.
constructor.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var sujeto = config["Avisos:Sujeto"] ?? "mailto:avisos@matchketing.es";
    var cargadas = ClavesVapid.De(config["Avisos:ClavePublica"], config["Avisos:ClavePrivada"], sujeto);

    if (cargadas.Exito)
    {
        return cargadas.Valor;
    }

    var registro = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Avisos");
    var generadas = ClavesVapid.Generar(sujeto);
    registro.LogWarning(
        "Sin claves VAPID en la configuración: se han generado unas de usar y tirar. Los avisos push " +
        "funcionarán hasta el próximo reinicio. Para producción, pon estas en la configuración: " +
        "Avisos:ClavePublica={Publica} Avisos:ClavePrivada={Privada}", generadas.Publica, generadas.Privada);

    return generadas;
});

// Cliente propio para los servicios de push: son terceros lentos y no queremos que un aviso atascado
// se coma el grupo de conexiones que atiende a las personas.
constructor.Services.AddHttpClient<IEmisorAvisos, EmisorWebPush>(c => c.Timeout = TimeSpan.FromSeconds(10));

// Y otro para los webhooks, por el mismo motivo y con una cautela más: **no seguir redirecciones**. Un
// 301 hacia otro dominio convertiría nuestra petición firmada, con el cuerpo entero dentro, en una
// petición a un sitio que el cliente nunca configuró.
constructor.Services
    .AddHttpClient<IEnviaWebhook, EnviaWebhook>(c => c.Timeout = EnviaWebhook.Espera)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

// Los siete trabajos que hacen solos lo que nadie va a hacer a mano. Ver Trabajos/TrabajoPeriodico.cs.
constructor.Services.AddHostedService<TrabajoBarridoMatch>();
constructor.Services.AddHostedService<TrabajoReboteLeads>();
constructor.Services.AddHostedService<TrabajoRetencion>();
constructor.Services.AddHostedService<TrabajoAvisoRepaso>();
constructor.Services.AddHostedService<TrabajoEntregaWebhooks>();
constructor.Services.AddHostedService<TrabajoEnvioCorreos>();
constructor.Services.AddHostedService<TrabajoCampanias>();

// Límite de intentos en el acceso. Sin esto, la única defensa de una contraseña es su longitud, y
// probar cien mil contraseñas contra un correo conocido no cuesta nada de dinero ni de tiempo.
//
// Se reparte **por IP de origen**, no por cuenta, y eso es deliberado: bloquear una cuenta tras N
// fallos convierte el límite en un arma contra su dueño —basta con fallar adrede para dejarle
// fuera—, y ese ataque es más fácil y más dañino que el que se pretendía evitar.
//
// Veinte intentos cada cinco minutos: de sobra para una oficina entera detrás de un NAT tecleando
// mal la contraseña, y ridículo para quien esté probando un diccionario.
constructor.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("acceso", contexto => RateLimitPartition.GetFixedWindowLimiter(
        contexto.Connection.RemoteIpAddress?.ToString() ?? "sin-ip",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(5),
            QueueLimit = 0,
        }));

    // Aceptar una invitación también comprueba una contraseña cuando la cuenta ya existe, así que
    // también necesita techo. Y va **por invitación, no por IP**, que es lo que hace este caso
    // distinto del de entrar:
    //
    // * Lo que se puede adivinar aquí es la contraseña de **una** cuenta, la del correo que lleva esa
    //   invitación dentro. El cubo tiene que ser esa invitación, no el edificio desde el que se
    //   teclea.
    // * Con un cubo por IP, una tarde de altas —cinco personas de la misma oficina entrando en la
    //   empresa— se habría comido los intentos de las demás. Y compartirlo con el de entrar habría
    //   dejado sin acceso a todo el mundo durante cinco minutos, que es peor que el ataque que evita.
    //
    // Cinco intentos cada cinco minutos, mucho más estrecho que los veinte del acceso, y aun así de
    // sobra: la invitación la abre una persona que sabe su contraseña.
    o.AddPolicy("invitacion", contexto => RateLimitPartition.GetFixedWindowLimiter(
        contexto.Request.RouteValues.TryGetValue("token", out var cual) && cual is string llave
            ? "invitacion:" + llave
            : contexto.Connection.RemoteIpAddress?.ToString() ?? "sin-ip",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(5),
            QueueLimit = 0,
        }));

    o.OnRejected = async (contexto, ct) =>
    {
        var segundos = contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera)
            ? (int)espera.TotalSeconds
            : 300;

        contexto.HttpContext.Response.Headers.RetryAfter = segundos.ToString(CultureInfo.InvariantCulture);
        await contexto.HttpContext.Response.WriteAsJsonAsync(
            new { codigo = "acceso.demasiados_intentos", mensaje = "Demasiados intentos. Prueba otra vez en unos minutos." }, ct)
            .ConfigureAwait(false);
    };
});

// **Detrás de un proxy inverso, la IP del cliente no es la del socket.** Es el detalle que convierte
// tres cosas correctas en tres cosas falsas, y ninguna de las tres avisa:
//
// 1. El techo de intentos de acceso reparte por IP. Con la del proxy, **todo el mundo comparte cubo**:
//    veinte intentos fallidos de cualquiera dejan sin entrar a la empresa entera.
// 2. La IP del consentimiento —`cumplimiento.consentimiento`— es parte de la prueba de que alguien
//    aceptó. Guardar la del proxy no rompe nada visible: deja una prueba que no prueba nada.
// 3. Lo mismo con la IP del envío de un formulario.
//
// Y el arreglo obvio —confiar en `X-Forwarded-For` siempre— es peor que el problema: cualquiera puede
// mandar esa cabecera, así que se podría elegir la IP que queda escrita en el consentimiento y saltarse
// el techo de intentos cambiándola en cada petición.
//
// Así que **falla cerrado**: solo se lee la cabecera si el despliegue declara que hay un proxio delante
// (`Proxy:Confiar=true`), y solo se acepta viniendo de las redes declaradas. Sin declararlo, la IP es
// la del socket, que es la verdad cuando no hay nada delante.
constructor.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // Un solo salto. Con más, la IP se toma de más a la izquierda de la lista, que es la parte que
    // escribe el cliente y por tanto la que se puede inventar.
    o.ForwardLimit = 1;

    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();

    var declaradas = (constructor.Configuration["Proxy:Redes"] ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // Sin redes declaradas se confía en las privadas, que es el caso de un contenedor con el proxio al
    // lado en la misma red. Es un valor por defecto útil y **solo se usa si alguien ya dijo que hay
    // proxio**: los dos interruptores tienen que estar puestos.
    if (declaradas.Length == 0)
    {
        declaradas = ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8", "::1/128"];
    }

    foreach (var red in declaradas)
    {
        var partes = red.Split('/');
        if (System.Net.IPAddress.TryParse(partes[0], out var direccion)
            && int.TryParse(partes.ElementAtOrDefault(1) ?? "32", CultureInfo.InvariantCulture, out var prefijo))
        {
            o.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(direccion, prefijo));
        }
    }
});

constructor.Services.AddAuthentication("Bearer").AddJwtBearer();

// Los parámetros de validación se configuran con `AjustesJwt` **resuelto del contenedor**, no con una
// variable capturada: así el emisor y la clave salen de la misma fuente que usa el generador de
// tokens, sea la que sea. Con un `AddJwtBearer(o => …)` que capturase una local, quien sobrescriba la
// configuración firmaría con una clave y validaría con otra.
constructor.Services
    .AddOptions<JwtBearerOptions>("Bearer")
    .Configure<AjustesJwt>((o, ajustes) => o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = ajustes.Emisor,
        ValidAudience = ajustes.Audiencia,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ajustes.Clave)),
        ClockSkew = TimeSpan.FromSeconds(30),
    });
constructor.Services.AddAuthorization();

// El formulario se pega en la web del cliente, que es **otro origen**. Sin CORS el navegador
// bloquearía el envío y la captación no funcionaría fuera de nuestro propio dominio.
//
// Se abre a cualquier origen a propósito y solo para las rutas públicas de `/f`: no sabemos en qué
// dominio está la web de cada cliente y pedirle que la registre sería una fricción absurda. Lo que
// protege el endpoint es la clave del formulario, no el origen; y no hay credenciales de por medio,
// así que un tercero no puede hacer nada que no pudiera hacer con un `curl`.
constructor.Services.AddCors(o => o.AddPolicy("captacion", p => p
    .WithMethods("GET", "POST")
    .AllowAnyHeader()
    .AllowAnyOrigin()));
constructor.Services.AddEndpointsApiExplorer();
constructor.Services.AddSwaggerGen(o => o.SwaggerDoc("v1", new() { Title = "match.keting", Version = "v1" }));

constructor.Services.AddSingleton<Aislamiento>();

var app = constructor.Build();

// **Antes de escuchar en ningún puerto.** Si un secreto sigue con el valor de desarrollo —que está
// publicado en el repositorio— la aplicación no arranca y dice cuál y por qué. Va aquí, después de
// `Build()`, para que estén todas las fuentes de configuración cargadas.
Secretos.Exigir(app.Configuration, app.Environment);

if (app.Environment.IsDevelopment())
{
    using var alcance = app.Services.CreateScope();
    var bd = alcance.ServiceProvider.GetRequiredService<ContextoMatchketing>();
    await bd.Database.MigrateAsync().ConfigureAwait(false);

    app.UseSwagger();
    app.UseSwaggerUI();
}

// Lo primero de todo, antes de que algo mire la IP o el esquema: si hay proxio declarado, se traduce
// aquí y el resto de la aplicación no se entera de que existe.
if (app.Configuration.GetValue("Proxy:Confiar", false))
{
    app.UseForwardedHeaders();
}

// HSTS solo en producción, y **nunca** en desarrollo: un `Strict-Transport-Security` en localhost se
// queda pegado en el navegador durante meses y deja `http://localhost` inaccesible para todos los
// proyectos que usen ese puerto. Es un tiro en el pie clásico.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// Cabeceras de seguridad. Van en un middleware propio y no en el proxio a propósito: si mañana esto se
// despliega detrás de otra cosa —o sin nada delante—, las cabeceras siguen puestas. Una protección que
// vive en la configuración de otro programa es una protección que se pierde en la primera mudanza.
app.Use(async (contexto, siguiente) =>
{
    var cabeceras = contexto.Response.Headers;
    cabeceras["X-Content-Type-Options"] = "nosniff";
    cabeceras["Referrer-Policy"] = "strict-origin-when-cross-origin";
    cabeceras["X-Frame-Options"] = "DENY";

    // El fragmento de captación es un `<script src>` en la web del cliente, **no un iframe**, así que
    // prohibir el marco no rompe nada y evita el secuestro de clics sobre la aplicación.
    //
    // `'unsafe-inline'` está y es una concesión de verdad, no un descuido: la aplicación es un solo
    // fichero con su estilo y su guion dentro, servido como fichero estático. Quitarlo exige un nonce
    // por petición, y para eso hay que dejar de servir la página como estática. Se acepta y se escribe:
    // a cambio, `default-src 'self'` deja fuera cualquier origen externo, que es la mitad del valor de
    // una CSP en una aplicación sin dependencias.
    cabeceras["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "connect-src 'self'; " +
        "worker-src 'self'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'";

    await siguiente(contexto).ConfigureAwait(false);
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Salud de verdad: pregunta a la base de datos.
//
// El anterior devolvía «vivo» mientras el proceso respondiera, lo cual es exactamente el estado en el
// que un equilibrador de carga **no** debe mandarte tráfico: proceso arriba y base de datos caída es
// el caso que la sonda tiene que detectar, y era el único que no detectaba.
app.MapGet("/salud", async (ContextoMatchketing bd, Aislamiento aislamiento, CancellationToken ct) =>
{
    if (!await bd.Database.CanConnectAsync(ct).ConfigureAwait(false))
    {
        return Results.Json(
            new { estado = "enfermo", base_datos = "sin conexión" },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    // Y la segunda pregunta, que es la que nadie hace: ¿está puesta la segunda barrera del aislamiento?
    // Con un rol superusuario las políticas por fila no se aplican y el producto promete algo que no
    // cumple, sin que falle nada. Aquí sí falla.
    if (!await aislamiento.DosBarrerasAsync(bd, ct).ConfigureAwait(false))
    {
        return Results.Json(
            new
            {
                estado = "enfermo",
                base_datos = "ok",
                aislamiento = "una sola barrera: el rol de la conexión es superusuario, así que las " +
                    "políticas por fila de PostgreSQL no se le aplican. Conéctate con un rol normal " +
                    "(ver docs/despliegue.md).",
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Ok(new { estado = "vivo", base_datos = "ok", aislamiento = "dos barreras" });
})
    .WithTags("Sistema")
    .WithSummary("Sonda de salud. Devuelve 503 si no se llega a la base o si falta una barrera del aislamiento.");

app.MapearIdentidad();
app.MapearOrganizacion();
app.MapearEquipo();
app.MapearContactos();
app.MapearEmbudo();
app.MapearTareas();
app.MapearMatch();
app.MapearCaptacion();
app.MapearInformes();
app.MapearCumplimiento();
app.MapearAuditoria();
app.MapearRepaso();
app.MapearAvisos();
app.MapearWebhooks();
app.MapearCorreo();
app.MapearAutomatizacion();
app.MapearCampanias();
app.MapearObjetivos();
app.MapearCampos();
app.MapFallbackToFile("index.html");

await app.RunAsync().ConfigureAwait(false);

// El `return 0` es por el modo sonda de arriba: en cuanto un camino del punto de entrada devuelve un
// número, todos tienen que devolverlo.
return 0;

/// <summary>Expuesto para que los tests de integración puedan levantar la API con WebApplicationFactory.</summary>
public partial class Program;
