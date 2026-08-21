/*
 * Trabajador de servicio de match.keting.
 *
 * Hace **una sola cosa**: guardar el armazón de la aplicación para que abra al instante y para que
 * abra también cuando no hay cobertura. Un comercial usa esto entre visitas, en un portal, en un
 * polígono; que la aplicación tarde ocho segundos en pintarse es motivo suficiente para no volver a
 * abrirla.
 *
 * Lo que **no** hace, a propósito:
 *
 * - No guarda respuestas de la API. Una pila de repaso de hace tres días es peor que ninguna: harías
 *   decisiones sobre cosas que ya cambiaron. Los datos siempre son de ahora o no son.
 * - No encola respuestas. La cola del repaso existe, pero vive **en la página**, no aquí: hace falta la
 *   sesión para enviarlas y no se manda nada con la aplicación cerrada. Ver `docs/movil.md`. Lo que
 *   sigue en pie es la regla: nada se recalcula en el móvil, así que el embudo nunca finge.
 *
 * Así que la regla es simple: **el armazón, de la caché; los datos, de la red.**
 */
'use strict';

// Al cambiar de versión se descarta la caché anterior entera. Es más barato que invalidar por fichero
// y no deja nunca una mezcla de dos versiones, que es de lo más difícil de depurar que hay.
const CACHE = 'matchketing-v20';

const ARMAZON = [
  '/',
  '/index.html',
  '/manifiesto.webmanifest',
  '/iconos/mk-192.png',
  '/iconos/mk-512.png',
];

self.addEventListener('install', evento => {
  evento.waitUntil(
    caches.open(CACHE)
      .then(cache => cache.addAll(ARMAZON))
      .then(() => self.skipWaiting()));
});

self.addEventListener('activate', evento => {
  evento.waitUntil(
    caches.keys()
      .then(claves => Promise.all(claves.filter(c => c !== CACHE).map(c => caches.delete(c))))
      .then(() => self.clients.claim()));
});

self.addEventListener('fetch', evento => {
  const peticion = evento.request;

  // Solo GET del propio origen. Un POST nunca se sirve de caché, y de otros dominios no nos metemos.
  if (peticion.method !== 'GET' || new URL(peticion.url).origin !== self.location.origin) {
    return;
  }

  const ruta = new URL(peticion.url).pathname;

  // **Lista blanca del armazón, no lista negra de la API.**
  //
  // Antes esto era al revés: una expresión con los prefijos de la API («repaso», «contactos», …) y
  // todo lo demás se guardaba. Eso falla abierto: el día que se añade un módulo nuevo, su ruta no está
  // en la lista, el trabajador la trata como si fuera un icono y **sirve datos desde la caché**. Pasó
  // con `/webhooks`: al crear uno, el listado seguía devolviendo el de antes, y solo aparecía tras
  // recargar. Es justo lo que este fichero dice que nunca hace.
  //
  // Al revés falla cerrado: una ruta nueva no está en la lista blanca, así que va a la red. Lo que hay
  // que acordarse de añadir es un fichero estático nuevo, y eso se nota al instante porque no se
  // guarda; olvidarse de una ruta de API no se notaba nunca.
  const esArmazon = ruta === '/'
    || ruta === '/index.html'
    || ruta === '/manifiesto.webmanifest'
    || ruta.startsWith('/iconos/');

  if (!esArmazon) {
    return;
  }

  // Armazón: primero la caché para que abra instantáneo, y en segundo plano se refresca. Si no hay
  // red y tampoco caché, se responde con la raíz: es una aplicación de una sola página, así que la
  // raíz es una respuesta válida para cualquier ruta de navegación.
  evento.respondWith(
    caches.match(peticion).then(guardada => {
      const desdeRed = fetch(peticion)
        .then(respuesta => {
          if (respuesta && respuesta.ok) {
            const copia = respuesta.clone();
            caches.open(CACHE).then(cache => cache.put(peticion, copia));
          }
          return respuesta;
        })
        .catch(() => guardada || caches.match('/'));

      return guardada || desdeRed;
    }));
});

/*
 * ---------- Avisos push ----------
 *
 * El cuerpo llega cifrado y el navegador ya lo ha descifrado por nosotros: aquí solo hay que pintarlo.
 */

self.addEventListener('push', function (evento) {
  // Un aviso **siempre** se muestra. Si el trabajador de servicio recibe un push y no enseña nada, el
  // navegador puede acabar revocando el permiso, y entonces se pierden todos los avisos futuros. Así
  // que si el cuerpo no se entiende, se enseña algo genérico en vez de callarse.
  var aviso = { titulo: 'match.keting', cuerpo: 'Tienes algo que revisar.', ruta: '/' };
  try {
    if (evento.data) { aviso = Object.assign(aviso, evento.data.json()); }
  } catch (e) { /* cuerpo ilegible: se muestra el genérico */ }

  evento.waitUntil(self.registration.showNotification(aviso.titulo, {
    body: aviso.cuerpo,
    icon: '/iconos/mk-192.png',
    badge: '/iconos/mk-192-recortable.png',
    lang: 'es-ES',
    // La etiqueta hace que un aviso nuevo **sustituya** al anterior en vez de apilarse. Tres avisos
    // del repaso en la bandeja son tres motivos para apagarlos.
    tag: 'repaso',
    renotify: false,
    data: { ruta: aviso.ruta || '/' }
  }).then(function () {
    // El número también en el icono, para que siga ahí cuando el aviso se descarte.
    if (typeof aviso.cuantas === 'number' && navigator.setAppBadge) {
      return navigator.setAppBadge(aviso.cuantas).catch(function () {});
    }
  }));
});

self.addEventListener('notificationclick', function (evento) {
  evento.notification.close();
  var ruta = (evento.notification.data && evento.notification.data.ruta) || '/';

  // Si la aplicación ya está abierta se reutiliza esa ventana en vez de abrir otra: encontrarse tres
  // pestañas de la misma aplicación es de las cosas que más se notan y menos se perdonan.
  evento.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(function (abiertas) {
      for (var i = 0; i < abiertas.length; i++) {
        if (new URL(abiertas[i].url).origin === self.location.origin) {
          return abiertas[i].navigate(ruta).then(function (c) { return c && c.focus(); });
        }
      }
      return self.clients.openWindow(ruta);
    }));
});
