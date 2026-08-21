# match.keting

El CRM que te dice **a quién llamar hoy, y por qué** — y que te deja cerrar el seguimiento de una
semana en menos de dos minutos, sin escribir nada. Para pymes españolas de 1 a 10 personas, con uno a
tres comerciales.

> **Producto independiente.** No es un módulo de ALXOR Core: repositorio, base de datos y login
> propios. La integración con el ERP es opcional y va por API y eventos.

Documentación de producto en [`docs/producto/`](docs/producto/):
[visión e identidad visual](docs/producto/vision.md) ·
[las 132 capacidades de HubSpot con veredicto](docs/producto/hubspot.md) ·
[diseño técnico y funcional](docs/producto/diseno-tecnico-funcional.md).
Documentación por módulo en [`docs/modulos/`](docs/modulos/).

## Principios

- La simplicidad gana a la cantidad de funcionalidades.
- **El comercial no rellena el CRM: el CRM pregunta y él contesta.** Todo lo que el sistema puede
  deducir, lo deduce y lo propone. Ver [Repaso](docs/modulos/repaso.md).
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
  Matchketing.Repaso          El repaso semanal: seis preguntas derivadas, cero texto libre
  Matchketing.Avisos          Web Push propio: VAPID, cifrado del cuerpo y el empujón del viernes
  Matchketing.Webhooks        Avisar a otro sistema: firma HMAC, buzón de salida y reintentos
  Matchketing.Correo          Plantillas, envío con permiso comprobado dos veces y aperturas
  Matchketing.Automatizacion  «Si pasa X, haz Y»: sin ramas, apagadas al nacer y auditables
  Matchketing.Cumplimiento    Consentimiento con prueba, baja de un clic, RGPD y retención
  Matchketing.Auditoria       Quién hizo qué (transversal, como Núcleo)
  Matchketing.Persistencia    EF Core, configuraciones, repositorios, hasher, migraciones
  Matchketing.Api             REST + OpenAPI + JWT + interfaz web + trabajos en segundo plano
tests/
  Matchketing.Nucleo.Tests · Matchketing.Identidad.Tests · Matchketing.Organizacion.Tests
  Matchketing.Contactos.Tests · Matchketing.Embudo.Tests · Matchketing.Tareas.Tests
  Matchketing.Match.Tests · Matchketing.Captacion.Tests · Matchketing.Informes.Tests
  Matchketing.Cumplimiento.Tests · Matchketing.Auditoria.Tests · Matchketing.Repaso.Tests
  Matchketing.Avisos.Tests · Matchketing.Webhooks.Tests · Matchketing.Correo.Tests
  Matchketing.Automatizacion.Tests · Matchketing.IntegrationTests
