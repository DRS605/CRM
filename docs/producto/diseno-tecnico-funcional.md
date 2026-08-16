# match.keting Start — Documento de diseño técnico y funcional

> Entregable previo al código, al estilo del de ALXOR Core. **Producto independiente**: repositorio,
> solución, base de datos y login propios.
>
> Los módulos **1 a 6 de 8 están implementados** en este repositorio; ver el estado en el
> [`README.md`](../../README.md) y el detalle de cada uno en [`docs/modulos/`](../modulos/).
>
> Complementa a [`vision.md`](vision.md) (ideas e identidad visual) y a [`hubspot.md`](hubspot.md)
> (las 132 capacidades de HubSpot con veredicto). Aquí ya **no hay preguntas abiertas**: están todas
> decididas en §1.

---

## 0. Visión

**match.keting Start** es el CRM más sencillo del mercado para una pyme española con entre uno y
tres comerciales. No compite con HubSpot en cantidad de funcionalidades: compite en que **el
comercial abre la aplicación por la mañana, ve lo que tiene que hacer, lo hace y la cierra**.

Regla de oro heredada de ALXOR: **la simplicidad gana siempre a la cantidad de funcionalidades**.

**Criterios de aceptación del producto:**

- Un lead que entra por formulario está **asignado, puntuado y con una acción propuesta en menos de
  60 segundos**, sin que nadie lo toque.
- Un comercial que nunca ha visto el programa entiende la pantalla **Hoy en menos de 5 minutos**,
  sin formación.
- **Cero contactos activos sin próxima acción**: el sistema no deja que eso pase inadvertido.

---

## 1. Decisiones tomadas

Cierro las nueve preguntas que quedaban abiertas. Cada una con el motivo en una línea.

| # | Pregunta | Decisión | Por qué |
|---|---|---|---|
| 1 | **Alcance del MVP** | El corte de cinco bloques: **Contactos · Embudo · Tareas y Hoy · Match v1 · Captación**, más lo que no es opcional (identidad y multiempresa, dos informes, cumplimiento mínimo, auditoría, API) | 44 funcionalidades eran más que el MVP entero del ERP. Esto es vendible y no hipoteca nada |
| 2 | **Público objetivo** | Micropyme española de 1–10 personas, con **1–3 comerciales** | Es el mercado que ya conocéis con ALXOR Core, y el reparto de leads sigue teniendo sentido con 2 personas |
| 3 | **B2B o B2C** | **Los dos**. `Cuenta` es opcional; en B2C simplemente no se rellena y no aparece | Una peluquería y una empresa de instalaciones deben caber en el mismo producto |
| 4 | **WhatsApp** | **Fuera del MVP**, a F2 | API oficial, plantillas aprobadas y coste por conversación: es un módulo entero, no una funcionalidad |
| 5 | **Correo conectado** | **Fuera del MVP**, primer añadido de F2 | OAuth de Google y Microsoft más IMAP genérico es donde más tiempo se va y donde más cosas se rompen |
| 6 | **Repositorio y despliegue** | **Separados desde el día uno**. Repositorio `matchketing`, base de datos propia, contenedor propio | Es la única forma de que la independencia sea real y no una intención |
| 7 | **Compartir el `Nucleo` de ALXOR** | **No**. Se duplican `Resultado`, `Error`, `Dinero`, `IReloj` y `ContextoEmpresa` | Son ~400 líneas. La independencia vale más que no repetirse; un paquete compartido ata las versiones de dos productos |
| 8 | **Identidad** | **Login propio**. *ALXOR ID* (SSO) queda para F3 y siempre como opción | Un producto que necesita otro producto para iniciar sesión no es independiente |
| 9 | **Marca** | Registrar `matchketing.com` y `matchketing.es`. **match.keting** es solo la grafía visual; en código, `Matchketing` | `.keting` no es un dominio real |

---

## 2. Alcance del MVP

**Incluye:**

- Registro, inicio de sesión, usuarios y equipos
- Empresa (multiempresa desde el diseño)
- Contactos y Cuentas, con cronología unificada
- Importación CSV y fusión de duplicados
- Embudo con oportunidades, etapas y motivo de pérdida
- Tareas y la pantalla **Hoy**
- **Match v1**: puntuación explicable y reparto de leads
- Formulario web embebible con consentimiento
- Seguimiento web de contactos ya conocidos
- Dos informes: **embudo** y **motivos de pérdida**
- Registro de llamadas en un clic
- Consentimiento, baja y borrado (RGPD)
- Auditoría de operaciones críticas
- API REST con OpenAPI y webhooks salientes

