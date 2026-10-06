// Service worker for Web Push. Served from the site root so its scope is "/".
// Payload (from ReminderJob): { title, body, url, tag }.

self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));

self.addEventListener('push', event => {
    const data = event.data ? event.data.json() : {};
    event.waitUntil(self.registration.showNotification(data.title || 'Home Duties Assistant', {
        body: data.body,
        tag: data.tag,          // a later reminder for the same item replaces the earlier one
        renotify: true,         // ...and still alerts (sound/vibration) when it does
        icon: '/icon-192.png',
        data: { url: data.url || '/' },
    }));
});

self.addEventListener('notificationclick', event => {
    event.notification.close();
    const url = new URL(event.notification.data?.url || '/', self.location.origin).href;
    event.waitUntil((async () => {
        const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
        const open = windows.find(client => new URL(client.url).origin === self.location.origin);
        if (open) {
            await open.focus();
            if ('navigate' in open) await open.navigate(url).catch(() => {});
            return;
        }
        await self.clients.openWindow(url);
    })());
});
