# match.keting — Catálogo de funcionalidades (mapeo completo de HubSpot)

> Complementa a [`vision.md`](vision.md).
> Aquí está **todo** lo que hace HubSpot hoy (seis hubs + Smart CRM + Breeze AI) traducido a
> match.keting, sin omitir nada. Inventario verificado en agosto de 2026.

---

## 0. Cómo leer esto

Copiar HubSpot entero choca con el principio de ALXOR de *«nunca añadiremos una funcionalidad solo
porque otro la tenga»*. Este documento no resuelve esa tensión: la **pone encima de la mesa con
todas las piezas a la vista**, para que recortar sea una decisión tomada y no un olvido.

HubSpot ha tardado ~19 años y varios miles de personas en construir este catálogo. El valor de la
tabla no es la lista: es el **veredicto de cada fila**.

| Veredicto | Significado |
|---|---|
| **Copiar** | Lo hacemos igual. HubSpot lo tiene bien resuelto y no hay nada que mejorar. |
| **Adaptar** | Lo hacemos, pero a nuestra manera: más simple, más opinado o con el giro del *match*. |
| **Delegar** | Existe en el ecosistema ALXOR, pero **no en match.keting**: es de ALXOR Core u otro producto. La frontera no se cruza. |
| **Descartar** | No lo hacemos, y la columna dice por qué. |

| Fase | Qué es |
|---|---|
| **MVP** | match.keting Start. Sin esto el producto no existe. |
| **F2** | match.keting Pro. Cuando haya clientes de pago. |
| **F3** | Solo si lo piden clientes reales. |

---

## 1. Smart CRM — el núcleo gratuito

Es la base sobre la que HubSpot monta los seis hubs, y es la parte que hay que copiar casi entera:
está bien pensada y es lo que la gente espera de un CRM.

| Funcionalidad de HubSpot | En match.keting | Veredicto | Fase |
|---|---|---|---|
| Contactos ilimitados | Contacto | **Copiar** — y sin cobrar por volumen, nunca | MVP |
| Empresas (*companies*) | Cuenta | **Copiar** — opcional, no estorba en B2C | MVP |
| Negocios y embudos (*deals & pipelines*) | Oportunidad, Embudo, Etapa | **Copiar** | MVP |
| Tickets de soporte | Ticket | **Adaptar** — versión mínima, sin help desk completo | F3 |
| Tareas | Tarea | **Copiar** | MVP |
| Cronología de actividad (*timeline*) | Actividad (*append-only*) | **Copiar** — es el corazón de la ficha | MVP |
| Notas y menciones a compañeros | Nota con `@` | **Copiar** | MVP |
| Propiedades personalizadas | Campos extra por objeto | **Adaptar** — máximo 10 por objeto, y con aviso al llegar | F2 |
| Objetos personalizados (*custom objects*) | — | **Descartar** — modelar antes de usar mata la promesa de «sin manual» | — |
| Asociaciones entre registros y etiquetas de asociación | Contacto ↔ Cuenta ↔ Oportunidad | **Adaptar** — las tres relaciones útiles, fijas y sin configurar | MVP |
| Listas estáticas y activas | Segmento (estático / dinámico) | **Copiar** | F2 |
| Vistas guardadas y filtros por usuario | Vistas guardadas | **Copiar** | MVP |
| Importación y exportación CSV | Importación con previsualización | **Copiar** — mismo patrón que `/clientes/importar` de ALXOR | MVP |
| Deduplicación y fusión de registros | Match contacto ↔ contacto | **Adaptar** — el sistema propone, la persona aprueba | MVP |
| Historial de cambios de propiedades | Registro de auditoría | **Copiar** — ya sabemos hacerlo | MVP |
| Usuarios, equipos y permisos | Usuario, Equipo, Rol | **Copiar** | MVP |
| Particiones de datos por equipo (*partitioning*) | Visibilidad por equipo | **Adaptar** — solo «todo» o «lo mío y lo de mi equipo» | F2 |
| Sandbox y entornos de prueba | — | **Descartar** — una pyme de 8 personas no tiene entorno de pruebas | — |
| Paneles e informes | Informes (los cinco de §6.7) | **Adaptar** | MVP |
| Constructor de informes a medida | — | **Descartar** en MVP; exportación a CSV cubre el caso raro | F3 |
| Aplicación móvil iOS/Android | App móvil | **Adaptar** — solo «Hoy» + ficha + registro de llamada. Nada más | F3 |
| Multidivisa | Moneda por oportunidad | **Adaptar** — EUR por defecto; multidivisa solo si hay demanda | F3 |
| Multiidioma de interfaz | Español | **Descartar** en MVP — el dominio es España | F3 |