**Fuera del MVP** (a F2 salvo donde se indique): correo conectado, plantillas y seguimiento de
envíos · secuencias · segmentos y campañas · WhatsApp · enlace de reuniones · extensión de navegador ·
automatizaciones «si pasa X, haz Y» · integración con ALXOR Core · atribución de origen ·
Match v2 con pesos aprendidos · tickets, chat y encuestas (F3) · app móvil (F3) · asistente de IA (F3).

**Automatizaciones**: fuera. En el MVP hay **una sola automatización, fija y no configurable**: lead
nuevo → asignar por match → crear tarea de primera llamada. Es la que justifica el producto; el resto
espera.

Menú lateral del MVP (**5 opciones**): **Hoy · Contactos · Embudo · Informes · Ajustes**.

---

## 3. Flujos esenciales

1. **Entra un lead** (formulario o alta manual) → se puntúa → se asigna al comercial con mejor match
   → se crea la tarea de primera llamada → aviso. **Objetivo: < 60 s, sin intervención.**
2. **El día del comercial**: abrir → **Hoy** → tarjeta a tarjeta (llamar, aplazar, descartar) → «Hecho
   por hoy».
3. **Registrar el resultado de una llamada**: un clic (contactado · no contesta · no interesa ·
   volver a llamar + fecha). Si el resultado lo pide, la siguiente tarea se crea sola.
4. **Mover una oportunidad**: arrastrar en el embudo. Al perder, **motivo obligatorio**.
5. **El lunes del gerente**: dos informes, embudo y motivos de pérdida. Nada más.

---

## 4. Experiencia de usuario

Los principios de ALXOR se aplican sin cambios: menos de 10 opciones de menú (aquí 5), formularios
mínimos, opciones avanzadas plegadas, valores por defecto inteligentes, todo con teclado, acciones
frecuentes en ≤ 3 clics.

Reglas propias de este producto:

- **La aplicación abre en Hoy**, nunca en un panel de gráficas.
- **Cuando Hoy se vacía, se dice y se para.** El vacío es una funcionalidad.
- **Nada se envía en nombre de una persona sin que lo vea antes.**
- **Ningún número sin motivo.** Si no hay al menos una razón que redactar, el Match no se muestra.
- Identidad visual y paleta: §10 bis de [`matchketing.md`](vision.md).

---

## 5. Modelo de dominio

Notación: **Agregado** { campos }. Todo agregado de negocio lleva `empresa_id`.

- **Usuario** { email, hash contraseña, nombre, estado, email verificado } *(global, no por tenant)*
- **Empresa** { nombre, NIF, zona por defecto, ajustes de match }
- **Membresia** { usuario_id, empresa_id, rol, estado }
- **Equipo** { nombre, miembros, provincias asignadas }
- **Contacto** { nombre, email, teléfono, cargo, cuenta_id?, origen, estado
  (lead · cliente · perdido · baja), propietario_id, etiquetas, activo }
- **Cuenta** { nombre, NIF, sector, tamaño, web, dirección, propietario_id } — **opcional**
- **Actividad** { contacto_id, tipo, dirección, cuerpo, resultado, fecha, autor_id } — *append-only*
- **Oportunidad** { contacto_id, cuenta_id?, título, importe estimado, embudo_id, etapa_id, fecha
  prevista de cierre, propietario_id, estado (abierta · ganada · perdida), **motivo de pérdida**,
  cerrada_en }
- **Embudo** { nombre, por_defecto } · **Etapa** { embudo_id, nombre, orden, probabilidad,
  días de aviso }
- **Tarea** { título, contacto_id?, oportunidad_id?, vence_el, responsable_id, estado, origen
  (manual · automática), aplazada_hasta }
- **Senal** { contacto_id, tipo, peso bruto, ocurrida_en, origen } — la materia prima del Momento
- **PuntuacionMatch** { contacto_id, encaje, momento, match, motivos (jsonb), calculada_en }
- **Formulario** { nombre, campos, destino, texto de consentimiento, página de gracias, clave pública }
- **EnvioFormulario** { formulario_id, datos (jsonb), IP, agente, contacto_id resultante }
- **Consentimiento** { contacto_id, finalidad, base legal, canal, otorgado_en, prueba (jsonb),
  retirado_en }
- **RegistroAuditoria** { actor, empresa, entidad, id, acción, cambios (jsonb), en } — *append-only*

