# match.keting

El CRM que te dice **a quién llamar hoy, y por qué**. Para pymes españolas de 1 a 10 personas, con
uno a tres comerciales.

> **Producto independiente.** No es un módulo de ALXOR Core: repositorio, base de datos y login
> propios. La integración con el ERP es opcional y va por API y eventos.

Documentación de producto en [`docs/producto/`](docs/producto/):
[visión e identidad visual](docs/producto/vision.md) ·
[las 132 capacidades de HubSpot con veredicto](docs/producto/hubspot.md) ·
[diseño técnico y funcional](docs/producto/diseno-tecnico-funcional.md).
Documentación por módulo en [`docs/modulos/`](docs/modulos/).

## Principios

- La simplicidad gana a la cantidad de funcionalidades.
- La aplicación abre en **Hoy**, no en un panel de gráficas. Cuando Hoy se vacía, se dice y se para.
- **Ningún número sin motivo**: si el Match no puede explicarse en una frase, no se muestra.
- Nada se envía en nombre de una persona sin que lo vea antes.
- Multiempresa desde el diseño. Fallos esperados con `Resultado`/`Error`, no con excepciones.
- **Un módulo a la vez, terminado del todo** antes de empezar el siguiente.

## Pila

.NET 8 LTS · PostgreSQL · EF Core (Npgsql) · JWT · monolito modular · Clean Architecture ligera ·
API First (OpenAPI) · xUnit + FluentAssertions. Dominio, tablas y API **en español**.

## Estructura

```
src/
  Matchketing.Nucleo          Resultado, Error, entidades base, IReloj, IContextoEmpresa, Email
  Matchketing.Identidad       Usuario, Membresia, Rol y permisos + casos de uso
  Matchketing.Organizacion    Empresa (tenant) y ajustes del motor Match
  Matchketing.Contactos       Contacto, Cuenta, Actividad, duplicados e importación CSV
  Matchketing.Embudo          Embudo, Etapa, Oportunidad, motivos de pérdida y previsión
  Matchketing.Tareas          Tarea y la pila de Hoy
  Matchketing.Match           Señales, Encaje, Momento y reparto de leads
  Matchketing.Captacion       Formulario embebible y entrada pública de leads
  Matchketing.Informes        Embudo, conversión y motivos de pérdida, con CSV
  Matchketing.Cumplimiento    Consentimiento con prueba (el resto, en el módulo 8)
  Matchketing.Persistencia    EF Core, configuraciones, repositorios, hasher, migraciones
  Matchketing.Api             REST + OpenAPI + JWT + interfaz web
tests/
  Matchketing.Identidad.Tests · Matchketing.Organizacion.Tests
  Matchketing.Contactos.Tests · Matchketing.Embudo.Tests · Matchketing.Tareas.Tests
  Matchketing.Match.Tests · Matchketing.Captacion.Tests · Matchketing.Informes.Tests
  Matchketing.IntegrationTests
```

## Estado

| Módulo | Estado |
|---|---|
| 1. Núcleo, Identidad y Organización | ✅ Terminado |
| 2. Contactos | ✅ Terminado |
| 3. Embudo | ✅ Terminado |
| 4. Tareas y Hoy | ✅ Terminado |
| 5. Match v1 | ✅ Terminado |
| 6. Captación | ✅ Terminado |
| 7. Informes | ✅ Terminado |
| 8. Cumplimiento | ⬜ Pendiente |

## Puesta en marcha

Requisitos: **.NET 8 SDK** y **PostgreSQL** en `localhost:5432` (`postgres`/`postgres`).

```bash
dotnet build
dotnet test                                  # 266 pruebas: 184 unitarias + 82 de integración
dotnet run --project src/Matchketing.Api     # http://localhost:5280
```

En *Development* la API aplica las migraciones sola y publica Swagger en `/swagger`. Prueba de
vida: `GET /salud`. La interfaz web se sirve en la raíz.

