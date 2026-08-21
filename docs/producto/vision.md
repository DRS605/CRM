# match.keting — Documento de ideas de funcionamiento

> **Producto independiente** del ecosistema ALXOR: repositorio, solución, base de datos y login
> propios. La integración con ALXOR Core es opcional y va por API y eventos.
>
> **Documentos del producto:**
> 1. **`matchketing.md`** (este) — ideas de funcionamiento, visión e identidad visual.
> 2. [`matchketing-funcionalidades.md`](hubspot.md) — las 132 capacidades de
>    HubSpot con un veredicto cada una.
> 3. [`matchketing-diseno-tecnico-funcional.md`](diseno-tecnico-funcional.md) — **el
>    entregable previo al código**: alcance cerrado, decisiones tomadas, dominio, invariantes,
>    esquema, API y orden de módulos.

---

## 0. Qué es y por qué

**match.keting** es el CRM de ALXOR: la herramienta donde una pyme española gestiona **a quién le
va a vender, qué le va a decir y cuándo**. HubSpot es la referencia de calidad (ficha unificada,
embudo visual, automatizaciones, gratis para empezar), pero HubSpot también es el ejemplo de lo que
no queremos: un producto que empieza gratis y sencillo y acaba siendo un panel de control con
setenta menús que necesita un consultor certificado.

La regla de oro de ALXOR se aplica igual aquí: **la simplicidad gana siempre a la cantidad de
funcionalidades**.

**Promesa del producto (criterio de aceptación):** un comercial abre match.keting por la mañana,
ve **una lista de menos de diez acciones**, las hace, y cierra la aplicación. Nada más. Si un día
tiene que pensar "¿y ahora qué miro?", el producto ha fallado.

**Métrica estrella:** desde que entra un lead (formulario, email, llamada, importación) hasta que
está **asignado a una persona, puntuado y con una primera acción propuesta**: **menos de 60
segundos, y sin que nadie lo toque**.

---

## 1. Principios (heredados de ALXOR, sin excepciones)

- La simplicidad gana a la cantidad de funcionalidades.
- Ninguna pantalla intimida. Menú lateral de **menos de 8 opciones**.
- Ninguna acción frecuente pasa de **tres clics**. Todo operable con **teclado**.
- Nunca añadimos algo solo porque HubSpot lo tenga.
- **Cero configuración obligatoria**: funciona con valores por defecto sensatos desde el minuto uno.
  La configuración avanzada existe, pero está plegada.
- **Nada de campos personalizados infinitos.** Un CRM que se puede modelar de cualquier manera es un
  CRM que hay que modelar antes de usarlo. Empezamos con un modelo opinado.
- **Todo lo que el sistema decide, lo explica.** Si puntúa un lead con 87, dice por qué en una
  frase. Sin cajas negras.

---

## 2. Independencia y relación con ALXOR Core

| Regla | Consecuencia |
|---|---|
| Funciona **solo**, sin ALXOR Core | Login propio, base de datos propia, despliegue propio, se puede vender a alguien que factura con otro programa |
| ALXOR Core **no** depende de match.keting | El ERP no importa nada del CRM; si el CRM no existe, el ERP funciona igual |
| La integración es **opcional y por API + eventos** | Se activa en Ajustes pegando una URL y una clave; nada de acoplamiento en el código |
| Sin migraciones al integrar | Si el cliente ya usaba los dos por separado, al conectarlos se enlazan por NIF/email, no se rehacen los datos |

Qué gana el cliente si tiene los dos conectados (y **solo** si los tiene):

- **Oportunidad ganada → presupuesto/factura en ALXOR Core** con un clic, sin retecleo.
- La **ficha del contacto** muestra su historial de facturación: qué compra, cuánto, si paga tarde.
- Señales del ERP alimentan el match: *factura vencida*, *presupuesto enviado sin respuesta*,
  *cliente que no compra desde hace 8 meses*, *presupuesto a punto de caducar*.
