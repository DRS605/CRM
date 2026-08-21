# Módulo 7 — Informes

**Dos informes, no cincuenta.** Lo que mira el gerente el lunes: qué hay en el embudo y por qué se
pierde. Con periodo, con exportación a CSV, y sin un solo número inventado.

## Embudo

Por etapa: cuántas hay abiertas, cuánto importe, y **cuántas han llegado hasta ahí o más allá**. De
ese último dato sale la conversión de una etapa a la siguiente.

Arriba, cinco indicadores: abiertas, importe en juego, **previsión ponderada** (`Σ importe × prob.`),
tasa de cierre y ticket medio. Debajo, el balance del periodo: ganado, perdido y **días medios para
cerrar** las ganadas.

### La conversión sale del histórico real

`paso_etapa` anota cada vez que una oportunidad entra en una etapa —al crearse y en cada
movimiento—, y es **append-only**.

«Han llegado» cuenta las oportunidades cuyo **punto más lejano** alcanzado es esa etapa o una
posterior. No es lo mismo que «estuvieron ahí», y la diferencia se veía en la pantalla:

> **Esto ha estado mal dos veces.**
>
> **La primera**, no se guardaba el histórico y «han pasado» se calculaba suponiendo que toda
> oportunidad cerrada había recorrido todas las etapas. El informe enseñaba **«100 % pasa a
> propuesta»** sin que nada se hubiera movido.
>
> **La segunda**, con el histórico ya guardado, se contaba «cuántas estuvieron en esta etapa». Y el
> tablero deja arrastrar una oportunidad de «Nuevo» a «Propuesta» saltándose «Contactado» —lo cual es
> correcto: a veces una venta se salta un paso—. Con una oportunidad parada en «Contactado» y dos que
> saltaron directas a «Propuesta», los recuentos eran 1 y 2, y el informe enseñaba **«↓ 200 % pasa a
> propuesta»**.
>
> Un porcentaje inventado en un informe es peor que no tenerlo: el gerente lo lee como un embudo
> perfecto y decide con eso. Y una conversión por encima del 100 % no es un dato raro, es un dato
> falso; quien lo ve una vez deja de creerse el informe entero.
>
> Contando el punto más lejano alcanzado, la serie es **decreciente por construcción** —quien llegó a
> la etapa 3 dejó atrás la 1 y la 2, se las saltara o no— y el porcentaje no puede pasar del 100 %
> haga lo que haga el comercial con el ratón. Hay tres pruebas de integración que lo fijan: una con
> una oportunidad que se cae en «Nuevo» y no infla nada, otra con el recorrido completo, y una tercera
> que reproduce exactamente la forma del 200 % y comprueba además que la serie no crece en ningún
> punto.
>
> La migración rellena una anotación por oportunidad viva con su etapa actual. El histórico anterior
> no se puede reconstruir —no se guardaba— y eso es preferible a inventárselo.

Una etapa borrada del embudo deja anotaciones que ya no se pueden situar en ningún orden. **No
cuentan**: colocarlas «al principio» inventaría un recorrido que nadie hizo, y colocarlas al final
inflaría el final del embudo, que es justo el número que se mira.

## Motivos de pérdida

Por qué se pierde, en orden, con cuántas, cuánto importe y qué porcentaje del total. Existe **porque
el motivo es obligatorio al cerrar** (invariante O1): sin esa obligación no habría informe.

## Lo que no se calcula, no se enseña

Sin cierres no hay tasa de cierre, ni ticket medio, ni días para cerrar. La API devuelve `null` y la
interfaz enseña un **guion**. Poner `0` sería mentir: «todavía no se sabe» y «cero por ciento» no son
lo mismo, y confundirlos es cómo un panel pierde la confianza de quien lo mira.

## Periodo

Atajos —**último mes, trimestre, año, todo**— o `desde`/`hasta` explícitos. El informe siempre dice
**de qué fechas habla** («del 01/08/2026 al 31/08/2026»), porque un número sin periodo no significa
nada. `hasta` incluye el día entero: quien pide «hasta el 31» espera que entre lo del 31.

El filtro se aplica sobre la **fecha de cierre**: lo abierto es una foto de ahora, no del periodo.

## CSV para Excel en español

Separador **punto y coma** y decimales con **coma**. Con separador de comas, Excel en español mete la
fila entera en la primera celda y el cliente cree que el programa está roto. Y con **BOM**, porque
sin él Excel en Windows enseña «Hosteler¡a».

La descarga pasa por `fetch` y un *blob*, no por un `<a href>`: el endpoint pide el token y un enlace
plano no lo lleva. Exige el permiso `datos.exportar`, no `informe.leer`.

## API

| Método | Ruta | Permiso | Descripción |
|---|---|---|---|
| `GET` | `/informes/embudo?periodo=mes` | `informe.leer` | Etapas, conversión, previsión y ratios |
| `GET` | `/informes/motivos-perdida` | `informe.leer` | Por qué se pierde, en orden |
| `GET` | `/informes/embudo.csv` | `datos.exportar` | El mismo informe en CSV |
| `GET` | `/informes/motivos-perdida.csv` | `datos.exportar` | Los motivos en CSV |

## Nota de arquitectura

El informe de motivos **vivía en el módulo Embudo** desde el módulo 3, por proximidad. Al llegar
Informes se ha movido aquí, que es donde le corresponde por función, y el panel duplicado que había
en la vista del Embudo se ha quitado. `ConsultaInformes` vive en persistencia porque cruza el embudo
con sus etapas y su histórico.

**Los identificadores los genera el dominio, nunca la base**, y ahora está declarado en el modelo
(`ValueGenerated.Never` para toda clave `Guid`). Sin eso, EF usa la heurística «clave distinta de
vacío ⇒ la fila ya existe» y al descubrir una entidad hija nueva colgando de un padre ya rastreado
emite `UPDATE` en lugar de `INSERT`. Es lo que rompió el primer intento de guardar el histórico de
etapas, con un `expected to affect 1 row(s), but actually affected 0`.

## Tests

- **Unitarios (11)**: descripción del periodo en castellano, los últimos N días incluyendo hoy, y el
  CSV —punto y coma, decimales con coma, totales al pie, **un dato incalculable sale vacío y no como
  cero**, orden de los motivos y entrecomillado cuando el texto lleva el separador.
- **Integración (11)**: informe vacío que no inventa ratios, reparto por etapa y previsión ponderada,
  tasa de cierre y ticket medio, **la conversión desde el histórico real de movimientos**, **una
  oportunidad que no se mueve no infla nada**, orden y porcentajes de los motivos, recorte por
  periodo, el atajo «mes» diciendo sus fechas, **CSV con BOM y acentos intactos**, el CSV de motivos,
  y aislamiento entre empresas.