Los tests de integración usan la base `matchketing_test`; se puede sobrescribir la cadena con la
variable `MATCHKETING_TEST_CONEXION`.

### Migraciones

```bash
dotnet tool restore
dotnet ef migrations add <Nombre> \
  --project src/Matchketing.Persistencia \
  --startup-project src/Matchketing.Api \
  --output-dir Migraciones
```

### Sin SDK instalado

`dn.sh` y `dsh.sh` ejecutan el SDK vía Docker (`mcr.microsoft.com/dotnet/sdk:8.0`) con
`--network host`. Deducen la raíz del repositorio del propio script, así que funcionan desde
cualquier ruta. Son un apaño para entornos sin SDK, no parte del producto: si lo tienes instalado,
ignóralos.

```bash
./dn.sh build
./dsh.sh 'dotnet test'
```

## API

| Método | Ruta | Descripción |
|---|---|---|
| `GET` | `/salud` | Prueba de vida |
| `POST` | `/auth/registro` | Crea cuenta y devuelve la sesión iniciada. **201** |
| `POST` | `/auth/login` | Inicia sesión |
| `GET` | `/auth/yo` | Perfil y empresas con membresía activa |
| `POST` | `/empresas` | Crea empresa, hace propietario a quien la crea, devuelve token con ella activa |
| `POST` | `/empresas/{id}/seleccionar` | Token nuevo con esa empresa activa |
| `GET` | `/empresas/activa` | Datos y ajustes de la empresa activa |
| `PUT` | `/empresas/activa/ajustes-match` | Peso del Encaje y horas de rebote |
| `GET` | `/contactos?busqueda=` | Listado con búsqueda |
| `GET` | `/contactos/{id}` | Ficha con la cronología |
| `POST` | `/contactos` | Crea un contacto. **201** |
| `POST` | `/contactos/{id}/notas` · `/llamada` | Añade a la cronología |
| `GET` | `/contactos/duplicados` | Parejas propuestas |
| `POST` | `/contactos/{id}/fusionar` | Fusiona sin perder actividades |
| `POST` | `/contactos/importar` | CSV con previsualización |
| `GET` `POST` | `/cuentas` | Cuentas (opcionales) |
| `GET` | `/embudo/tablero` | Columnas, sumas, previsión y estancadas |
| `POST` | `/oportunidades` | Crea una oportunidad. **201** |
| `POST` | `/oportunidades/{id}/mover` | Cambia de etapa |
| `POST` | `/oportunidades/{id}/ganar` · `/perder` | Cierra. Perder exige motivo |
| `GET` | `/informes/motivos-perdida` | Por qué se pierde, en orden |
| `GET` | `/hoy` | La pila del día, ordenada y con sus motivos |
| `GET` `POST` | `/tareas` | Tareas |
| `POST` | `/tareas/{id}/completar` · `/descartar` · `/aplazar` | Aplazar exige fecha futura |
| `GET` | `/match/contactos/{id}` | Puntuación con encaje, momento y motivos |
| `POST` | `/match/recalcular` | Recalcula toda la empresa |
| `GET` | `/match/contactos/{id}/comercial` | Qué comercial encaja mejor, y por qué |
| `POST` | `/match/contactos/{id}/asignar` | Asigna, deja constancia y crea la primera llamada |
| `GET` `POST` | `/formularios` | Formularios de captación |
| `GET` | `/f/{clave}/script.js` | El script de una línea para la web del cliente |
| `POST` | `/f/{clave}` | **Entrada pública de leads** (pública) |
| `POST` | `/f/{clave}/visita` | Visita web de un contacto conocido (pública) |
| `GET` | `/informes/embudo?periodo=mes` | Etapas, conversión real, previsión y ratios |
| `GET` | `/informes/motivos-perdida` | Por qué se pierde, en orden |
| `GET` | `/informes/embudo.csv` · `/motivos-perdida.csv` | CSV para Excel en español |

Documentación por módulo en [`docs/modulos/`](docs/modulos/).
