# Módulo 15 — Campañas

La pieza por la que, hasta ahora, un cliente de match.keting tenía que contratar además una herramienta
de mailing. **No la copia.** Si lo que alguien quiere es mandar veinte mil correos con una plantilla
maquetada, Brevo o Mailchimp lo hacen mejor y hay que decírselo. Lo que hace este módulo es lo que una
plataforma de envío masivo no puede hacer sin romper su propio modelo de negocio.

## Las tres decisiones

### 1. Un segmento son condiciones, no una lista

`campania.segmento` no guarda ni un identificador de contacto. Guarda seis criterios, y los contactos
se buscan **cada vez que se usa**.

Una lista de 1.200 correos subida en marzo tiene en octubre gente que se fue de su empresa, gente que
ya compró y gente que se dio de baja. Nadie la limpia, porque limpiarla es trabajo. Un segmento no
puede quedarse desfasado porque no contiene nada que pueda desfasarse. Y cuando alguien ejerce el
artículo 17 y se le borran los datos, desaparece de todos los segmentos sin que nadie tenga que
acordarse de nada; con listas, un borrado obliga a recorrerlas todas.

Los seis criterios salen de datos que el CRM ya tiene por trabajar, no de etiquetas que haya que
mantener a mano:

| Criterio | De dónde sale | Nota |
|---|---|---|
| Estado | `contactos.contacto.estado` | Lead, cliente o perdido. **No existe «baja»** |
| Provincia | `contactos.cuenta.provincia` | Un particular sin cuenta no entra: no sabemos dónde está |
| Origen | `contactos.contacto.origen` | «formulario web», «alta manual», «importación»… |
| Match mínimo | `match.puntuacion.match` | Sin match calculado no entra: «no se sabe» no es «bajo» |
| Sin actividad desde | `contactos.actividad` | Sin *ninguna* actividad posterior. Quien no tiene ninguna, cuenta |
| Etapa | `embudo.oportunidad` | Con una oportunidad **abierta** en esa etapa |

Se combinan con **y**, nunca con «o». Un editor de condiciones anidadas es la forma más rápida de que
alguien construya sin darse cuenta un segmento que incluye a toda su base de datos; con «y», añadir un
criterio solo puede encoger el resultado, y eso se entiende sin explicación.

**Un segmento sin ningún criterio no se guarda.** Un segmento vacío significa «todos mis contactos», y
eso tiene que costar una decisión explícita —«clientes», «leads»— y no un despiste al rellenar un
formulario.

Y hay dos exclusiones que no se pueden pedir ni quitar, escritas en el punto de partida de la consulta
(`ConsultaSegmentos.Consulta`) y no en un `if` que se pueda olvidar al añadir un criterio nuevo:

- **Quien está de baja no entra.** Nunca, ni con el criterio de estado puesto a otra cosa. Es defensa en
  profundidad: si algún día alguien se salta la comprobación de permiso, la audiencia ya no lo contenía.
  Por el mismo motivo `EstadoBuscado` **no tiene** el valor «baja»: si estuviera, habría un desplegable
  donde se puede elegir.
- **Quien no tiene dirección de correo tampoco.** No es un destinatario, y meterlo solo serviría para
  inflar el número de excluidos con gente que nunca pudo estar dentro.

Lo que **no** se filtra aquí es el consentimiento, y es deliberado. Ver el punto siguiente.

### 2. El permiso se comprueba persona a persona, al encolar cada correo

Una campaña **no manda correos: encola correos de uno en uno por el mismo camino que un correo escrito
a mano**. El mismo `ServicioCorreo`, la misma comprobación de permiso, el mismo buzón de salida, la
misma anotación en la cronología del contacto. No hay un camino rápido para campañas, porque un camino
rápido para campañas es exactamente el agujero por el que se manda publicidad a quien no la ha pedido.

De ahí sale el flujo, que tiene dos momentos y no uno:

```
lanzar                                    el trabajo, cada minuto
──────                                    ──────────────────────
resuelve el segmento                      coge 50 pendientes
escribe una fila por persona   ──────▶    pregunta el permiso de cada uno
  (campania.envio, «pendiente»)           encola el correo, o excluye con el motivo
la campaña queda «enviando»               cuando no queda nadie, «enviada»
```