- El comercial ve el **margen real** de lo que vende, no solo el importe.

Misma puerta abierta para **Questioner** (una incidencia de calidad es una señal de riesgo de fuga),
**Tuday** y **CostControl**.

**Identidad:** login propio desde el día uno. En el futuro, *ALXOR ID* (SSO común a todos los
productos) como opción, nunca como requisito.

---

## 3. El eje del producto: el *match*

El nombre no es decorativo, es la funcionalidad diferencial. Donde HubSpot te da una base de datos
enorme y tú decides qué hacer, match.keting **empareja** y te propone la jugada:

| Match | Qué empareja | Para qué sirve |
|---|---|---|
| **Contacto ↔ momento** | Un contacto con el instante en que merece la pena llamarle | Es el *Match* (0–100) que ordena el trabajo del día |
| **Lead ↔ comercial** | El lead nuevo con la persona con más probabilidad de cerrarlo | Reparto automático, no una rueda tonta por turnos |
| **Cliente ↔ producto** | Lo que ya tiene con lo que compran clientes parecidos | Venta cruzada con argumento, no a bulto |
| **Contacto ↔ contacto** | Duplicados | Fusión de fichas, la base limpia sola |
| **Mensaje ↔ persona** | La plantilla adecuada al perfil y al momento | Redactar deja de ser el cuello de botella |

### 3.1 La puntuación Match (0–100)

Dos factores, ambos visibles:

- **Encaje (0–100)** — cuánto se parece a tus clientes buenos: sector, tamaño, zona, canal de
  entrada, producto de interés, ticket medio esperado. Se calibra con **tus** oportunidades ganadas
  y perdidas: al principio, reglas con pesos por defecto; a partir de ~200 oportunidades cerradas,
  los pesos se ajustan solos y se avisa de ello.
- **Momento (0–100)** — qué ha pasado últimamente: abrió el email, visitó la página de precios tres
  veces, rellenó un formulario, respondió, el presupuesto caduca el viernes, lleva 40 días sin
  contacto. **Decae con el tiempo** (semivida ~7 días): un lead caliente de hace un mes está frío,
  y el sistema lo sabe.

`Match = w·Encaje + (1−w)·Momento`, con `w = 0,5` por defecto y ajustable en Ajustes (plegado).

**Siempre explicado, en una frase:**
> **87** · Encaja con tus clientes de hostelería en Levante · Ha abierto el presupuesto 3 veces esta
> semana · Caduca en 4 días.

Sin esa frase la puntuación no se muestra. Un número sin motivo no lo usa nadie.

### 3.2 El reparto de leads (lead ↔ comercial)

En vez del *round robin*: para cada lead nuevo se puntúa a cada comercial por **zona**, **afinidad
de sector** (su tasa de cierre histórica con perfiles parecidos), **carga de trabajo actual** y
**velocidad de respuesta** reciente. Se asigna al mejor, se avisa por notificación y **queda dicho
por qué**. Si nadie responde en el plazo configurado, el lead **rebota** al siguiente.

Regla de honestidad: un comercial nuevo no tiene histórico, así que arranca con la media del equipo
y no se le castiga por no tener datos.

---

## 4. La pantalla estrella: **Hoy**

La aplicación **no abre en un panel de gráficas**. Abre en **Hoy**: una pila de tarjetas, ordenada
por Match, con lo que hay que hacer. Una tarjeta = un contacto + por qué ahora + la acción.

```
┌──────────────────────────────────────────────────────────┐
│  Bar Casa Manolo · Valencia                    Match 87  │
│  Abrió tu presupuesto 3 veces esta semana.               │
│  Caduca el viernes.                                      │
│                                                          │
│  [Llamar]  [Enviar recordatorio]  [Aplazar ▾]  [Descartar]│
└──────────────────────────────────────────────────────────┘
```

- **Llamar** abre la ficha con el guion y registra la llamada al colgar (resultado en un clic).
- **Enviar recordatorio** abre el email con la plantilla ya redactada, revisable antes de enviar.
  **Nunca se envía nada solo en nombre de una persona sin que lo vea.**