---

## 2. Sales Hub — ventas

El hub más cercano al corazón de match.keting. Aquí está también lo que más nos ha influido: el
*Prospecting Workspace* de HubSpot es el pariente lejano de nuestra pantalla **Hoy**, pero es una
cola de tareas; la nuestra decide el orden y explica el porqué.

| Funcionalidad de HubSpot | En match.keting | Veredicto | Fase |
|---|---|---|---|
| Embudos múltiples y etapas configurables | Embudo, Etapa | **Copiar** — uno por defecto de 5 etapas, más si hacen falta | MVP |
| Probabilidad por etapa | Probabilidad derivada de la etapa | **Copiar** | MVP |
| Tablero kanban con arrastrar y soltar | Embudo | **Copiar** | MVP |
| Motivo de cierre perdido | Motivo de pérdida | **Adaptar** — **obligatorio**, no opcional. Alimenta el informe más útil | MVP |
| *Prospecting Workspace* (cola diaria del comercial) | **Hoy** | **Adaptar** — es nuestra pantalla estrella, ordenada por Match y explicada | MVP |
| Puntuación de leads (reglas) | Match: Encaje | **Adaptar** — con explicación obligatoria en una frase | MVP |
| Puntuación predictiva de leads (IA, Enterprise) | Match v2 con pesos aprendidos | **Adaptar** — se calibra con tus ganadas/perdidas a partir de ~200 cierres | F2 |
| Rotación y asignación de leads (*round robin*) | Match lead ↔ comercial | **Adaptar** — por zona, afinidad de sector, carga y tasa de cierre. **Nuestro diferenciador** | MVP |
| Seguimiento de apertura y clic de correo | Seguimiento de envíos | **Copiar** — con interruptor por correo | MVP |
| Notificaciones en tiempo real de actividad | Avisos | **Copiar** | MVP |
| Plantillas de correo con variables | Plantilla | **Copiar** | MVP |
| Estadísticas de plantilla (apertura, respuesta) | Estadísticas de plantilla | **Copiar** — dice qué asunto funciona de verdad | MVP |
| Fragmentos (*snippets*) | Fragmentos | **Copiar** — barato y se usa mucho | F2 |
| Biblioteca de documentos con seguimiento | Documentos compartidos | **Adaptar** — enlace con aviso de apertura; sin biblioteca completa | F2 |
| Secuencias de correo automatizadas | Secuencia | **Adaptar** — máximo 5 pasos, salida automática al responder | F2 |
| Llamadas desde el navegador (VoIP) | — | **Descartar** — integramos con la centralita que ya tenga el cliente | — |
| Registro y grabación de llamadas | Registro de llamada en un clic | **Adaptar** — resultado en un clic; grabación no | MVP |
| Transcripción e inteligencia de conversación | — | **Descartar** — coste alto, valor dudoso en equipos de 3 personas | — |
| *Coaching* de comerciales sobre llamadas | — | **Descartar** — es producto de empresa grande | — |
| Enlaces de reunión y calendario | Enlace de reuniones | **Copiar** | F2 |
| Reparto rotativo de reuniones (*round robin meetings*) | Reunión con match de comercial | **Adaptar** | F2 |
| *Playbooks* (guiones de venta) | Guion en la tarjeta de Hoy | **Adaptar** — el guion aparece al llamar, no en un menú aparte | F2 |
| Previsión de ventas (*forecasting*) | Informe de previsión ponderada | **Adaptar** — importe × probabilidad, sin IA ni submisiones de previsión | MVP |
| Objetivos (*goals*) por comercial | Objetivo mensual | **Adaptar** — un número, no un módulo | F2 |
| Inspección de negocios y *deal score* | Alerta de estancamiento | **Adaptar** — más simple y más accionable | MVP |
| Seguimiento de ingresos recurrentes (MRR/ARR) | — | **Delegar** — es facturación: ALXOR Core | — |
| Herramientas ABM (*account-based marketing*) | — | **Descartar** — no aplica a la micropyme española | — |
| Suite de analítica de ventas | Informes de actividad y origen | **Adaptar** — cinco informes, no cincuenta | MVP |
| Extensión de Gmail y Outlook | Extensión de navegador | **Copiar** — sin esto, el comercial no registra nada | F2 |