**Value objects**: `Email`, `Telefono` (normalizado E.164 con España por defecto), `Nif`,
`Direccion`, `Dinero`, `Puntuacion` (0–100).

**Embudo por defecto** creado con la empresa, 5 etapas: Nuevo (10 %) · Contactado (25 %) ·
Propuesta (50 %) · Negociación (75 %) · Cierre (90 %).

---

## 6. Invariantes y reglas críticas

**Contactos**
- **C1** — Un contacto necesita **al menos un medio de contacto**: email o teléfono.
- **C2** — Email y teléfono se **normalizan** al guardar (minúsculas; E.164 con prefijo `+34` por
  defecto). Sin esto, la deduplicación no funciona.
- **C3** — Duplicado = mismo email normalizado, o mismo teléfono normalizado, dentro de la empresa.
  El sistema **propone** la fusión; **nunca fusiona solo**.
- **C4** — Al fusionar, las actividades se conservan **todas** y se reasignan al superviviente. No se
  pierde historia.
- **C5** — Las actividades son **append-only**: no se editan ni se borran. Una conversación es un
  hecho.

**Embudo**
- **O1** — Cerrar como perdida **exige motivo**. Sin motivo, la transición falla.
- **O2** — El estado se deriva: `abierta` mientras no haya `cerrada_en`.
- **O3** — Una oportunidad ganada o perdida **no se reabre**: se crea otra. El histórico de tasas de
  cierre depende de esto.
- **O4** — La probabilidad **la fija la etapa**, no se edita a mano por oportunidad.
- **O5** — `importe_estimado ≥ 0` y en la moneda de la empresa (EUR en el MVP).

**Match**
- **M1** — **Ningún Match sin al menos un motivo redactable.** Si no hay motivos, se muestra «—».
- **M2** — Sin histórico suficiente (< 20 oportunidades cerradas en la empresa), el **Encaje es 50**
  y se dice explícitamente: *«Todavía sin histórico para calibrar el encaje.»*
- **M3** — El **Momento decae**: ninguna señal pesa igual una semana después (§7).
- **M4** — Un comercial sin histórico **no se penaliza**: arranca con la media del equipo.

**Tareas y Hoy**
- **H1** — Un contacto en estado `lead` o `cliente` **siempre tiene próxima acción**, o aparece
  marcado en Hoy. Es la promesa del producto.
- **H2** — Aplazar exige fecha. No existe «aplazar indefinidamente».

**Multiempresa y cumplimiento**
- **T1** — Aislamiento total por `empresa_id`: **filtro global de EF Core + RLS** en PostgreSQL.
- **T2** — Un usuario solo opera sobre empresas donde tiene `Membresia` activa.
- **G1** — No se envía comunicación comercial a un contacto **sin consentimiento vigente** con base
  legal registrada. El envío falla, no avisa.
- **G2** — La baja es **inmediata e irreversible por el sistema**: solo el contacto puede volver a
  darse de alta.

---

## 7. El motor Match v1

Es el diferenciador, así que se especifica entero. **Sin modelos ni IA en el MVP**: reglas con pesos,
porque tienen que poder explicarse en una frase.

### 7.1 Momento (0–100)

Cada señal aporta puntos brutos que **decaen exponencialmente con semivida de 7 días**:

`aporte = peso × 0,5 ^ (días_transcurridos / 7)`

| Señal | Peso | Tope |
|---|---:|---|
| Formulario enviado | 35 | — |
| Respuesta a un correo | 30 | — |
| Reunión realizada | 30 | — |
| Llamada contestada | 25 | — |
| Oportunidad creada | 20 | — |
| Clic en un enlace | 15 | 3 por día |
| Correo abierto | 8 | 3 por día |
| Visita a una página (contacto conocido) | 6 | 5 por día |
| **Sin ninguna actividad en 30 días** | **−20** | suelo en 0 |

`Momento = min(100, max(0, Σ aportes))`

### 7.2 Encaje (0–100)

Se compara el contacto con el perfil de las oportunidades **ganadas** de esa empresa. Pesos por
defecto, ajustables en Ajustes (plegado):

| Factor | Peso |
|---|---:|
| El sector está entre los 3 con más ganadas | 30 |
| La provincia está entre las que tienen ganadas | 20 |
| El origen del lead convierte por encima de la media | 20 |
| El tamaño encaja con el rango de las ganadas | 15 |
| Calidad del dato (email y teléfono válidos) | 15 |