- **Aplazar** con lenguaje natural: *mañana*, *el lunes*, *en 2 semanas*, *cuando abra el email*.
- Todo con teclado: `J`/`K` para moverse, `L` llamar, `E` email, `A` aplazar, `?` la ayuda.

Cuando la pila se vacía: **"Hecho por hoy"**. Sin más pantallas que mirar. Ese vacío es una
funcionalidad, no un hueco por rellenar.

Menú lateral (**6 opciones**): **Hoy · Contactos · Embudo · Bandeja · Campañas · Informes** (+
Ajustes abajo).

---

## 5. Modelo de dominio (en español, como en ALXOR)

Notación: **Agregado** { campos clave }. Todo lleva `empresa_id` (multiempresa desde el diseño).

- **Contacto** { nombre, email, teléfono, cargo, cuenta_id?, origen, estado (lead/cliente/perdido),
  propietario_id, consentimientos, match (encaje, momento, calculado_en), etiquetas }
- **Cuenta** { nombre, NIF, sector, tamaño, web, dirección, propietario_id } — la empresa a la que
  pertenece el contacto. Opcional: en B2C no se usa y no estorba.
- **Oportunidad** { cuenta/contacto, título, importe estimado, embudo_id, etapa_id, probabilidad
  (derivada de la etapa), fecha prevista de cierre, propietario, estado (abierta/ganada/perdida),
  **motivo de pérdida** } — el motivo de pérdida es **obligatorio** al perder: sin él no hay
  aprendizaje ni informe que valga.
- **Embudo** { nombre, etapas ordenadas } y **Etapa** { nombre, probabilidad, días de aviso de
  estancamiento }. Un embudo por defecto de **5 etapas**: Nuevo · Contactado · Propuesta ·
  Negociación · Cierre. Se pueden crear más embudos, pero no es lo primero que se pide.
- **Actividad** { tipo (email, llamada, reunión, nota, mensaje, evento web, evento ERP), dirección,
  cuerpo, resultado, fecha, autor } — es el **timeline**, el corazón de la ficha.
- **Tarea** { título, contacto/oportunidad, vencimiento, responsable, estado, origen (manual o
  automática) }
- **Segmento** { nombre, criterios, dinámico|estático } — una lista que se recalcula sola.
- **Plantilla** { asunto, cuerpo con variables, estadísticas de uso (apertura, respuesta) }
- **Secuencia** { pasos (email/tarea/espera), condiciones de salida } — *fase 2*.
- **Automatización** { disparador, condiciones, acciones } — el "si pasa X, haz Y".
- **Formulario** { campos, destino, consentimiento, página de gracias } — el captador web.
- **Campaña** { nombre, segmento, plantilla, envío, métricas } — *fase 2*.
- **Consentimiento** { contacto, base legal, finalidad, canal, fecha, prueba (IP/origen), retirada } —
  no es un `bool`, es un registro con historia. En España esto no es opcional.
- **Ticket** { contacto, asunto, estado, prioridad } — *fase 3*, solo si lo piden clientes reales.

**Datos que nunca se pierden:** las actividades son *append-only*, como el registro de auditoría del
ERP. Una conversación no se edita.

---

## 6. Bloques funcionales

### 6.1 Contactos y cuentas — la ficha unificada

Lo mejor de HubSpot y lo que hay que copiar sin complejos: **una sola pantalla con todo lo que ha
pasado con esta persona**, en orden cronológico: emails, llamadas, reuniones, notas, formularios,
visitas web, y —si hay ERP conectado— presupuestos, facturas y cobros.

- Cabecera con lo que importa: nombre, empresa, teléfono con clic-para-llamar, propietario, Match
  con su motivo, y el **siguiente paso** (si no hay siguiente paso, aviso en rojo: un contacto sin
  próxima acción es un contacto que se pierde).
