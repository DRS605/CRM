using System.Text;
using Matchketing.Api.Comun;
using Matchketing.Api.Endpoints;
using Matchketing.Contactos.Aplicacion;
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

constructor.Services.AddHttpContextAccessor();
constructor.Services.AddSingleton<IReloj, RelojSistema>();
constructor.Services.AddSingleton(ajustesJwt);
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
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/salud", () => Results.Ok(new { estado = "vivo" })).WithTags("Sistema");
app.MapearIdentidad();
app.MapearOrganizacion();
app.MapearContactos();
app.MapearEmbudo();
app.MapearTareas();
app.MapearMatch();
app.MapearCaptacion();
app.MapearInformes();
app.MapFallbackToFile("index.html");

await app.RunAsync().ConfigureAwait(false);

/// <summary>Expuesto para que los tests de integración puedan levantar la API con WebApplicationFactory.</summary>
public partial class Program;
