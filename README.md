# match.keting

El CRM que te dice **a quién llamar hoy, y por qué**. Para pymes españolas de 1 a 10 personas, con
uno a tres comerciales.

> **Producto independiente.** No es un módulo de ALXOR Core: repositorio, base de datos y login
> propios. La integración con el ERP es opcional y va por API y eventos.

Documentación de producto (hoy en el repositorio de ALXOR Core, `docs/productos/`):
`matchketing.md` (visión e identidad) · `matchketing-funcionalidades.md` (mapeo de HubSpot) ·
`matchketing-diseno-tecnico-funcional.md` (el diseño que implementa este código).

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
  Matchketing.Persistencia    EF Core, configuraciones, repositorios, hasher, migraciones
  Matchketing.Api             REST + OpenAPI + JWT + interfaz web
tests/
  Matchketing.Identidad.Tests · Matchketing.Organizacion.Tests
  Matchketing.Contactos.Tests · Matchketing.IntegrationTests
```

## Estado

| Módulo | Estado |
|---|---|
| 1. Núcleo, Identidad y Organización | ✅ Terminado |
| 2. Contactos | ✅ Terminado |
| 3. Embudo | ⬜ Pendiente |
| 4. Tareas y Hoy | ⬜ Pendiente |
| 5. Match v1 | ⬜ Pendiente |
| 6. Captación | ⬜ Pendiente |
| 7. Informes | ⬜ Pendiente |
| 8. Cumplimiento | ⬜ Pendiente |

## Puesta en marcha

Requisitos: **.NET 8 SDK** y **PostgreSQL** en `localhost:5432` (`postgres`/`postgres`).

```bash
dotnet build
dotnet test                                  # 98 pruebas: 74 unitarias + 24 de integración
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
`--network host` y el CA del proxy. Son un apaño del entorno de desarrollo remoto, no parte del
producto: si tienes el SDK instalado, ignóralos.

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

Documentación por módulo en [`docs/modulos/`](docs/modulos/).
