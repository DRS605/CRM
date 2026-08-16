# Módulo 1 — Núcleo, Identidad y Organización

Primer módulo de match.keting. Deja montado lo que todos los demás dan por hecho: tipos comunes,
acceso, multiempresa y permisos.

## Núcleo

`Resultado` / `Resultado<T>` y `Error` (con `TipoError`: validación, no encontrado, conflicto, no
autorizado, prohibido) para los fallos esperados; **las excepciones se reservan para errores de
programación**. Leer `.Valor` de un resultado fallido lanza `InvalidOperationException` a propósito.

`EntidadBase`, `RaizAgregado` (con eventos de dominio) y `RaizAgregadoEmpresa` (que obliga a llevar
`EmpresaId`). `IReloj` se inyecta siempre: el decaimiento del Momento del módulo 5 se probará
moviendo el reloj, nunca esperando.

`Email` es un *value object* que **normaliza a minúsculas y recorta**. No es cosmético: sin esa
normalización, la detección de duplicados de contactos del módulo 2 no funciona.

## Identidad

`Usuario` { email único, hash de contraseña, nombre, email verificado, activo, último acceso } es
**global**, no pertenece a ninguna empresa. `Membresia` { usuario, empresa, rol, activa } es la que
expresa la pertenencia, para que una misma persona trabaje en varias empresas sin duplicar cuenta.

Roles y sus permisos:

| Rol | Permisos |
|---|---|
| **Propietario** | Los 11 |
| **Comercial** | Contactos, oportunidades y tareas (leer y gestionar) + `informe.leer` |
| **Solo lectura** | Todos los `*.leer` + `datos.exportar` |

Una membresía desactivada se queda **sin ningún permiso**, y no se le puede cambiar el rol.

**Contraseñas**: mínimo 8 caracteres combinando letras y números. Deliberadamente modesto: exigir
símbolos raros empuja a la gente a apuntar la contraseña en un papel. Hash con **PBKDF2-SHA256**,
210.000 iteraciones, sal aleatoria por contraseña y comparación en tiempo constante.

**Inicio de sesión**: el error de contraseña incorrecta y el de correo inexistente son **byte a
byte el mismo**. Distinguirlos regala información a quien prueba correos; hay un test de
integración que lo comprueba.

## Organización

`Empresa` es el inquilino (*tenant*): { nombre, NIF, provincia, **peso del Encaje**, **horas de
rebote**, activa }. Nace con el Match a mitad y mitad (0,5) y el rebote de leads a 4 horas.

Quien crea una empresa se convierte en su **propietario**, y empresa y membresía se guardan en la
**misma transacción**: una empresa sin propietario sería una empresa a la que nadie puede entrar.

## Multiempresa

La empresa activa viaja **dentro del JWT** (claim `eid`) y se resuelve con `ContextoEmpresaHttp`.
Ningún endpoint acepta un `empresa_id` por parámetro: el token es la única fuente de verdad
(invariante T2).

> **Decisión explícita**: la tabla `identidad.membresia` **no** lleva filtro global por empresa. Es
> la tabla que decide a qué empresas puede entrar un usuario, así que filtrarla por la empresa
> activa impediría listar las empresas entre las que elegir. El aislamiento de los datos de negocio
> (contactos, oportunidades…) sí irá por filtro global + RLS, módulo a módulo.

## Persistencia

Un solo `ContextoMatchketing`; cada módulo aporta sus configuraciones y **su propio esquema de
PostgreSQL**, de forma que las fronteras entre módulos también se ven en la base de datos.

| Esquema | Tabla |
|---|---|
| `identidad` | `usuario` (índice único por email), `membresia` (único por usuario+empresa) |
| `organizacion` | `empresa` |

Nombres en español y `snake_case`. Claves `uuid`, marcas de tiempo `timestamptz`.

## Interfaz

Acceso (entrar / crear cuenta), elección o creación de empresa, y la aplicación con su menú de
cinco opciones. **Ajustes es funcional**: muestra los datos de la empresa y permite mover la
balanza Encaje/Momento y las horas de rebote. Hoy, Contactos, Embudo e Informes muestran su estado
real —vacío o pendiente— con el número de módulo en el que llegan. Paleta magenta e identidad según
`matchketing.md`.

## Tests

- **Unitarios (33)**: normalización de correo, validación de contraseña y nombre, eventos de alta,
  permisos por rol, membresía desactivada, hasher (sal distinta, formato inválido no revienta),
  validación de los ajustes del Match.
- **Integración (11)**, contra PostgreSQL real: registro y sesión, correo repetido, entrar con el
  correo en mayúsculas, **respuesta idéntica ante contraseña mala y correo inexistente**, creación
  de empresa con los 11 permisos, **un usuario no puede entrar en la empresa de otro**, sin token no
  se ve nada, y persistencia de los ajustes del Match.