- **Búsqueda global instantánea** (`/`) por nombre, email, teléfono, empresa o NIF.
- **Importación CSV** con previsualización y detección de duplicados, igual que
  `/clientes/importar` en ALXOR: mismo patrón, misma calidad.
- **Fusión de duplicados** propuesta por el sistema, aprobada por la persona.
- **Enriquecimiento ligero y honesto**: del dominio del email se deduce la web y el nombre de la
  empresa. Nada de comprar bases de datos de terceros.

### 6.2 Embudo (kanban)

- Tablero de columnas por etapa, arrastrar y soltar, **suma en la cabecera de cada columna**.
- **Alerta de estancamiento**: una oportunidad que lleva más días de los previstos en una etapa se
  marca; si sigue, genera tarea automática.
- Cerrar como perdida **exige motivo** (lista corta: precio, plazo, competencia, no era el momento,
  no contesta, otro). De ahí sale el informe más útil que tendrá el gerente.
- Ganar una oportunidad: si hay ALXOR Core conectado, ofrece **"Crear presupuesto"** en el mismo
  diálogo.

### 6.3 Comunicación: la Bandeja

- **Conexión de correo real** (Google / Microsoft por OAuth, o IMAP+SMTP genérico). Los emails con
  clientes aparecen solos en el timeline; el resto, no. **No leemos el buzón entero**: solo lo que
  cruza con un contacto del CRM, y se dice claramente.
- **Seguimiento de aperturas y clics**, con interruptor por email y aviso claro de que se está
  usando (y respetando la elección del destinatario).
- **Plantillas** con variables y **estadísticas reales**: qué asunto se abre, qué texto se responde.
- **WhatsApp Business** (fase 2) — en España pesa más que el email en muchos sectores; se integra
  vía API oficial con plantillas aprobadas, no con trucos.
- **Registro de llamadas en un clic** con resultado (contactado / no contesta / no interesa /
  volver a llamar + fecha).

### 6.4 Captación

- **Formulario embebible** con un `<script>` de una línea. Campos mínimos, consentimiento
  obligatorio, mensaje de gracias configurable.
- **Seguimiento web** del mismo script: qué páginas visita un contacto conocido. Solo con
  consentimiento de cookies del visitante, y desactivable.
- **Enlace de reuniones** (fase 2): página pública con la disponibilidad del comercial; reservar
  crea contacto, reunión y tarea.
- **Códigos QR / enlaces de campaña** con origen: para ferias, folletos y visitas comerciales. Muy
  útil en pyme española real.

### 6.5 Automatización — el "si pasa X, haz Y"

Un solo concepto, con lenguaje natural en la interfaz. Nada de lienzos de diagramas de flujo con
ramas anidadas (ahí es donde HubSpot deja de ser sencillo).

```
Cuando   un contacto rellena el formulario "Presupuesto web"
si       es de la Comunidad Valenciana
entonces asignar a la persona con mejor match
         + crear tarea "Llamar" para hoy
         + enviar el email "Gracias, te llamamos"
```

- Disparadores: formulario, email abierto/respondido, cambio de etapa, oportunidad ganada/perdida,
  inactividad de N días, fecha (cumpleaños de alta, aniversario), señal del ERP.
- Acciones: asignar, crear tarea, enviar email/plantilla, cambiar etapa, añadir a segmento, avisar
  por notificación, llamar a un webhook.
- **Simulación antes de activar**: "esta automatización habría afectado a 34 contactos el mes
  pasado". Nadie enciende algo a ciegas.
- **Límite deliberado**: 3 condiciones y 5 acciones por automatización. Si necesitas más, es que el
  proceso está mal.

### 6.6 Marketing (fase 2, sin convertirse en Mailchimp)

- **Segmentos dinámicos** con los mismos criterios que la búsqueda.
- **Envío a segmento** con plantilla, medición de aperturas/clics/bajas y **límite de frecuencia**
  (nadie recibe más de N correos al mes; es respeto y es entregabilidad).