```

`Matchketing.Auditoria` es la **única excepción** a la regla de que ningún módulo referencia a otro:
es dominio puro sin frameworks, y los módulos de negocio la usan igual que usan `Matchketing.Nucleo`.
El motivo está en [`docs/modulos/auditoria.md`](docs/modulos/auditoria.md).

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
| 8. Cumplimiento | ✅ Terminado |
| 9. Repaso | ✅ Terminado |
| 10. Avisos | ✅ Terminado |
| 11. Webhooks | ✅ Terminado |
| 12. Correo | ✅ Terminado |
| 13. Automatización (F2) | ✅ Terminado |
| 14. Equipo | ✅ Terminado |
| 15. Campañas | ✅ Terminado |

Los ocho primeros son el MVP: con ellos ya se puede trabajar. Los siete siguientes son los que hacen
que un comercial vuelva al día siguiente, y cada uno cierra un agujero concreto:

- **[Repaso](docs/modulos/repaso.md)** reduce cerrar la semana a dos minutos.
- **[Avisos](docs/modulos/avisos.md)** hace que se acuerde: un aviso al móvil los viernes a las seis,
  y solo si hay algo que decidir.
- **[Webhooks](docs/modulos/webhooks.md)** deja de contarlo dos veces: oportunidad ganada aquí,
  pedido emitido en el ERP.
- **[Correo](docs/modulos/correo.md)** le da al repaso su séptima pregunta y la más rentable: «le
  escribiste hace seis días, lo ha abierto tres veces y no ha contestado».
- **[Automatización](docs/modulos/automatizacion.md)** quita el trabajo que se repite: «si entra un
  lead de feria, llámalo hoy». Sin lienzo de ramas, y las reglas nacen apagadas.
- **[Equipo](docs/modulos/equipo.md)** cierra un renglón del MVP que estaba a medias: una empresa ya
  puede tener dos personas. Los tres papeles y las zonas del reparto de leads existían desde el
  módulo 1 **sin ninguna forma de llegar a ellos**.
- **[Campañas](docs/modulos/campanias.md)** es la pieza por la que hasta ahora había que contratar
  además una herramienta de mailing. No la copia: los segmentos son condiciones sobre datos del CRM y
  no listas que envejecen, el permiso se comprueba **persona a persona** al encolar cada correo, y la
  ficha de la campaña dice a cuántos **no** llegó y por qué. Ese último número es el que una plataforma
  de envío masivo no te enseña.

Lo que se añadió después del octavo y no es un módulo —auditoría, trabajos en segundo plano, límite de
intentos de acceso, sonda de salud real y el rol de base de datos sin superusuario— está en
[`docs/despliegue.md`](docs/despliegue.md).

## Puesta en marcha

Requisitos: **.NET 8 SDK** y **PostgreSQL** en `localhost:5432` (`postgres`/`postgres`).

```bash
dotnet build
dotnet test                                  # 914 pruebas: 607 unitarias + 307 de integración
dotnet run --project src/Matchketing.Api     # http://localhost:5280
```

La interfaz tiene **nueve secciones**: Hoy · Repaso · Contactos · Cuentas · Embudo · Tareas ·
Informes · Equipo · Ajustes. En un teléfono la barra enseña cuatro y el resto va en «Más», porque
nueve entradas en una barra de pulgar dan cuarenta píxeles cada una.

Se **instala en el móvil**: manifiesto, iconos y el número de decisiones pendientes en el propio
icono de la aplicación. Ver [`docs/movil.md`](docs/movil.md).
Las letras y el sistema de color —qué dato dice cada color, y por qué las tipografías están en el
repositorio y no en un CDN— en [`docs/interfaz.md`](docs/interfaz.md).

En *Development* la API aplica las migraciones sola y publica Swagger en `/swagger`. La interfaz web
se sirve en la raíz. `GET /salud` es la sonda: devuelve **503** si no llega a la base de datos.

**Antes de poner esto en producción, lee [`docs/despliegue.md`](docs/despliegue.md).** La aplicación
tiene que conectarse con un rol **sin privilegios de superusuario** o la mitad del aislamiento entre
empresas —la *row level security* de PostgreSQL— no se aplica.

Los tests de integración usan la base `matchketing_test`, que **borran y recrean** al empezar; se
puede sobrescribir la cadena con la variable `MATCHKETING_TEST_CONEXION`.

Y hay una comprobación que ningún test de C# puede hacer, porque para hacerla hay que conectarse con
otro rol de PostgreSQL:

```bash
./scripts/comprobar-aislamiento.sh           # la RLS, con un rol sin superusuario
```

La [integración continua](.github/workflows/pruebas.yml) ejecuta las dos cosas en cada empujón, contra
un PostgreSQL de verdad.

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
| `POST` | `/auth/contrasena` | Cambia la contraseña. Exige la actual |
| `PUT` | `/empresas/activa/ajustes-retencion` | Plazo de conservación de leads (3–120 meses) |
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
| `GET` | `/cumplimiento/contactos/{id}` | Panel de privacidad, con su enlace de baja |
| `GET` `POST` `DELETE` | `/cumplimiento/contactos/{id}/consentimientos` | Permisos con su prueba |
| `GET` | `/cumplimiento/contactos/{id}/puede-enviar?finalidad=` | **G1**: sí o no, con el motivo |
| `GET` | `/cumplimiento/contactos/{id}/exportar` | Derecho de acceso y portabilidad |
| `DELETE` | `/cumplimiento/contactos/{id}` | Derecho de supresión. Borra de verdad |
| `GET` | `/cumplimiento/empresa/exportar` | Copia completa de la empresa |
| `POST` | `/cumplimiento/empresa/borrar` | Cierre de cuenta, escribiendo su nombre |
| `POST` | `/cumplimiento/retencion` | Aplica ya la retención de leads |
| `GET` | `/b/{token}` | **Página de baja** (pública). Pregunta; no da de baja |
| `POST` | `/b/{token}` | Confirma la baja (pública) |
| `GET` | `/auditoria` | Quién hizo qué y cuándo |
| `GET` | `/repaso` | **La pila del repaso**: qué decidir, con las respuestas escritas |
| `POST` | `/repaso/responder` | Contesta una pregunta y hace todo lo que implica |
| `GET` | `/repaso/resumen?dias=7` | Su semana, contada para él |
| `GET` | `/avisos/clave` | La clave pública VAPID con la que se suscribe el navegador |
| `GET` | `/avisos/aparatos` | Sus aparatos con avisos activados. Sin el endpoint |
| `POST` | `/avisos/suscripcion` | Da de alta este aparato. Idempotente |
| `DELETE` | `/avisos/suscripcion?endpoint=` | Lo apaga. **Nunca falla**, ni si no existía |
| `GET` | `/webhooks/eventos` | El catálogo: cinco eventos, con su nombre público |
| `GET` | `/webhooks` | Los webhooks de la empresa. **Nunca devuelve el secreto** |
| `POST` | `/webhooks` | Da de alta uno y devuelve su secreto de firma, la única vez |
| `GET` | `/webhooks/{id}/entregas` | Los últimos intentos: lo que se mira cuando algo no llega |
| `POST` | `/webhooks/{id}/secreto` | Secreto nuevo. El anterior deja de valer al momento |
| `POST` | `/webhooks/{id}/reactivar` | Vuelve a encender uno que se apagó solo |
| `DELETE` | `/webhooks/{id}` | Lo borra |
| `GET` | `/plantillas` | Las plantillas de correo, las más usadas primero |
| `POST` | `/plantillas` | Crea una. Un hueco que no exista se rechaza aquí, no al enviar |
| `GET` | `/correo/borrador?contactoId=&plantillaId=` | Lo que se va a mandar y si se puede. **Sin enviar nada** |
| `POST` | `/correo/enviar` | Encola un correo. Devuelve **202**: está en el buzón de salida |
| `GET` | `/correo/contacto/{id}` | Sus correos, con el texto y las aperturas |
| `GET` | `/e/{token}.gif` | **El píxel de apertura** (público). Siempre la misma imagen |
| `GET` | `/reglas` | Las reglas automáticas, cada una **leída en castellano** |
| `POST` | `/reglas` | Crea una regla, **apagada** |
| `GET` | `/reglas/{id}/ensayo?contactoId=` | Qué haría con ese contacto, **sin hacerlo** |
| `GET` | `/reglas/{id}/ejecuciones` | Qué ha hecho y sobre quién |
| `POST` | `/reglas/{id}/encender?encender=` | La enciende o la apaga |

Documentación por módulo en [`docs/modulos/`](docs/modulos/).
