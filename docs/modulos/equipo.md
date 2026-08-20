# Módulo 14 — Equipo

**Estado**: terminado · **Proyecto**: `Matchketing.Identidad` · **Esquema**: `identidad`

Una empresa con dos personas. Hasta este módulo no podía tenerlas, y eso hacía falso un renglón del
alcance del MVP: «Registro, inicio de sesión, **usuarios y equipos**».

## El agujero que cierra

No faltaba el modelo. Faltaba la puerta.

Desde el módulo 1 existían tres roles (`Propietario`, `Comercial`, `SoloLectura`), once permisos
repartidos entre ellos, y una `Membresia` con `Rol`, `Zonas`, `CambiarRol`, `FijarZonas` y
`Desactivar`. **Nada de eso tenía un solo llamante.** La única membresía que se creaba en toda la
aplicación era la del propietario al crear la empresa:

```csharp
identidad.AnadirMembresia(Membresia.Crear(usuarioId, creada.Valor.Id, Rol.Propietario, reloj));
```

Consecuencias, todas silenciosas:

* Ninguna empresa podía tener dos personas.
* `Rol.Comercial` y `Rol.SoloLectura` eran **inalcanzables**: el reparto de permisos de
  `PermisosDeRol` se probaba en tests unitarios y no se usaba en producción jamás.
* El permiso `usuario.gestionar` no se comprobaba en **ningún** endpoint: solo decidía una etiqueta
  en la pantalla de Ajustes.
* `Membresia.Zonas` estaba siempre vacío. Es el **primer factor** del reparto de leads del Match
  (`Repartidor`: quien lleva la zona gana a quien no), así que el reparto por zona repartía sin que
  nadie tuviera zona.
* `/match/comerciales`, `contactos/{id}/asignar`, el rebote de leads «al siguiente comercial» y la
  acción «asignárselo a un comercial» de las reglas operaban sobre un conjunto de un solo elemento.

Un método público sin llamantes no da ningún error: compila, pasa los analizadores y aparenta que la
funcionalidad existe. Es la misma clase de hallazgo que
[`Empresa.Actualizar`](identidad-organizacion.md) y que el interruptor de aperturas de
[`correo.md`](correo.md), y por eso está en las trampas conocidas de `CLAUDE.md`.

## La invitación es un enlace

Se invita por correo electrónico y con un papel, y sale **un enlace** que se le pasa a la persona por
donde se hable con ella. No se manda desde aquí: mandar correos a direcciones que no son de un
contacto —y sin consentimiento de nada— sería otro sistema de correo distinto del del módulo 12, con
sus propias reglas. El enlace se enseña una vez y quien invita lo reparte.

**Por qué un enlace y no una contraseña provisional.** La alternativa habitual es que el propietario
cree la cuenta y le diga la contraseña a su compañero. Eso significa que el propietario conoce la
contraseña de otra persona, y desde ese momento **el registro de auditoría ya no puede afirmar quién
hizo qué**: se lleva por delante la mitad de lo que sostiene este producto. Con el enlace, la
contraseña la elige quien la va a usar y nadie más la ve nunca.

### Qué se guarda, y qué no

```
identidad.invitacion
  id, empresa_id
  email               -- vale para ese correo y para ningún otro
  rol
  invitado_por
  huella_token unique -- SHA-256 del token, en hexadecimal
  creada_en, caduca_en
  aceptada_en, retirada_en
```

**Del token solo se guarda su huella.** En la tabla va un SHA-256, no el token. Quien lea la base de
datos —una copia de seguridad vieja, un volcado en el portátil de alguien— no se lleva llaves de
nadie. Se busca por la huella, que es determinista, así que el índice sigue sirviendo.

**El token lleva la empresa dentro** (16 bytes en Base64Url) más 32 bytes al azar. Lo primero es lo
que permite que el endpoint público fije el inquilino **antes** de tocar la base: sin
`app.empresa_actual`, la RLS de PostgreSQL no devuelve ninguna fila y la invitación no se encontraría
nunca. Es el mismo truco que el enlace de baja y el píxel de apertura. Lo segundo es lo que hace que
no se pueda adivinar.

### Por qué esta se guarda y el enlace de baja no

`EnlaceBaja` es un token **firmado y sin tabla**, y a propósito no caduca: una baja tiene que
funcionar dentro de tres años, cuando alguien encuentre un correo viejo en el buzón. Una invitación es
lo contrario: es **una llave de la empresa**, y necesita las tres cosas que un token firmado no puede
dar.