Separar «congelar» de «encolar» es lo que hace que esto se pueda contestar después. La fila existe desde
el primer momento, así que si mañana alguien pregunta por una persona concreta, la respuesta es «estaba
en la audiencia y se le excluyó por esto» o «estaba y se le mandó», nunca «no sé».

Y la comprobación que cuenta es la del **momento de encolar**, no la del lanzamiento: entre una cosa y
otra pasan minutos, y en esos minutos alguien puede retirar su consentimiento. Hay una prueba de
integración que hace exactamente eso — `Quien_retira_el_permiso_entre_lanzar_y_encolar_no_recibe_el_correo`.

Por eso el segmento no filtra por consentimiento: si lo hiciera, la gente sin permiso desaparecería del
informe y nadie sabría nunca cuánta base de datos tiene sin permiso, que es justamente el número que
hace falta para arreglarlo.

### 3. La ficha dice a cuántos NO llegó, y por qué

`campania.envio` guarda una fila por persona con su estado y, si se quedó fuera, **el motivo en una
frase**. La ficha los agrupa de más a menos:

```
POR QUÉ NO LES LLEGÓ
No hay base legal vigente para comunicaciones comerciales.  ████████████  94
Se dio de baja.                                             ██             7
```

Ese es el número que ninguna plataforma de envío pone junto al de entregas, porque su negocio es el
volumen. «94 sin consentimiento comercial» no es una queja: es la lista de deberes antes de la próxima
campaña.

La tabla **no guarda nombres ni correos**, solo el identificador del contacto, para que un borrado del
artículo 17 se lleve por delante estas filas con el contacto y no queden copias del dato personal
esparcidas por el historial de campañas.

## Reglas y límites, y por qué

| Regla | Motivo |
|---|---|
| La plantilla tiene que ser **comercial** | Mandar a quinientas personas un texto escrito para atender una solicitud es mentir sobre la base legal del envío, y además se nota al leerlo. Se comprueba al crear la campaña **y otra vez al lanzarla**: entre las dos cosas pueden pasar días |
| Máximo **2.000** destinatarios por campaña | A partir de ahí esto ya no es «escribirle a mis clientes» sino mailing masivo, y para eso hacen falta cosas que aquí no hay a propósito: reputación de IP, calentamiento de dominio, gestión de rebotes a escala. Mejor decir «hasta aquí llegamos» que quemar el dominio de correo del cliente |
| **50** correos encolados por pasada | No es por la base de datos: es por el SMTP del cliente, que tiene un límite por hora y lo aplica cortando la conexión. Cincuenta por minuto son tres mil por hora, más de lo que admite una campaña entera |
| Máximo **20** segmentos | El valor de un segmento es que alguien lo mire y lo entienda; una lista de ochenta filtros parecidos no la mira nadie. Y la lista cuenta cuántos contactos tiene cada uno, que son tantas consultas como segmentos |
| No se lanza a un segmento **vacío** | Una campaña lanzada a cero personas queda en la lista como «enviada» y nadie vuelve a mirarla. Mejor que falle en la cara de quien la lanza, mientras se acuerda de qué segmento eligió |
| Una campaña **lanzada** no se edita ni se borra | Es la prueba de a quién se le escribió. Borrar la fila no recoge los correos; lo único que consigue es que nadie pueda contestar quién los recibió |
| Un segmento **con campañas** detrás no se borra | Una ficha que dice «segmento: (borrado)» es la clase de agujero que hace inútil un historial |
| Los correos los **firma quien lanzó** la campaña | El hueco `{{comercial}}` sale con su nombre, y es a quien le van a contestar. Que una campaña la firme alguien y no «el sistema» es parte de que uno se lo piense antes de darle al botón |

## Detener una campaña

Detenerla **no recoge lo que ya salió**. Un correo en el buzón de salida está a un minuto de salir y
prometer que se puede recuperar sería mentir. Lo que hace es dejar de encolar: los que quedaban
pendientes pasan a excluidos con el motivo «la campaña se detuvo antes de llegarle el turno», para que
la suma siga cuadrando y en la ficha no quede nadie sin explicación.