Con menos de 20 oportunidades cerradas: **Encaje = 50** y se dice (invariante M2).

### 7.3 Match

`Match = redondeo(w × Encaje + (1 − w) × Momento)`, con **w = 0,5** por defecto, ajustable.

### 7.4 La explicación (obligatoria)

Se guardan los **tres factores con más aporte** y se redactan con plantilla:

> **87** · Encaja con tus clientes de hostelería en Valencia · Ha abierto el presupuesto 3 veces esta
> semana · Sin respuesta desde hace 4 días.

Si no hay ningún factor con aporte > 0, no hay número (invariante M1).

### 7.5 Cuándo se recalcula

- **Al llegar una señal** (evento de dominio), de forma síncrona: el contacto queda puntuado al
  instante. Es lo que permite el objetivo de los 60 segundos.
- **Barrido nocturno** de toda la empresa, para aplicar el decaimiento y la penalización por
  inactividad.

### 7.6 Reparto lead ↔ comercial

Para cada comercial con `Membresia` activa y permiso de venta:

| Factor | Puntos |
|---|---:|
| La provincia del lead está en su cartera | 30 |
| Afinidad de sector: su tasa de cierre en ese sector, normalizada | 0–30 |
| Carga: oportunidades abiertas frente a la media del equipo, invertida | 0–20 |
| Velocidad: tiempo medio de primera respuesta en 30 días, invertida | 0–20 |

Gana el de más puntos; empate, el de menos carga. Se registra **por qué**. Sin histórico, media del
equipo (invariante M4).

**Rebote**: si no hay primera acción en **4 horas laborables** (configurable), se reasigna al
siguiente y se avisa. Un lead sin tocar es dinero perdido.

---

## 8. Multiempresa, permisos y auditoría

Idéntico patrón al de ALXOR Core, que ya está probado:

- **Multiempresa**: base compartida, `empresa_id` obligatorio, tenant resuelto del JWT →
  `IContextoEmpresa` (scoped) + `SET app.empresa_actual`. Doble barrera: **filtro global de EF Core**
  y **RLS en PostgreSQL**.
- **Permisos** por código: `contacto.leer`, `contacto.gestionar`, `oportunidad.gestionar`,
  `tarea.gestionar`, `formulario.gestionar`, `informe.leer`, `datos.exportar`, `empresa.ajustes`,
  `usuario.gestionar`. Roles: **Propietario** (todos), **Comercial** (operativa sobre lo suyo y lo de
  su equipo), **Solo lectura** (`*.leer` + `datos.exportar`).
- **Auditoría**: eventos de dominio → `registro_auditoria` en la misma transacción. Se audita:
  fusión de contactos, cambio de propietario, cierre de oportunidad, borrado de contacto, retirada de
  consentimiento, cambios de ajustes de match.

---

## 9. Esquema inicial de base de datos

Convenciones idénticas a ALXOR: PK `uuid` v7, `empresa_id` en toda tabla de negocio,
`creado_en`/`actualizado_en timestamptz`, índices `(empresa_id, …)`, **nombres en español,
`snake_case`**, RLS por `current_setting('app.empresa_actual')`.

| Esquema | Tablas |
|---|---|
| `identidad` | `usuario`, `membresia`, `rol`, `permiso`, `rol_permiso` |
| `organizacion` | `empresa`, `equipo`, `equipo_usuario`, `ajustes_match` |
| `contactos` | `contacto`, `cuenta`, `actividad`, `etiqueta`, `contacto_etiqueta` |
| `embudo` | `embudo`, `etapa`, `oportunidad` |
| `tareas` | `tarea` |
| `match` | `senal`, `puntuacion_match`, `asignacion` |
| `captacion` | `formulario`, `envio_formulario`, `visita_web` |
| `cumplimiento` | `consentimiento`, `baja` |
| `auditoria` | `registro_auditoria` *(append-only)* |

Índices que importan desde el día uno: `contacto (empresa_id, email_normalizado)`,
`contacto (empresa_id, telefono_normalizado)`, `senal (empresa_id, contacto_id, ocurrida_en desc)`,
`oportunidad (empresa_id, etapa_id, estado)`, `tarea (empresa_id, responsable_id, vence_el)`.

---

## 10. API REST (contrato del MVP)

