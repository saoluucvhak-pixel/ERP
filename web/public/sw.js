// Service worker của HAK: mở được khung ứng dụng khi mất kết nối; mọi lời gọi /api luôn đi thẳng tới máy chủ (không lưu số liệu kế toán trong bộ nhớ đệm).
const CACHE = 'hak-shell-v1'
self.addEventListener('install', () => self.skipWaiting())
self.addEventListener('activate', e => e.waitUntil(
  caches.keys().then(keys => Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k)))).then(() => self.clients.claim())))
self.addEventListener('fetch', e => {
  const u = new URL(e.request.url)
  if (e.request.method !== 'GET' || u.origin !== self.location.origin || u.pathname.startsWith('/api/')) return
  e.respondWith(fetch(e.request).then(res => {
    if (res.ok) { const copy = res.clone(); caches.open(CACHE).then(c => c.put(e.request, copy)) }
    return res
  }).catch(() => caches.match(e.request).then(hit => hit || caches.match('/'))))
})