---

## 3. Marketing Hub — captación y comunicación

Aquí es donde HubSpot es más grande y donde hay que recortar más: buena parte de este hub es otro
producto (un Mailchimp, un gestor de redes, un CMS).

| Funcionalidad de HubSpot | En match.keting | Veredicto | Fase |
|---|---|---|---|
| Marketing por correo con editor visual | Campaña a segmento | **Adaptar** — plantillas buenas por defecto, sin editor de arrastrar bloques | F2 |
| Pruebas A/B de correo | Prueba A/B de asunto | **Adaptar** — solo el asunto, que es el 80 % del efecto | F2 |
| Contenido inteligente / personalización | Variables en plantilla | **Adaptar** — variables sí; contenido condicional no | F2 |
| Automatización de marketing (*workflows*) | Automatización «si pasa X, haz Y» | **Adaptar** — 3 condiciones y 4 acciones. **Sin lienzo de ramas** | F2 |
| Segmentación por listas | Segmento dinámico | **Copiar** | F2 |
| Formularios (embebido, emergente, independiente) | Formulario embebible | **Adaptar** — embebido y emergente; el independiente no | MVP |
| CTAs (botones de llamada a la acción) | — | **Descartar** — resuelto con un enlace normal | — |
| Páginas de aterrizaje con editor | — | **Descartar** — es un CMS. Su web ya existe | — |
| Blog | — | **Descartar** — mismo motivo | — |
| SEO y grupos temáticos | — | **Descartar** — otro oficio, otro producto | — |
| Analítica de tráfico web | Seguimiento de contactos conocidos | **Adaptar** — solo qué visita **un contacto ya identificado**, que es lo que mueve una venta | MVP |
| Anuncios (Google, Meta, LinkedIn) y audiencias sincronizadas | Origen de campaña con enlaces UTM | **Adaptar** — medimos el origen; no gestionamos la publicidad | F2 |
| Gestión y programación de redes sociales | — | **Descartar** — hay diez herramientas mejores y más baratas | — |
| Campañas (agrupar activos bajo una iniciativa) | Campaña | **Adaptar** — agrupa origen, segmento y envío; sin gestor de activos | F2 |
| Atribución de ingresos multicontacto | Informe de origen | **Adaptar** — primer y último contacto. Con ALXOR Core conectado, atribuye a **dinero cobrado**, no prometido | F2 |
| Analítica de recorrido del cliente | — | **Descartar** — pantalla bonita que nadie usa dos veces | — |
| Eventos de marketing y seminarios web | — | **Descartar**; el QR de feria cubre el caso real | — |
| Alojamiento y seguimiento de vídeo | — | **Descartar** | — |
| SMS | — | **Descartar** — en España pesa poco frente a WhatsApp | — |
| Canal de WhatsApp | WhatsApp Business | **Copiar** — en España es **más importante que el correo** en muchos sectores | F2 |
| Captación y rotación de leads | Match lead ↔ comercial | **Adaptar** — ver §2 | MVP |
| Correo transaccional | Correo del sistema | **Copiar** | MVP |