| | Enlace de baja | Invitación |
| --- | --- | --- |
| Se guarda | No | Sí (solo la huella) |
| Caduca | **Nunca**, a propósito | **7 días** |
| Veces que vale | Todas | **Una** |
| Se puede retirar | No (solo rotando el secreto de todos) | Sí, una a una |

Siete días: bastante para quien está de vacaciones, poco para que una invitación olvidada en un chat
de hace dos años siga abriendo la puerta.

Invitar dos veces al mismo correo **no acumula invitaciones**: la anterior se retira y su enlace deja
de valer. Dos llaves vivas de la misma puerta son una llave que nadie sabe que existe.

## Aceptar: el enlace no es una identidad

`POST /invitaciones/{token}` no pide autenticación —quien lo abre puede no tener cuenta— pero **no se
fía del enlace para decir quién es nadie**:

* **Si no hay cuenta con ese correo**, se crea ahí mismo con el nombre y la contraseña que teclee la
  persona.
* **Si ya hay cuenta**, hace falta **su contraseña**. El enlace prueba que alguien tiene el enlace, no
  que sea esa persona: sin esta comprobación, reenviar el mensaje a un tercero le daría acceso a la
  empresa **con la cuenta de otro**.

Porque comprueba una contraseña, el endpoint lleva techo de intentos. Y **el cubo es la invitación, no
la IP**, que es lo que hace este caso distinto del de entrar:

* Lo único que se puede adivinar aquí es la contraseña de **una** cuenta, la del correo que lleva esa
  invitación dentro. El cubo tiene que ser esa invitación, no el edificio desde el que se teclea.
* Con un cubo por IP, una tarde de altas —cinco personas de la misma oficina entrando en la empresa—
  se habría comido los intentos de las demás. Y compartirlo con el de entrar habría dejado sin acceso
  a todo el mundo durante cinco minutos: peor que el ataque que evita.

Cinco intentos cada cinco minutos por invitación, mucho más estrecho que los veinte del acceso, y aun
así de sobra para quien sabe su contraseña. Lo prueba
`Adivinar_la_contrasena_de_una_invitacion_se_corta_sin_estorbar_a_las_demas`, que además comprueba que
la invitación de al lado sigue funcionando.

Se marca como usada **al final**, cuando ya no puede fallar nada. Marcarla antes la gastaría en cuanto
la contraseña no valiera —equivocarse tecleando dejaría a la persona fuera y sin enlace— y dejaría la
corrección en manos de que quien llama no guarde: eso funciona hoy y se rompe el día que alguien añada
un `GuardarCambiosAsync` de más.

Al aceptar se devuelve la sesión **con la empresa ya activa**: aceptar una invitación y tener que
buscar después la pantalla de acceso sería raro dos veces.

Un token inventado, uno caducado y uno retirado contestan **lo mismo**: `invitacion.no_vale`.
Distinguirlos diría a quien prueba tokens cuáles existieron.

## La empresa fijada gana al token de sesión

Esto cambió con este módulo y es la parte que más se acerca a un fallo de aislamiento.

`IContextoEmpresaPublico.FijarEmpresa` existía para la entrada pública de leads, y el contexto
resolvía la empresa así:

```csharp
public Guid? EmpresaId => Leer(Claims.EmpresaId) ?? empresaPublica;   // el token ganaba
```

Con esa precedencia, una petición a `/f/{clave}` —el formulario en la web de un cliente— que llegara
con la sesión de **otra** empresa abierta habría guardado el lead en la empresa de la sesión, no en la
del formulario. Hoy no pasaba porque el navegador no adjunta el token a esas rutas, pero eso es una
casualidad del transporte, no una garantía. Y la invitación sí se abre desde la propia aplicación, con
una sesión de otra empresa posiblemente activa.

Ahora manda la empresa fijada. Fijarla es un acto deliberado de cuatro endpoints contados —formulario
público, enlace de baja, píxel de apertura, invitación— y el valor siempre viene firmado o de una fila,
nunca de un parámetro que el cliente pueda inventar. El invariante **T2** queda con una frase más: la
empresa sale del JWT, salvo en los endpoints públicos que la derivan de un token propio, donde manda
la derivada.

Lo prueba `PruebasFlujoEquipo.Un_enlace_de_otra_empresa_no_sirve_para_entrar_en_la_tuya`.

## Lo que no se puede hacer