| Método | Ruta | Permiso | Descripción |
|---|---|---|---|
| `POST` | `/auth/registro` · `/auth/login` | — | Alta y JWT |
| `POST` | `/empresas` · `/empresas/{id}/seleccionar` | — | Empresa activa en el token |
| `GET` `POST` `PUT` | `/contactos` · `/contactos/{id}` | `contacto.*` | CRUD |
| `GET` | `/contactos/{id}/cronologia` | `contacto.leer` | Actividades del contacto |
| `POST` | `/contactos/importar` | `contacto.gestionar` | CSV con previsualización |
| `GET` | `/contactos/duplicados` | `contacto.gestionar` | Propuestas de fusión |
| `POST` | `/contactos/{id}/fusionar` | `contacto.gestionar` | Fusiona con otro |
| `POST` | `/contactos/{id}/llamada` | `contacto.gestionar` | Registra resultado de llamada |
| `GET` `POST` `PUT` | `/cuentas` · `/cuentas/{id}` | `contacto.*` | CRUD |
| `GET` | `/embudos` · `/embudos/{id}` | `oportunidad.leer` | Embudo con etapas |
| `GET` `POST` `PUT` | `/oportunidades` | `oportunidad.*` | CRUD |
| `POST` | `/oportunidades/{id}/mover` | `oportunidad.gestionar` | Cambia de etapa |
| `POST` | `/oportunidades/{id}/ganar` · `/perder` | `oportunidad.gestionar` | `perder` exige motivo |
| `GET` `POST` | `/tareas` | `tarea.*` | Tareas |
| `POST` | `/tareas/{id}/completar` · `/aplazar` | `tarea.gestionar` | Aplazar exige fecha |
| `GET` | `/hoy` | `tarea.leer` | **La pila de tarjetas, ordenada por Match** |
| `GET` | `/contactos/{id}/match` | `contacto.leer` | Puntuación con sus motivos |
| `POST` | `/leads` | público con clave | Entrada de lead: puntúa, asigna y crea tarea |
| `GET` `POST` `PUT` | `/formularios` | `formulario.gestionar` | Formularios |
| `POST` | `/f/{clave}` | público | Envío desde la web del cliente |
| `GET` | `/informes/embudo` · `/informes/motivos-perdida` | `informe.leer` | Los dos informes |
| `POST` | `/contactos/{id}/baja` | público con firma | Baja en un clic |
| `GET` `DELETE` | `/cuenta/datos` | `datos.exportar` | Exportación y borrado RGPD |
| `GET` `POST` | `/webhooks` | `empresa.ajustes` | Suscripción a eventos |

Eventos publicados: `contacto.creado`, `contacto.fusionado`, `lead.asignado`,
`oportunidad.ganada`, `oportunidad.perdida`, `match.superado` (umbral configurable).

---

## 11. Cumplimiento (mínimo del MVP)

No es un extra: es lo que permite vender esto en España y es ventaja frente a las herramientas
americanas.

- **Registro de consentimiento** por contacto y finalidad, con base legal, canal, fecha y prueba
  (IP y origen del envío).
- **Bloqueo de envío** sin base legal vigente (invariante G1).
- **Baja en un clic**, con enlace firmado, sin necesidad de iniciar sesión.
- **Exportación y borrado** del contacto y de la empresa, como ya hace ALXOR Core.
- **Retención configurable**: borrado automático de leads no convertidos pasados N meses (por
  defecto 24).
- Datos alojados en la UE.

---

## 12. Arquitectura y estructura

.NET 8 LTS · PostgreSQL · monolito modular · API First · Clean Architecture ligera · DDD práctico ·
EF Core (Npgsql) · JWT · UUID v7 · `Resultado`/`Error` para fallos esperados · `TreatWarningsAsErrors`.

```
src/
  Matchketing.Api                            # REST + OpenAPI + JWT + SPA
  Matchketing.Nucleo                         # Resultado, Error, Dinero, IReloj, IContextoEmpresa
  Matchketing.Identidad(.Infraestructura)
  Matchketing.Organizacion(.Infraestructura)
  Matchketing.Contactos(.Infraestructura)
  Matchketing.Embudo(.Infraestructura)
  Matchketing.Tareas(.Infraestructura)
  Matchketing.Match(.Infraestructura)
  Matchketing.Captacion(.Infraestructura)
  Matchketing.Informes(.Infraestructura)
  Matchketing.Cumplimiento(.Infraestructura)
  Matchketing.Trabajos                       # barrido nocturno, rebote de leads
tests/
  <por módulo>.Tests + Matchketing.IntegrationTests
```

- **Dominio** sin frameworks; **Aplicación** = casos de uso + puertos; **Infraestructura** = EF Core y
  adaptadores; **Api** = REST + OpenAPI + JWT.