- **Baja en un clic** en todos los envíos, obligatoria y no eliminable.
- Nada de constructor de páginas de aterrizaje ni gestor de redes sociales. Eso es otro producto.

### 6.7 Informes

Cinco, no cincuenta:

1. **Embudo**: qué hay abierto, por etapa e importe; conversión entre etapas.
2. **Previsión**: importe ponderado por probabilidad para el mes y el trimestre.
3. **Motivos de pérdida**: por qué se pierde, en orden.
4. **Actividad por comercial**: llamadas, emails, reuniones, y **tiempo de primera respuesta** (el
   indicador que más ventas mueve y que casi nadie mide).
5. **Origen**: qué canal trae leads y cuáles se convierten en dinero (con ERP conectado, en dinero
   cobrado de verdad, no en dinero prometido).

Todos exportables a CSV. Sin constructor de informes a medida en el MVP.

---

## 7. Cumplimiento (España) — de serie, no como extra de pago

Esto no es burocracia, es una **ventaja competitiva** frente a herramientas americanas:

- **Registro de consentimiento** por contacto y finalidad, con fecha, canal y prueba de origen.
- **Base legal** explícita en cada envío comercial (consentimiento o interés legítimo), y bloqueo de
  envío a quien no la tenga.
- **Doble opt-in** opcional en formularios.
- **Baja** en cada comunicación, con efecto inmediato y a prueba de errores.
- **Derechos ARCO/RGPD**: exportación y borrado del contacto en un clic, como ya hace ALXOR Core.
- **Retención configurable**: borrado automático de leads no convertidos pasados N meses.
- Datos alojados en la UE.

---

## 8. Arquitectura técnica (propuesta)

Mismo criterio que ALXOR Core, porque funciona y porque el equipo ya lo domina:

- **.NET 8 LTS + PostgreSQL**, monolito modular, **Clean Architecture ligera**, API First (OpenAPI),
  EF Core (Npgsql), JWT, UUID v7, `Resultado`/`Error` en vez de excepciones para fallos esperados,
  `TreatWarningsAsErrors`.
- **Multiempresa** con `empresa_id` obligatorio, filtro global de EF Core **+ RLS** en PostgreSQL.
- **Repositorio, solución y base de datos propios.** Nada compartido con el ERP salvo, si acaso, un
  paquete NuGet con el `Nucleo` (`Resultado`, `Dinero`, `IReloj`) publicado aparte. Si eso genera la
  menor atadura, se duplica el código: la independencia vale más que no repetirse.
- **Integraciones tras puertos**: correo, WhatsApp, ALXOR Core, calendario. Todo adaptador
  sustituible, todo con implementación falsa para tests.
- **Eventos + webhooks salientes** para que otros productos escuchen (`oportunidad.ganada`,
  `contacto.creado`, `match.superado`).
- **Trabajos en segundo plano** para lo que no puede ir en la petición: recálculo de Match, secuencias,
  envíos, sincronización de correo.
- **Tests desde el día uno**: unitarios de dominio (puntuación, reparto, transiciones de etapa,
  consentimiento) e integración contra PostgreSQL real.
- Coste operativo bajo: un contenedor .NET, un PostgreSQL pequeño, un worker.

Estructura, con la misma nomenclatura:

```
src/
  Matchketing.Api                       # REST + OpenAPI + JWT + SPA
  Matchketing.Nucleo                    # Resultado, Dinero, IReloj, ContextoEmpresa
  Matchketing.Identidad(.Infraestructura)
  Matchketing.Contactos(.Infraestructura)      # Contacto, Cuenta, Actividad
  Matchketing.Embudo(.Infraestructura)         # Oportunidad, Embudo, Etapa
  Matchketing.Tareas(.Infraestructura)
  Matchketing.Match(.Infraestructura)          # motor de puntuación y reparto
  Matchketing.Comunicacion(.Infraestructura)   # email, plantillas, tracking
  Matchketing.Captacion(.Infraestructura)      # formularios, seguimiento web
  Matchketing.Automatizacion(.Infraestructura)
  Matchketing.Informes(.Infraestructura)
  Matchketing.Integraciones(.Infraestructura)  # ALXOR Core y otros, tras puertos
```

