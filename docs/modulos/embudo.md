# Módulo 3 — Embudo

Oportunidades sobre un tablero de etapas, con las dos cosas que hacen que un embudo sirva de algo:
**el motivo de pérdida obligatorio** y **el aviso de estancamiento**.

## Modelo

`Embudo` { nombre, porDefecto, etapas } · `Etapa` { nombre, orden, **probabilidad**, **díasAviso** } ·
`Oportunidad` { contacto, cuenta?, título, importe, embudo, etapa, entróEnEtapaEn, previstaCierre,
propietario, motivo?, detalleMotivo?, cerradaEn? }.

Cada empresa **nace con su embudo** de cinco etapas: Nuevo (10 %) · Contactado (25 %) · Propuesta
(50 %) · Negociación (75 %) · Cierre (90 %). Se crea en la misma transacción que la empresa: nadie
debería tener que montar un embudo antes de poder apuntar su primera venta.

## Las invariantes

- **O1 — Perder exige motivo.** Sin él la transición falla. No es rigidez: sin motivo no hay informe
  de pérdidas, que es lo único que enseña a vender mejor. Lista corta y cerrada —precio, plazo,
  competencia, no era el momento, no contesta, otro— porque con texto libre no habría informe.
- **O2 — El estado no se guarda, se deduce.** `Abierta` si no hay `cerradaEn`; `Ganada` si está
  cerrada sin motivo; `Perdida` si está cerrada con él. No hay columna de estado que pueda
  descuadrarse: en la base ni siquiera existe.
- **O3 — Una oportunidad cerrada no se reabre.** Ni se gana dos veces, ni se mueve, ni se edita. Si
  el cliente vuelve, se crea otra: de eso depende que las tasas de cierre signifiquen algo.
- **O4 — La probabilidad la pone la etapa.** Vive en `Etapa`, no en `Oportunidad`, así que no se
  puede tocar a mano caso por caso y la previsión sigue queriendo decir algo.
- **O5 — El importe no puede ser negativo**, y se redondea a dos decimales al guardar.

## Estancamiento

Cada etapa dice cuántos días tolera. `entróEnEtapaEn` se reinicia **solo cuando la oportunidad
cambia de etapa de verdad** —moverla a la etapa en la que ya está no reinicia nada— y con eso el
tablero marca en ámbar lo que lleva parado y lo cuenta en la cabecera. Una oportunidad cerrada nunca
está estancada.

## Previsión ponderada

`Σ (importe de la columna × probabilidad de la etapa)`. Sin IA y sin ceremonia: la previsión de una
pyme de tres comerciales no necesita un modelo, necesita ser correcta y explicable.

## Lo que pasa al cerrar

Ganar y perder no se quedan en el embudo: se escriben en la **cronología del contacto**, y ganar
además lo marca como **cliente** —quien compra deja de ser un lead—. Lo orquesta el endpoint, que es
el punto donde los dos módulos se encuentran sin conocerse. Con ALXOR Core conectado, aquí es donde
nacería el presupuesto.

## API

| Método | Ruta | Permiso | Descripción |
|---|---|---|---|
| `GET` | `/embudo/tablero` | `oportunidad.leer` | Columnas, sumas, previsión ponderada y estancadas |
| `POST` | `/oportunidades` | `oportunidad.gestionar` | Crea en la primera etapa. **201** |
| `PUT` | `/oportunidades/{id}` | `oportunidad.gestionar` | Actualiza (solo si está abierta) |
| `POST` | `/oportunidades/{id}/mover` | `oportunidad.gestionar` | Cambia de etapa |
| `POST` | `/oportunidades/{id}/ganar` | `oportunidad.gestionar` | Gana y marca cliente al contacto |
| `POST` | `/oportunidades/{id}/perder` | `oportunidad.gestionar` | **Motivo obligatorio** |
| `GET` | `/informes/motivos-perdida` | `informe.leer` | Por qué se pierde, en orden |

## Persistencia

Esquema **`embudo`**: `embudo`, `etapa`, `oportunidad`. Filtro global de EF Core y política de RLS en
`embudo` y `oportunidad`; **`etapa` no lleva `empresa_id`** porque cuelga de `embudo` y su
aislamiento viene por la clave ajena. Las etapas son parte del agregado `Embudo` y se cargan y
guardan con él.

`ConsultaEmbudo` vive en persistencia y no en el módulo: cruza el embudo con los contactos y las
cuentas, y ninguno de los tres debe conocer a los otros.

## Interfaz

Tablero kanban con **arrastrar y soltar** entre columnas, cabecera con el número y la suma de cada
etapa más su probabilidad, tarjetas en ámbar cuando están paradas, y botones de *Ganada* y *Perdida*
en cada una. El diálogo de pérdida no deja cerrar sin elegir motivo. Debajo, el informe de motivos
con su barra por frecuencia.

Nota de alcance: el informe de motivos vive aquí porque es del embudo. La pantalla **Informes**
completa sigue siendo el módulo 7.

## Tests

- **Unitarios (24)**: el embudo por defecto y sus probabilidades ascendentes, validación de etapas,
  oportunidad en la primera etapa, importe negativo y redondeo, etapa de otro embudo, **perder sin
  motivo**, ganar y perder dos veces, cerrada que no se mueve ni se edita, **mover reinicia el
  contador y mover a la misma etapa no**, estancada al pasar los días, y cerrada que nunca lo está.
- **Integración (11)**: la empresa nueva ya trae sus cinco etapas, la oportunidad cae en la primera
  columna y suma, **la previsión pondera** (1.000 × 10 % + 2.000 × 50 % = 1.100), mover cambia de
  columna y pone los días a cero, perder sin motivo da 400, ganar marca cliente y deja rastro en la
  cronología, la ganada no se puede perder, el informe ordena por el motivo que más duele, **una
  empresa no ve el embudo de otra**, no se puede mover a una etapa de otra empresa, y las políticas
  de RLS están puestas.