## Permisos

Dos nuevos, y **`campania.gestionar` no cae dentro de `contacto.gestionar`**: una cosa es escribirle a
un cliente y otra es escribirle a cuatrocientos en nombre de la empresa. El día que se dio de alta a un
comercial nuevo, nadie pensó que le estaba dando eso.

| Rol | `campania.leer` | `campania.gestionar` |
|---|---|---|
| Propietario | sí | sí |
| Comercial | sí | **no** |
| Solo lectura | sí | no |

Un comercial ve las campañas porque le hace falta: si a su cliente le llegó un correo de campaña, tiene
que saberlo antes de llamarle, o llama a ciegas.

## Lo que este módulo tocó de otros

- **`ServicioCorreo.EnviarEnNombreDeAsync`** (nuevo). Una campaña la lanza una persona y los correos
  salen por lotes minutos u horas después, desde un trabajo de fondo donde no hay sesión de nadie. Quién
  firma es un **parámetro explícito** y no estado ambiente: es la diferencia entre «el sistema mandó
  esto» y «esto lo mandó Marta», y esa diferencia tiene que estar escrita en la llamada.
- **`ServicioCorreo.DireccionAsync`** ahora recibe el usuario en vez de leerlo del contexto. Leyéndolo
  del contexto, un envío desde un trabajo de fondo devolvía nulo y el correo se rechazaba con «ese
  contacto no tiene una dirección válida». El contacto sí la tenía; lo que faltaba era la sesión. Un
  mensaje de error que acusa al dato equivocado cuesta más de encontrar que uno que no diga nada.
- **`Permisos`**: dos códigos nuevos. Dos pruebas de integración comprobaban el número de permisos con
  una cifra escrita a mano; ahora se comparan contra `Permisos.Todos` y `PermisosDeRol`, que es la
  fuente. Un número escrito a mano habría dicho lo mismo hasta el día en que se añade un permiso.
- **`Plantilla`**: el comentario decía «no es una plantilla de campaña: aquí no hay listas ni
  segmentos». Sigue siendo cierto lo que importaba de esa frase —el permiso se comprueba por persona
  antes de cada envío— y por eso una campaña puede reusar las plantillas sin romper nada.

## Aislamiento

Las tres tablas (`campania.segmento`, `campania.campania`, `campania.envio`) llevan filtro global de EF
y RLS de PostgreSQL con `ENABLE` + `FORCE`, escrita a mano en la migración. `campania.envio` es la que
más importa: es una lista de identificadores de contacto, y aunque no guarda nombres ni correos, saber
que la empresa de al lado mandó una campaña a 1.800 personas y que la mitad no tenía permiso ya es
información de la empresa de al lado. Las tres están en `scripts/comprobar-aislamiento.sh`.

## Índices

- `ix_envio_campania_estado` — los pendientes de una campaña, que es lo que pide el trabajo cada minuto.
- `ix_envio_unico` (**único**, `campania_id` + `contacto_id`) — no es una optimización, es una regla: una
  persona no puede estar dos veces en la misma campaña. La guarda del dominio (`EnvioCampania.Encolar`
  devuelve falso si ya estaba resuelto) evita el doble encolado si dos pasadas se solapan; este índice
  evita el otro camino al mismo correo duplicado, que es escribir la fila repetida.
- `ix_campania_segmento` — para saber si un segmento está en uso sin recorrer la tabla.

## Lo que no hace, a propósito

- **No hay HTML ni plantillas maquetadas.** Es la misma decisión que en el módulo de correo: un correo
  de un comercial a un cliente es un correo de una persona a otra, y los que llegan con cabecera y
  botones se leen como publicidad porque lo son.
- **No hay pruebas A/B ni programación a futuro.** Con un techo de 2.000 destinatarios, una prueba A/B
  divide la muestra en dos grupos de mil y ningún resultado es significativo. Sería un gráfico bonito
  midiendo ruido.
- **No hay lienzo de secuencias.** Eso es el módulo de automatización, y ya existe.
- **No se puede recuperar un correo enviado.** Ni se ofrece el botón.