- Integraciones (correo, WhatsApp, ALXOR Core) **siempre tras puertos**, con implementación falsa
  para tests.
- **Trabajos en segundo plano**: barrido nocturno de Match y rebote de leads sin atender.
- Coste operativo: un contenedor .NET, un PostgreSQL pequeño.

---

## 13. Orden de desarrollo

Regla estricta heredada: **un módulo a la vez, terminado del todo**. Un módulo no está hecho hasta
que están verdes: dominio · API · persistencia · tests unitarios · tests de integración · documentación.

1. **Núcleo + Identidad + Organización** — multiempresa, RLS, JWT, permisos, auditoría base, CI.
2. **Contactos** — Contacto, Cuenta, Actividad, búsqueda, importación CSV, duplicados y fusión.
3. **Embudo** — Oportunidad, Embudo, Etapa, motivo de pérdida obligatorio, aviso de estancamiento.
4. **Tareas y Hoy** — la pantalla estrella. Sin esto no hay producto.
5. **Match v1** — señales, Encaje, Momento, explicación, reparto y rebote.
6. **Captación** — formulario embebible, consentimiento, seguimiento web, endpoint `/leads`.
7. **Informes** — embudo y motivos de pérdida.
8. **Cumplimiento** — exportación, borrado, retención, baja firmada.

**Ocho módulos.** Es menos de la mitad del alcance que salía del mapeo completo de HubSpot, y sigue
siendo un proyecto comparable a ALXOR Core. Conviene saberlo antes de empezar, no a mitad.

---

## 14. Testing

- **Unitarios**: decaimiento del Momento, cálculo del Encaje sin histórico y con él, redacción de la
  explicación, reparto con empates y con comerciales nuevos, transiciones del embudo, motivo de
  pérdida obligatorio, normalización de teléfono y email, detección de duplicados.
- **Integración** contra PostgreSQL real: aislamiento por empresa en **todas** las tablas, flujo
  completo de `/leads` en menos de un segundo, fusión de contactos sin pérdida de actividades,
  bloqueo de envío sin consentimiento.
- **Prueba de reloj**: el decaimiento se prueba con `IReloj` falso, nunca con esperas reales.
- **Prueba de concurrencia**: dos leads simultáneos no se asignan al mismo comercial saltándose la
  carga.

---

## 15. Riesgos

| Riesgo | Mitigación |
|---|---|
| **El Match no acierta y nadie se fía** | La explicación obligatoria (M1) y el Encaje neutro sin histórico (M2). Es preferible decir «no lo sé» a inventar |
| **Fuga entre empresas** | Doble barrera: filtro global + RLS, y test de aislamiento por cada tabla |
| **La deduplicación fusiona lo que no debe** | Nunca automática: el sistema propone, la persona aprueba (C3) |
| **Hoy se llena de ruido y deja de usarse** | Tope de tarjetas por día y medición del % de acciones hechas desde Hoy |
| **Envío comercial sin base legal** | Bloqueo en la capa de aplicación (G1), no un aviso en la interfaz |
| **El alcance crece por el camino** | El mapeo de HubSpot ya tiene 41 «descartar» escritos con motivo: sirve para decir que no |
| **Depender del correo antes de tiempo** | El correo conectado está fuera del MVP a propósito: es lo que más se rompe |

---

## 16. Criterios de verificación

El MVP está terminado cuando, además de `dotnet build` y `dotnet test` verdes con integración contra
PostgreSQL real y el contrato OpenAPI publicado:

- Un `POST /leads` deja el contacto **puntuado, asignado y con tarea** en **< 1 s**, y el flujo de
  extremo a extremo (formulario en una web real → tarjeta en Hoy) baja de **60 s**.
- Una persona que no ha visto el programa opera la pantalla Hoy **sin preguntar nada**.
- El informe de motivos de pérdida sale de datos reales de una empresa piloto.
- Ninguna consulta devuelve datos de otra empresa, con RLS activo y con RLS desactivado (el filtro de
  EF debe bastar por sí solo).

---

## 17. Después del MVP

Por orden de valor esperado, no de facilidad: **correo conectado** (plantillas y seguimiento) →
**automatizaciones** «si pasa X, haz Y» → **integración con ALXOR Core** → **WhatsApp** → **segmentos
y campañas** → **secuencias** → **Match v2** con pesos aprendidos.

Cada uno entra **solo si lo piden clientes reales**, como manda el principio.