---

## 4. Service Hub — postventa

Solo si lo piden clientes reales. Un CRM que se convierte en centro de soporte deja de ser un CRM.

| Funcionalidad de HubSpot | En match.keting | Veredicto | Fase |
|---|---|---|---|
| Tickets y embudos de ticket | Ticket | **Adaptar** — un embudo, cuatro estados | F3 |
| Espacio de trabajo *Help Desk* | — | **Descartar** — es un producto entero | — |
| Bandeja compartida omnicanal | Bandeja | **Adaptar** — correo y WhatsApp; el resto no | F2 |
| Chat en vivo en la web | Chat | **Adaptar** — un chat simple que crea contacto | F3 |
| Bots conversacionales | — | **Descartar** — mal chatbot resta más de lo que suma | — |
| Base de conocimiento | — | **Descartar** — es contenido, y el contenido es otro producto | — |
| Portal del cliente | — | **Descartar** en match.keting | — |
| Encuestas NPS, CSAT y CES | Encuesta de una pregunta | **Adaptar** — se envía tras ganar; alimenta el Encaje | F3 |
| SLA y escalado | — | **Descartar** — no hay SLA en una pyme de 8 personas | — |
| Enrutamiento de conversaciones | Asignación por match | **Adaptar** | F2 |
| Informes de servicio | — | **Descartar** | — |

---

## 5. Content Hub — contenido y web

**Descartado en bloque.** Es un CMS y un generador de contenido: otro producto, otro oficio, otro
equipo. Se documenta para dejar constancia de que la decisión es deliberada.

| Funcionalidad de HubSpot | Veredicto |
|---|---|
| Alojamiento web, temas y editor de arrastrar y soltar | **Descartar** |
| Blog, páginas de aterrizaje, contenido dinámico | **Descartar** |
| Pódcast, casos de estudio, *content remix* | **Descartar** |
| Áreas privadas y membresías | **Descartar** |
| Traducciones multiidioma del sitio | **Descartar** |
| CDN, SSL y seguridad web | **Descartar** |
| Recomendaciones de SEO | **Descartar** |
| Funciones sin servidor y plantillas HubL | **Descartar** |
| Pruebas A/B y adaptativas de página | **Descartar** |
| Agente de contenido con IA y voz de marca | **Descartar** |

Lo único que se rescata: el **script de una línea** que se pega en la web del cliente, que aquí sirve
para el formulario y para saber qué visita un contacto conocido.

---

## 6. Data Hub — datos e integraciones

| Funcionalidad de HubSpot | En match.keting | Veredicto | Fase |
|---|---|---|---|
| Sincronización bidireccional con otras herramientas | Integración con ALXOR Core | **Adaptar** — un conector bueno vale más que veinte a medias | F2 |
| Automatización programable (código en flujos) | Acción «llamar a un webhook» | **Adaptar** | F2 |
| Webhooks entrantes y salientes | Eventos y webhooks | **Copiar** — es como se integran los demás productos ALXOR | MVP |
| Calidad de datos: formateo automático | Normalización de teléfono, NIF y correo al guardar | **Copiar** — barato y se nota mucho | MVP |
| Deduplicación automática | Match contacto ↔ contacto | **Copiar** | MVP |
| *Datasets* reutilizables para informes | — | **Descartar** — presupone un analista | — |
| Conexión con almacén de datos (Snowflake y similares) | — | **Descartar** | — |
| Enriquecimiento de datos desde terceros | Deducción del dominio del correo | **Adaptar** — de lo que ya tenemos. **No compramos bases de datos** | F2 |
| Mapeo de campos personalizados | Mapeo en la importación CSV | **Copiar** | MVP |
| API pública | API REST con OpenAPI | **Copiar** — API First, como todo ALXOR | MVP |
| Mercado de aplicaciones (1.700+) | — | **Descartar** — no hay ecosistema que construir todavía | F3 |

---

## 7. Revenue Hub — cotizaciones, facturas y cobros