Dos reglas, y las dos existen para que no se llegue a un estado del que no se pueda salir sin entrar
en la base de datos a mano:

* **Nadie se cambia su propio papel ni se quita a sí mismo el acceso.** Que lo haga otra persona con
  permiso.
* **La empresa no se queda sin propietario activo.** Al último propietario no se le baja el papel ni
  se le retira el acceso: una empresa sin propietario no se puede administrar.

Y una tercera que no es una prohibición sino una decisión: **quitar el acceso no borra a nadie**. La
cuenta sigue existiendo —esa persona puede estar en otras empresas—, sus contactos siguen asignados a
su nombre, y su rastro en la auditoría y en las cronologías no se toca, porque son hechos. Quien ya no
entra **sigue saliendo en la lista**, marcado como «sin acceso»: desaparecer dejaría oportunidades con
un dueño que la pantalla no sabe nombrar. Volver a invitarle **reactiva la membresía que ya había**, en
vez de crear otra: el índice único de usuario+empresa no deja dos, y conservar la fecha de alta
original no cuesta nada.

## Zonas

Las provincias que cubre cada persona, separadas por comas, en la misma pantalla. Es el primer factor
del reparto de leads: quien lleva la zona gana a quien no
(`PruebasRepartidor.Quien_lleva_la_zona_gana_a_quien_no`). Lo que faltaba no era la puntuación, era
poder rellenar el campo.

## API

| Verbo | Ruta | Permiso | Qué hace |
| --- | --- | --- | --- |
| GET | `/equipo` | (cualquier miembro) | El equipo con sus papeles y zonas. Las invitaciones pendientes, solo si `usuario.gestionar`. |
| POST | `/equipo/invitaciones` | `usuario.gestionar` | Invita y devuelve el enlace. **Una sola vez.** |
| DELETE | `/equipo/invitaciones/{id}` | `usuario.gestionar` | Retira una invitación sin usar. |
| PUT | `/equipo/{id}/rol` | `usuario.gestionar` | Cambia el papel. No el propio. |
| PUT | `/equipo/{id}/zonas` | `usuario.gestionar` | Las provincias que cubre. |
| DELETE | `/equipo/{id}` | `usuario.gestionar` | Le quita el acceso. No borra nada. |
| GET | `/invitaciones/{token}` | — (público) | Qué empresa, qué papel, y si ya hay cuenta. |
| POST | `/invitaciones/{token}` | — (público, con límite de intentos) | Acepta y devuelve la sesión. |

**Ver** el equipo no pide `usuario.gestionar`: un comercial necesita saber a quién puede asignarle un
lead y quién lleva su zona. Lo que pide permiso es **cambiarlo**. Las invitaciones pendientes sí, que
son direcciones de correo de gente que todavía no ha entrado.

## Auditoría

Se auditan las tres operaciones: `equipo.invitado`, `equipo.rol_cambiado` y
`equipo.acceso_retirado`. Dar y quitar acceso a los datos de los clientes es la operación más delicada
que hay aquí.

**Sin el correo de nadie.** Se apunta el papel y el identificador; el correo de la persona invitada es
un dato personal, y en `auditoria.registro` no entran datos personales (ver
[`auditoria.md`](auditoria.md)). Lo prueba
`PruebasFlujoEquipo.Invitar_y_quitar_el_acceso_queda_en_la_auditoria_sin_el_correo_de_nadie`.

## Interfaz

**Ajustes › Equipo**: la lista con «Tú» marcado, el papel en un desplegable, las zonas en un campo, y
«Quitar acceso». Debajo, las invitaciones pendientes con su fecha de caducidad y «Retirar». Y el
formulario de invitar, que al enviarlo enseña el enlace en la misma caja ámbar que el secreto de un
webhook: **pásaselo ahora**, porque no se puede volver a ver.

Quien abre el enlace ve una pantalla propia con **la empresa y el papel antes de que se le pida nada**,
y la pista de que la contraseña la elige él.

### La interfaz según el papel

Toda la interfaz se escribió cuando la única persona posible en una empresa era su propietaria, con los
once permisos. Con tres papeles de verdad salieron los defectos que hasta entonces **no podían existir**:

* Abrir Ajustes como comercial lanzaba **cinco peticiones que el servidor contestaba con 403**, y una
  de ellas ni se recogía: la pantalla se quedaba a medias y en la consola aparecía «No se ha podido
  completar la operación».