---

## 9. Modelo de negocio

Copiamos de HubSpot lo único que hay que copiar de su comercialización: **el plan gratuito de
verdad**, no una prueba de 14 días.

| Plan | Qué incluye | Idea de precio |
|---|---|---|
| **Gratis** | 1 usuario, contactos ilimitados, embudo, tareas, Hoy, formulario, informes básicos | 0 € |
| **Equipo** | Varios usuarios, reparto por match, automatizaciones, correo conectado, plantillas | por usuario/mes |
| **Pro** | Campañas, segmentos, secuencias, WhatsApp, integración ALXOR Core, API abierta | por usuario/mes |

Reglas: **el precio nunca depende del número de contactos** (el castigo por crecer que todos odian
de HubSpot), no se cobra por "quitar la marca", y los datos se exportan enteros siempre, también en
el plan gratuito.

---

## 10. MVP y orden de trabajo

Se mantiene el método de ALXOR: **un módulo a la vez, terminado del todo** (dominio · API ·
persistencia · tests unitarios · tests de integración · documentación) antes de empezar el
siguiente.

**match.keting Start (MVP):**

1. **Núcleo + Identidad** (registro, login, JWT, usuarios, multiempresa, RLS, CI).
2. **Contactos** (Contacto, Cuenta, Actividad/timeline, búsqueda, importación CSV, duplicados).
3. **Embudo** (Oportunidad, etapas, kanban, motivo de pérdida, estancamiento).
4. **Tareas y Hoy** (la pantalla estrella; sin esto el producto no existe).
5. **Match v1** (encaje + momento con reglas explicables, reparto de leads).
6. **Comunicación** (correo conectado, plantillas, seguimiento, registro de llamadas).
7. **Captación** (formulario embebible + consentimiento + seguimiento web).
8. **Automatización** (si pasa X, haz Y, con simulación).
9. **Informes** (los cinco de §6.7).
10. **Cumplimiento** (consentimientos, bajas, exportación/borrado, retención).

**Fase 2:** secuencias · segmentos y campañas · WhatsApp · enlace de reuniones · integración con
ALXOR Core · Match v2 (pesos aprendidos).

**Fase 3, solo si lo piden clientes reales:** tickets de soporte · chat en la web · aplicación móvil ·
recomendación de producto (cliente ↔ producto).

---

## 10 bis. Identidad visual (paleta magenta)

Idea rectora: **un solo color con voz y el resto en voz baja**. El magenta no decora, marca la
siguiente acción. Los grises llevan un sesgo de ciruela para convivir con él sin ensuciarlo.

**El punto del nombre es el logotipo.** No hace falta símbolo aparte: el punto que separa *match* de
*keting* es el mismo que marca una sección, un dato en vivo o un contacto sin próximo paso.

| Token | Claro | Oscuro | Uso |
|---|---|---|---|
| `--magenta` | `#5C2340` | `#C89BB4` | El punto, la línea del elemento activo, lo que avisa. **No** el fondo de los botones |
| `--magenta-hover` | `#3F1730` | `#DEBACD` | *Hover* y pulsado |
| `--magenta-velo` | `#F4EFF1` | `#211619` | Fondo teñido, muy de vez en cuando |
| `--tinta` | `#191316` | `#F2ECEE` | Texto, logotipo **y la acción principal** |
| `--grafito` | `#6E6167` | `#9C8F95` | Texto secundario y estado «perdida» |
| `--papel` | `#F7F4F2` | `#0C0809` | Fondo de página |
| `--superficie` | `#FFFFFF` | `#131011` | Tarjetas |