**Delegado en bloque a ALXOR Core.** Es exactamente la frontera que no se cruza: si match.keting
factura, empieza a competir con vuestro propio ERP y ambos productos pierden.

| Funcionalidad de HubSpot | Dónde vive en ALXOR | Veredicto |
|---|---|---|
| Presupuestos (*quotes*) | Módulo Presupuestos de ALXOR Core | **Delegar** — «Oportunidad ganada → Crear presupuesto» en un clic |
| Facturas | Módulo Facturación | **Delegar** |
| Enlaces de pago | Tesorería | **Delegar** |
| Suscripciones y cobro recurrente | Facturación recurrente | **Delegar** |
| Pasarela de pago (Stripe y similares) | Tesorería | **Delegar** |
| Cálculo de impuestos | Facturación (IVA, IRPF, recargo) | **Delegar** — y lo hacemos mejor: es fiscalidad española real |
| Firma electrónica en presupuestos | Documentos | **Delegar** (F3 en ALXOR) |
| Productos y líneas de artículo | Catálogo | **Delegar** — match.keting los lee, no los mantiene |
| Informes de facturación | Informes | **Delegar** |

Sin ALXOR Core conectado, match.keting registra el **importe estimado** de la oportunidad y ahí se
detiene. Es una limitación honesta, y también el mejor argumento comercial para vender los dos.

---

## 8. Breeze AI — la capa de inteligencia

HubSpot ha puesto IA en todas partes. Aquí conviene ser selectivo: la IA que no se puede explicar
rompe nuestro principio de *«todo lo que el sistema decide, lo explica»*.

| Funcionalidad de HubSpot | En match.keting | Veredicto | Fase |
|---|---|---|---|
| Breeze Copilot (asistente en toda la interfaz) | — | **Descartar** en MVP — un asistente que no conoce tu negocio es un adorno caro | F3 |
| Agente de prospección (busca y contacta solo) | — | **Descartar** — enviar correos en frío en nombre del cliente es un riesgo legal y reputacional | — |
| Agente de atención al cliente | — | **Descartar** | — |
| Agente de contenido | — | **Descartar** | — |
| Agente de datos (limpieza automática) | Normalización y duplicados por reglas | **Adaptar** — reglas explicables, sin modelo | MVP |
| *Buyer intent* (identificar empresas que te visitan) | Señales de Momento | **Adaptar** — de contactos conocidos, no de visitantes anónimos identificados por IP | MVP |
| Enriquecimiento de datos con IA | Deducción del dominio | **Adaptar** | F2 |
| Acortado inteligente de formularios | Formulario que no repregunta lo que ya sabe | **Copiar** — sube la conversión y es fácil | F2 |
| Redacción asistida de correos | Borrador de correo desde la ficha | **Adaptar** — **siempre revisable antes de enviar**, nunca automático | F2 |
| Resumen de la ficha del contacto | Resumen de la conversación | **Adaptar** — útil de verdad cuando hay 40 actividades | F2 |
| Previsión con IA | — | **Descartar** — previsión ponderada por etapa, explicable | — |

---

## 9. Plataforma

| Funcionalidad de HubSpot | En match.keting | Veredicto | Fase |
|---|---|---|---|
| Paneles personalizables | Panel fijo bien diseñado | **Adaptar** — un panel que no hay que montar | MVP |
| Inicio de sesión único (SSO) | ALXOR ID | **Adaptar** — opcional, nunca requisito | F3 |
| Verificación en dos pasos | 2FA | **Copiar** | F2 |
| Registro de auditoría | Registro de auditoría | **Copiar** — ya resuelto en ALXOR | MVP |
| Permisos granulares por objeto y campo | Permisos por código (`contacto.gestionar`…) | **Copiar** — mismo patrón que ALXOR | MVP |
| Cumplimiento RGPD | Consentimiento con base legal y prueba | **Adaptar** — **mejor que HubSpot**: pensado para LOPDGDD y LSSI, no adaptado a posteriori | MVP |

---

