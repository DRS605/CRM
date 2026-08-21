# Módulo 16 — Objetivos

El módulo más pequeño del sistema. Cabe en una frase —**cuánto se compromete cada uno a cerrar este
mes**— y cambia las tres pantallas que más se miran.

Informes y el resumen del repaso dicen *qué pasó*. Sin un objetivo al lado no hay forma de saber si eso
era suficiente, y un comercial sin referencia no sabe si el jueves va bien o va tarde. Un número
convierte un informe en una herramienta.

## Cuatro decisiones, y las cuatro son sobre lo que no hace

### 1. Un objetivo es de un mes concreto, no «el objetivo»

Guardar un solo número en la empresa habría sido menos código y habría reescrito la historia: el día
que se sube el objetivo, todos los meses anteriores pasarían a estar incumplidos de golpe. Un
compromiso tiene fecha.

De ahí sale la regla dura del módulo: **el objetivo de un mes que ya pasó no se puede tocar.** Poner en
marzo el objetivo de enero es escribir la historia después de conocerla, y un histórico que se puede
retocar no sirve ni para quien lo mira ni para quien lo cumplió. Cambiar el del mes en curso sí se
puede, y a propósito: los objetivos se revisan a mitad de mes en la vida real, y prohibirlo solo
conseguiría que se llevaran en una hoja aparte.

### 2. Solo dinero ganado

No hay objetivo de llamadas, ni de correos, ni de oportunidades creadas. Un objetivo sobre actividad se
cumple **haciendo actividad**, y así es exactamente como se enseña a un equipo a rellenar el CRM en vez
de vender.

Aquí la actividad la propone el sistema —eso son Hoy y el repaso— y lo que se le pide a la persona es
el resultado. Es la misma división de trabajo que sostiene todo el producto, escrita en un número.

Lo ganado se cuenta con tres reglas, y las tres cambian la cifra:

- **Solo ganadas.** `Motivo == null` en una oportunidad cerrada es lo que significa «ganada» aquí
  —perderla exige motivo, invariante O1—. Contar las cerradas a secas sumaría las perdidas.
- **Por fecha de cierre**, no de creación. Se cumple cuando se firma, no cuando se empieza a hablar.
- **Al propietario de la oportunidad**, que es quien la cerró, no a quien creó el contacto hace ocho
  meses. Una oportunidad sin propietario no cuenta para nadie: repartirla o dársela a alguien por
  defecto sería inventar un mérito.

### 3. No se inventa ninguno

Sin objetivo puesto, las pantallas **no enseñan la línea**. Ni siquiera lo ganado: «has ganado 12.400 €
este mes» sin objetivo es una curiosidad, y Hoy no es sitio para curiosidades. Un objetivo por defecto
sería un número sin motivo, que en esta interfaz es lo mismo que una mentira.

Por lo mismo, **quitar un objetivo no es ponerlo a cero**. Un cero dejaría un 0 % permanente en la
pantalla de esa persona; quitarlo hace desaparecer la línea, que es lo que significa «esta persona no
tiene objetivo este mes». En la tabla del equipo se quita vaciando la casilla.

### 4. No regaña

Un objetivo incumplido se enseña como número y como barra, sin adjetivos y sin rojo de alarma. Es la
misma regla que el resumen semanal del repaso: «una semana floja se cuenta sin regañar».

La barra usa la **escala de avance** del resto de la aplicación (`--av-1..5`, ver
[`docs/interfaz.md`](../interfaz.md)): es un porcentaje, igual que la probabilidad de una etapa, así
que un mes al 84 % y una etapa al 84 % son del mismo color a propósito. Y a 0 % **no se pinta nada**,
por lo mismo que una etapa vacía del embudo: un tope de color donde no hay dato es un color sin motivo.

## El número que no tenía nadie

```
ESTE MES  11.600 €  de 30.000 €   ▓▓▓▓▓░░░░░░░░   Faltan 18.400 € · quedan 10 días · 1.840 € al día
```

`PorDiaQueQueda` es lo único de este módulo que no se puede sacar de ninguna otra pantalla, y es lo que
cambia lo que alguien hace esta tarde. Un 39 % no le dice a nadie si tiene que darse prisa; «1.840 € al
día» sí.

Se reparte entre los **días laborables que quedan del mes, hoy incluido**, de lunes a viernes. Sin
festivos, por lo mismo que `HorasLaborables`: cada comunidad y cada municipio tienen los suyos, y
equivocarse un día cuesta mucho menos que mantener catorce calendarios.

Dos casos en los que **no** se da la cifra, porque sería falsa:

- **Cumplido**: no hay nada que repartir.
- **Mes acabado**: «te faltan 18.400 € al día» al lado de un mes que ya no se puede cambiar es una
  cifra sin sentido. Lo que faltó se sigue enseñando —es un dato del mes—, el reparto diario no.