* La ficha de un contacto le ofrecía a alguien de solo lectura registrar una llamada, apuntar una nota,
  apuntar un permiso, retirarlo y borrar todos sus datos. Cinco botones, cinco 403 al pulsar.
* A un comercial le ofrecía «Descargar sus datos» y los dos «Descargar CSV» de Informes, y `datos.exportar`
  **no está en su papel** a propósito: quien se va de la empresa no se lleva la base de clientes.

Esconder no es la seguridad. La seguridad la hace el servidor, permiso a permiso, en cada endpoint, y
eso lo prueban `Solo_lectura_no_escribe_nada` y `Un_comercial_vende_pero_no_se_lleva_los_datos`. Lo que
se arregla en la pantalla es otra cosa: **un botón que contesta 403 al pulsarlo promete algo que no va a
pasar**.

El mecanismo es un atributo y **un solo sitio** que lo aplica:

```html
<button id="pv-exportar" data-permiso="datos.exportar">Descargar sus datos</button>
```

```javascript
function aplicarPermisos(raiz) {
  var nodos = (raiz || document).querySelectorAll('[data-permiso]');
  Array.prototype.forEach.call(nodos, function (n) {
    if (!puede(n.dataset.permiso)) { n.classList.add('sin-permiso'); }
  });
}
```

Un `if` por botón se olvida en el siguiente botón; un atributo se ve al leer el HTML. Y como casi todas
las listas se pintan con `createElement` cuando llegan los datos —los botones de una tarjeta del embudo
nacen media hora después de entrar—, un `MutationObserver` aplica lo mismo a lo que aparezca luego.
Marcar un elemento basta y sobra, esté donde esté y cuando sea.

**Con una clase, no con `hidden`.** El primer intento ponía `hidden = true` y duró hasta la primera
ficha de contacto: `pintarPrivacidad` hace `$('pv-alta').hidden = deBaja`, o sea **false** para un
contacto normal, y volvía a enseñar el formulario que se acababa de esconder. Dos mecanismos para lo
mismo se pisan; con una clase propia y un `!important` conviven y este gana siempre.

Y solo esconde, nunca enseña: los permisos van firmados en el token y no cambian mientras dure la sesión.

Casos particulares que valen la pena:

* **El repaso no sale en el menú** sin `tarea.gestionar`. Es una cola de decisiones: quien no puede
  contestarlas no tiene nada que hacer ahí.
* **Hoy sí sale**, sin los botones. Saber qué hay pendiente sirve aunque no se pueda tocar.
* **Ver el equipo** no pide permiso; cambiarlo sí.

## Tests

* **Unitarios (32)**: el token existe una vez y de él se guarda la huella; dos invitaciones no
  comparten token; la empresa se lee del token sin tocar la base; un token con mala forma no dice
  ninguna empresa; nace viva y caduca en una semana; se usa una sola vez; retirada ya no vale; una
  aceptada no se retira; el correo se normaliza; un papel que no existe no se invita. Y las reglas:
  invitar y aceptar, la contraseña de una cuenta existente, invitar a quien ya está, reinvitar retira
  la anterior, reactivar a quien se le quitó el acceso, el último propietario, nada sobre uno mismo,
  nada de otra empresa, las zonas, quien ya no entra sigue en la lista, y **una contraseña floja no
  gasta la invitación**.
* **Integración (18)**, contra PostgreSQL real: una empresa con dos personas y los siete permisos de
  un comercial; el enlace se abre sin sesión gracias a la empresa que lleva dentro; **un enlace de
  otra empresa no sirve para entrar en la tuya**; cada empresa ve solo su equipo y sus invitaciones;
  un comercial ve el equipo y no lo cambia; ascender cambia los permisos del siguiente token;
  quitar el acceso deja a la persona fuera pero con su cuenta; las zonas llegan al reparto; retirar
  una invitación mata el enlace; una contraseña floja no gasta la invitación; un token inventado
  contesta lo mismo que uno caducado; la auditoría sin el correo de nadie; sin empresa activa no hay
  equipo; sin token no se ve nada; **darse de alta cuenta como haber entrado**; **solo lectura no
  escribe nada** y sí lee y exporta; **un comercial vende pero no se lleva los datos**; y adivinar la
  contraseña de una invitación se corta sin estorbar a las demás.

Esa última salió de una captura: la lista decía «no ha entrado nunca» junto al nombre de quien estaba
mirando la pantalla, porque el último acceso solo se apuntaba al pasar por el login y registrarse
devuelve la sesión ya iniciada sin pasar por ahí.