## 10. Lo que HubSpot **no** tiene y nosotros sí

El mapeo anterior es defensivo. Estas cinco son la razón de existir del producto:

1. **La pantalla Hoy que decide el orden.** HubSpot te da una cola de tareas; nosotros decidimos qué
   hacer primero y explicamos por qué en una frase.
2. **El reparto de leads por afinidad real**, no por turno rotatorio.
3. **Cumplimiento español de serie**: consentimiento con base legal, prueba de origen y bloqueo de
   envío sin ella. En HubSpot eso se configura; aquí viene puesto.
4. **El precio no depende del número de contactos.** El castigo por crecer que todo el mundo odia.
5. **Continuidad hasta el cobro** con ALXOR Core: la atribución llega hasta el dinero cobrado, no
   hasta el dinero prometido.

---

## 11. Recuento

| Veredicto | Funcionalidades |
|---|---|
| **Copiar** | 33 |
| **Adaptar** | 48 |
| **Delegar** a ALXOR Core | 10 |
| **Descartar** | 41 |
| **Total inventariado** | **132** |

Es decir: de las 132 capacidades de HubSpot, **81 entran** (61 %) en alguna forma, 10 las cubre el
ERP y **41 se quedan fuera de forma deliberada**. De las 81 que entran, **43 son del MVP**; el resto
espera a tener clientes de pago.

Ese 31 % descartado es el producto. Sin él, match.keting sería HubSpot con menos presupuesto.

---

## 12. Hoja de ruta resultante

> **Corrección (módulo 13).** Esta tabla marcaba la automatización como MVP y **estaba mal**: tanto
> [`diseno-tecnico-funcional.md`](diseno-tecnico-funcional.md) —que es el documento que manda— como
> [`modulos/tareas-hoy.md`](../modulos/tareas-hoy.md) dicen que las automatizaciones configurables son
> F2, y que en el MVP hay **una sola, fija**: lead nuevo → asignar por match → tarea de primera llamada.
> Dos documentos contra uno, y el que manda entre ellos. Corregido arriba y en las listas de abajo.
>
> El orden no cambia: el propio documento de diseño pone las automatizaciones justo **después del correo
> conectado** en la cola de después del MVP, que es donde se han hecho.

**match.keting Start (MVP) — 43 funcionalidades**

Núcleo + Identidad · Contactos, Cuentas y cronología · Importación y duplicados · Embudo con motivo
de pérdida y estancamiento · Tareas y **Hoy** · **Match v1** y reparto de leads · Correo conectado,
plantillas y seguimiento · Registro de llamadas · Formulario y seguimiento web de conocidos ·
Cinco informes · Consentimiento y bajas · Auditoría y permisos · API y webhooks.

**match.keting Pro (F2)**

**Automatización «si pasa X, haz Y»** · Secuencias · Segmentos y campañas · WhatsApp Business ·
Enlace de reuniones · Extensión de navegador · Fragmentos y documentos · Guiones en Hoy · Objetivos ·
Prueba A/B de asunto · Atribución de origen · Integración con ALXOR Core · **Match v2** con pesos
aprendidos · Borradores y resúmenes asistidos · Campos extra · 2FA.

**F3 — solo bajo petición de clientes reales**

Tickets · Chat web · Encuestas · App móvil · Constructor de informes · Multidivisa · SSO · Asistente.

---

## 13. Lo que esto cuesta (aviso honesto)

El MVP de 44 funcionalidades es **más grande que el MVP entero de ALXOR Core**, que fueron 10
módulos. Con el método de un módulo a la vez, terminado del todo, es un trabajo de escala
comparable al del ERP — o mayor.

Si hubiera que recortar aún más para llegar antes al mercado, el corte defendible es este: **Contactos
+ Embudo + Tareas/Hoy + Match v1 + Formulario**. Cinco bloques. Es un producto vendible, y todo lo
demás se puede añadir después sin rehacer el núcleo. Los informes y las automatizaciones pasarían
a F2, y el correo conectado sería el primer añadido de pago.