Y mirar un mes que no es el actual da **0 días**, no el mes entero: mirar el objetivo de noviembre en
agosto tiene que decir «no quedan días de ese mes por trabajar todavía», no repartir el importe entre
veintiún días que aún no han empezado.

## El objetivo de la empresa es la suma de los de su gente

No hay una cifra de empresa guardada aparte. Un objetivo de empresa que no cuadre con la suma de los de
su gente son dos verdades, y la que se mira siempre es la equivocada.

Con un detalle que importa: **lo ganado por quien no tiene objetivo no entra en el total**. Sumar lo de
todos y compararlo con la suma de unos pocos objetivos daría más del cien por cien sin que nadie hubiera
vendido más de lo previsto, que es la clase de número que hace inútil un panel. Su cifra sigue estando
en su fila —no se esconde— solo no se suma a un total que no le corresponde.

## Quién ve qué

| | Su objetivo | El del equipo | Ponerlos |
|---|---|---|---|
| Propietario | sí | sí | sí |
| Comercial | **sí** | no | no |
| Solo lectura | (no tiene) | no | no |

No hay permiso nuevo: poner objetivos es `usuario.gestionar`, porque es una decisión sobre personas.
Ver **el propio** no pide ningún permiso, y tiene que ser así o el objetivo no serviría de nada: la
ruta `/objetivos/mio` solo mira el usuario de la sesión, así que no hay forma de pedir el de otro por
ahí.

Quien no puede gestionar oportunidades **no sale en la tabla**: un objetivo de venta para quien no puede
tocar una oportunidad es un objetivo que no puede cumplir. Se decide por el permiso y no por el rol, así
que un rol nuevo que venda lo recoge sin tocar nada.

## Detalles de la implementación que costaron algo

- **`204` cuando no hay objetivo**, no `404` y no `200` con el cuerpo vacío. Son tres cosas distintas:
  `404` diría «esa ruta no existe» y obligaría a la pantalla a distinguir «no hay objetivo» de «se ha
  roto algo»; un `200` con el cuerpo vacío —lo que devuelve `Results.Ok(null)`— promete una
  representación y no la manda. `204` dice exactamente lo que pasa.
- **El mes se normaliza al día 1** en el dominio. Sin normalizar, «agosto» puesto el día 18 y «agosto»
  puesto el día 3 serían dos filas distintas y la persona tendría dos objetivos del mismo mes. El índice
  único `(empresa, usuario, mes)` solo sirve de algo con el mes normalizado.
- **`PUT` y no `POST`.** Fijar el objetivo de alguien para un mes es idempotente, y quien rellena la
  tabla del equipo no sabe ni le importa cuáles existían ya. Dos operaciones distintas —crear y
  editar— le habrían obligado a saberlo.
- **La casilla se guarda al salir de ella**, sin botón. Quien pone objetivos rellena cinco casillas
  seguidas; un «guardar» al final significa perderlas todas si se cierra la pestaña.
- **El mes en instantes va abierto por arriba**: `>= día 1` y `< día 1 del siguiente`. Con `<=` al
  último día se perdería todo lo cerrado ese día después de medianoche, que es justamente cuando se
  firma lo que se firma a final de mes.
- **`identidad.membresia` no lleva filtro global por empresa** —es la tabla que decide a qué empresas
  puede entrar alguien—, así que `ConsultaEquipoObjetivos` filtra a mano. Olvidarlo habría devuelto el
  equipo de todas las empresas.
- **Un techo de un millón por persona y mes.** No es una opinión sobre nadie: a partir de ahí lo que hay
  es un cero de más al teclear, y un objetivo mal escrito hace que la barra de todo el equipo no
  signifique nada durante un mes.

## Aislamiento

`objetivos.objetivo` lleva filtro global de EF y RLS de PostgreSQL con `ENABLE` + `FORCE`, escrita a
mano en la migración, y está en `scripts/comprobar-aislamiento.sh`. No hay datos personales en la tabla
—un identificador y un importe— pero sí algo que no puede salir de la empresa: cuánto se le pide a cada
comercial y cuánto lleva. Es información que no se comparte ni dentro de la propia empresa sin permiso.

## Lo que no hace, a propósito

- **No hay objetivos de equipo ni de zona.** La suma ya está; una jerarquía de objetivos es un módulo de
  gestión, no una herramienta de venta.
- **No hay comisiones.** Calcular lo que cobra alguien a partir de esto pide reglas, tramos, retenciones
  y un cierre mensual auditable. Eso es nómina, y la nómina no vive en un CRM.
- **No hay previsión ni proyección.** Informes ya da la previsión ponderada del embudo. Poner aquí «vas
  camino de 24.000 €» sería el mismo número dicho otra vez, y con menos contexto.
- **No hay avisos de «vas tarde».** El repaso ya avisa de lo que hay que hacer. Un aviso que solo dice
  que vas mal no propone nada, y a quien avisa sin proponer se le deja de escuchar.
