import { createHash } from 'node:crypto'
import { defineConfig, type Plugin } from 'vite'
import vue from '@vitejs/plugin-vue'

/**
 * Emits sw.js listing every built asset, so the app shell loads with the server unreachable.
 * Navigations are network-first (updates arrive normally); hashed assets are cache-first; the API is never cached.
 */
function serviceWorker(): Plugin {
  return {
    name: 'odysseum-service-worker',
    apply: 'build',
    generateBundle(_, bundle) {
      const assets = ['/index.html', '/favicon.svg', '/manifest.webmanifest', '/icon-192.png', '/icon-512.png', '/icon-maskable-512.png',
        ...Object.keys(bundle).filter(file => file !== 'index.html').map(file => `/${file}`)]
      const version = createHash('sha256').update(assets.join('\n')).digest('hex').slice(0, 12)
      this.emitFile({ type: 'asset', fileName: 'sw.js', source: `const VERSION = 'odysseum-${version}';
const ASSETS = ${JSON.stringify(assets)};
self.addEventListener('install', event => {
  event.waitUntil(caches.open(VERSION).then(cache => cache.addAll(ASSETS)).then(() => self.skipWaiting()));
});
self.addEventListener('activate', event => {
  event.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(key => key !== VERSION).map(key => caches.delete(key)))).then(() => self.clients.claim()));
});
self.addEventListener('fetch', event => {
  const url = new URL(event.request.url);
  if (event.request.method !== 'GET' || url.origin !== self.location.origin) return;
  if (/^\\/(api|health|openapi|scalar)(\\/|$)/.test(url.pathname)) return;
  if (event.request.mode === 'navigate') {
    event.respondWith(fetch(event.request).catch(() => caches.match('/index.html')));
    return;
  }
  event.respondWith(caches.match(event.request).then(hit => hit || fetch(event.request)));
});
` })
    },
  }
}

export default defineConfig({
  plugins: [vue(), serviceWorker()],
  server: {
    port: 5173,
    strictPort: true,
    proxy: { '/api': 'http://127.0.0.1:5080', '/health': 'http://127.0.0.1:5080' },
  },
})
