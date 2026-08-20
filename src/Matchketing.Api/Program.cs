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
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

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
constructor.Services.AddScoped<ServicioIdentidad>();
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
constructor.Services.AddScoped<ServicioCorreo>();
constructor.Services.AddScoped<IRepositorioReglas, RepositorioReglas>();
constructor.Services.AddScoped<IConsultaHechos, ConsultaHechos>();
constructor.Services.AddScoped<IAccionesAutomatizacion, AccionesAutomatizacion>();
constructor.Services.AddScoped<ServicioAutomatizacion>();

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

// Los seis trabajos que hacen solos lo que nadie va a hacer a mano. Ver Trabajos/TrabajoPeriodico.cs.
constructor.Services.AddHostedService<TrabajoBarridoMatch>();
constructor.Services.AddHostedService<TrabajoReboteLeads>();
constructor.Services.AddHostedService<TrabajoRetencion>();
constructor.Services.AddHostedService<TrabajoAvisoRepaso>();
constructor.Services.AddHostedService<TrabajoEntregaWebhooks>();
constructor.Services.AddHostedService<TrabajoEnvioCorreos>();

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

var app = constructor.Build();

if (app.Environment.IsDevelopment())
{
    using var alcance = app.Services.CreateScope();
    var bd = alcance.ServiceProvider.GetRequiredService<ContextoMatchketing>();
    await bd.Database.MigrateAsync().ConfigureAwait(false);

    app.UseSwagger();
    app.UseSwaggerUI();
}

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
app.MapGet("/salud", async (ContextoMatchketing bd, CancellationToken ct) =>
        await bd.Database.CanConnectAsync(ct).ConfigureAwait(false)
            ? Results.Ok(new { estado = "vivo", base_datos = "ok" })
            : Results.Json(new { estado = "enfermo", base_datos = "sin conexión" }, statusCode: StatusCodes.Status503ServiceUnavailable))
    .WithTags("Sistema")
    .WithSummary("Sonda de salud. Devuelve 503 si no se llega a la base de datos.");

app.MapearIdentidad();
app.MapearOrganizacion();
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
app.MapFallbackToFile("index.html");

await app.RunAsync().ConfigureAwait(false);

/// <summary>Expuesto para que los tests de integración puedan levantar la API con WebApplicationFactory.</summary>
public partial class Program;