> **El magenta se cambió por ciruela, y el token conserva el nombre.** El tono anterior (`#D4006E`) era
> correcto en contraste y equivocado en registro: saturado, plano y repartido a partes iguales por toda
> la pantalla —botones rellenos, pastillas de fondo, cifras enormes— hacía que una herramienta de trabajo
> pareciera una aplicación de colorear. Lo que se arregló no fue el tono: fue **cuánto se usa**. El
> acento pasó de rellenar bloques a señalar, la acción principal se fue a tinta, los radios bajaron de
> 9–12 px a 3–4 px, las pastillas de 999 px desaparecieron y los titulares y las cifras pasaron a la
> serifa que ya estaba declarada. Renombrar el token a `--ciruela` habría tocado doscientas referencias
> para no cambiar nada.
| `--trazo` | `#E9DDE3` | `#3A2430` | Filetes y bordes |
| `--turquesa` | `#00767F` | `#3ED0D8` | **Uso único**: el factor *Momento* del Match |
| `--verde` | `#0E7C5A` | `#2BB98A` | Ganada, cobrado |
| `--ambar` | `#A66A00` | `#E0A83C` | Estancada, caduca |

**No hay rojo en el sistema**: junto al magenta es ilegible. Los avisos van en ámbar y en texto.

**Reglas del magenta.** Sí: un único botón principal por pantalla, la cifra de Match, el punto, la
pestaña activa (en rosa velo, no en magenta pleno) y los enlaces. No: fondos grandes, degradados,
cabeceras a sangre, errores, texto largo, ni tocando al turquesa o al verde.

**Tipografía**: sin fuentes descargadas (la aplicación arranca instantánea y se ve igual en cualquier
equipo). Tres papeles — titular en sans a peso 800 con interletraje muy cerrado (−0,035 em),
afirmaciones en serif, y datos en monoespaciada con **números tabulares**. La personalidad la da el
tratamiento, no una fuente exótica.

El color nunca es el único portador de información: cada estado lleva punto, palabra y posición.

---

## 11. Lo que NO vamos a hacer (anti-alcance)

Escrito aquí para poder decir que no dentro de un año:

- Constructor de páginas de aterrizaje ni CMS.
- Gestor de redes sociales.
- Campos y objetos personalizados ilimitados.
- Lienzo de automatizaciones con ramas anidadas.
- Puntuación de leads que no se pueda explicar en una frase.
- Envíos masivos sin base legal registrada.
- Marcador telefónico propio (integramos con el que ya tenga el cliente).
- Contabilidad, facturación o stock: **eso es ALXOR Core**, y la frontera no se cruza.

---

## 12. Métricas de éxito del producto

- **< 60 s** desde que entra un lead hasta que está asignado, puntuado y con acción propuesta.
- **< 5 min** para que un comercial nuevo entienda la pantalla Hoy sin formación.
- **> 70 %** de las acciones del día hechas desde Hoy (si la gente se va a los listados, Hoy no
  sirve).
- **0** contactos activos sin próxima acción.
- Mejora medible del **tiempo de primera respuesta** del equipo en el primer mes de uso.

---

## 13. Preguntas abiertas — **resueltas**

Todas están cerradas en el §1 de
[`matchketing-diseno-tecnico-funcional.md`](diseno-tecnico-funcional.md). En resumen:

1. **Público**: micropyme española de 1–10 personas, con 1–3 comerciales.
2. **B2B y B2C**: los dos; `Cuenta` es opcional.
3. **WhatsApp**: fuera del MVP, a F2 (API oficial y coste por conversación son un módulo entero).
4. **Repositorio y despliegue separados** desde el día uno: sí.
5. **Marca**: registrar `matchketing.com` / `.es`; **match.keting** solo como grafía visual.
6. **`Nucleo` de ALXOR**: se **duplica**, no se comparte. Son ~400 líneas y la independencia vale más
   que no repetirse.

Añadidas y también cerradas: **alcance del MVP** (cinco bloques, no las 44 funcionalidades del mapeo
completo), **correo conectado** fuera del MVP, e **identidad** con login propio.
