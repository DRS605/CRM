using System.Globalization;
using System.Text;
using System.Threading.RateLimiting;
using Matchketing.Api.Comun;
using Matchketing.Api.Endpoints;
using Matchketing.Api.Trabajos;
using Matchketing.Auditoria.Aplicacion;
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
using Matchketing.Persistencia.Seguridad;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var constructor = WebApplication.CreateBuilder(args);

var cadena = constructor.Configuration.GetConnectionString("Matchketing")
    ?? "Host=localhost;Port=5432;Database=matchketing;Username=postgres;Password=postgres";

var ajustesJwt = new AjustesJwt(
    constructor.Configuration["Jwt:Clave"] ?? "clave-de-desarrollo-no-usar-en-produccion-0123456789",
    constructor.Configuration["Jwt:Emisor"] ?? "matchketing",
    constructor.Configuration["Jwt:Audiencia"] ?? "matchketing",
    int.TryParse(constructor.Configuration["Jwt:MinutosVigencia"], out var m) ? m : 480);

// El secreto de los enlaces de baja es **distinto** al del JWT a propósito: los tokens de sesión
// caducan en horas y su clave puede rotarse sin avisar a nadie; los enlaces de baja tienen que
// seguir funcionando dentro de años. Compartir la clave habría atado las dos rotaciones y la primera
// rotación del JWT habría matado todos los enlaces de baja emitidos.
var ajustesBaja = new AjustesBaja(
    constructor.Configuration["Baja:Secreto"] ?? "secreto-de-desarrollo-para-enlaces-de-baja-0123456789",
    constructor.Configuration["Baja:UrlBase"] ?? "https://app.matchketing.es");

constructor.Services.AddHttpContextAccessor();
constructor.Services.AddSingleton<IReloj, RelojSistema>();
constructor.Services.AddSingleton(ajustesJwt);
constructor.Services.AddSingleton(ajustesBaja);
constructor.Services.AddScoped<ContextoEmpresaHttp>();
constructor.Services.AddScoped<IContextoEmpresa>(sp => sp.GetRequiredService<ContextoEmpresaHttp>());
constructor.Services.AddScoped<IContextoEmpresaPublico>(sp => sp.GetRequiredService<ContextoEmpresaHttp>());
constructor.Services.AddScoped<IGeneradorTokens, GeneradorJwt>();
constructor.Services.AddSingleton<IHasherContrasena, HasherContrasena>();

constructor.Services.AddScoped<InterceptorEmpresa>();
constructor.Services.AddDbContext<ContextoMatchketing>((sp, o) => o
    .UseNpgsql(cadena)
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

// Los tres trabajos que hacen solos lo que nadie va a hacer a mano. Ver Trabajos/TrabajoPeriodico.cs.
constructor.Services.AddHostedService<TrabajoBarridoMatch>();
constructor.Services.AddHostedService<TrabajoReboteLeads>();
constructor.Services.AddHostedService<TrabajoRetencion>();

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

constructor.Services
    .AddAuthentication("Bearer")
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = ajustesJwt.Emisor,
            ValidAudience = ajustesJwt.Audiencia,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ajustesJwt.Clave)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
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
app.MapFallbackToFile("index.html");

await app.RunAsync().ConfigureAwait(false);

/// <summary>Expuesto para que los tests de integración puedan levantar la API con WebApplicationFactory.</summary>
public partial class Program;
