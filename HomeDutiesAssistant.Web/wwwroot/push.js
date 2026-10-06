// Web Push interop for PushToggle.razor.
// Clicks are handled HERE, not in Blazor: Notification.requestPermission() must
// run during the user's click, and a Blazor Server click reaches JS only after a
// SignalR round trip (too late — Firefox/Safari would refuse to prompt).
window.pushInterop = (() => {
    const listeners = new WeakMap();

    const supported = () =>
        'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;
    const isIos = () =>
        /iPad|iPhone|iPod/.test(navigator.userAgent) ||
        (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1);
    const isStandalone = () =>
        window.matchMedia('(display-mode: standalone)').matches || navigator.standalone === true;

    const base64UrlToBytes = value => {
        const padded = (value + '='.repeat((4 - value.length % 4) % 4)).replace(/-/g, '+').replace(/_/g, '/');
        return Uint8Array.from(atob(padded), c => c.charCodeAt(0));
    };

    const currentSubscription = async () => {
        const registration = await navigator.serviceWorker.getRegistration('/');
        return registration ? registration.pushManager.getSubscription() : null;
    };

    async function state() {
        if (!supported()) return isIos() && !isStandalone() ? 'ios-not-installed' : 'unsupported';
        if (Notification.permission === 'denied') return 'denied';
        return (await currentSubscription()) ? 'subscribed' : 'not-subscribed';
    }

    async function enable(dotNetRef, publicKey) {
        // Must be the first await after the click (keeps the user activation).
        const permission = await Notification.requestPermission();
        if (permission !== 'granted') {
            await dotNetRef.invokeMethodAsync('StateChanged', await state());
            return;
        }
        const registration = await navigator.serviceWorker.register('/sw.js');
        await navigator.serviceWorker.ready;
        const subscription = await registration.pushManager.getSubscription()
            ?? await registration.pushManager.subscribe({
                userVisibleOnly: true,
                applicationServerKey: base64UrlToBytes(publicKey),
            });
        await sendSubscribed(dotNetRef, subscription);
    }

    const sameBytes = (buffer, bytes) => {
        if (!buffer) return false;
        const view = new Uint8Array(buffer);
        return view.length === bytes.length && view.every((b, i) => b === bytes[i]);
    };

    const sendSubscribed = (dotNetRef, subscription) => {
        const json = subscription.toJSON();
        return dotNetRef.invokeMethodAsync('Subscribed', {
            endpoint: json.endpoint, encryptionKey: json.keys.p256dh, auth: json.keys.auth,
        });
    };

    // On load, an existing subscription is re-saved for the CURRENT user/home
    // (another user may have used this browser), and re-created if the server's
    // VAPID key changed. No requestPermission here — it's already granted.
    async function resync(dotNetRef, publicKey) {
        let subscription = await currentSubscription();
        if (!subscription) return;
        const key = base64UrlToBytes(publicKey);
        if (!sameBytes(subscription.options.applicationServerKey, key)) {
            const registration = await navigator.serviceWorker.getRegistration('/');
            await subscription.unsubscribe();
            subscription = await registration.pushManager.subscribe({
                userVisibleOnly: true, applicationServerKey: key,
            });
        }
        await sendSubscribed(dotNetRef, subscription);
    }

    async function disable(dotNetRef) {
        const subscription = await currentSubscription();
        if (!subscription) {
            await dotNetRef.invokeMethodAsync('StateChanged', 'not-subscribed');
            return;
        }
        const endpoint = subscription.endpoint;
        await subscription.unsubscribe();
        await dotNetRef.invokeMethodAsync('Unsubscribed', endpoint);
    }

    return {
        async init(root, dotNetRef, publicKey) {
            const onClick = event => {
                const button = event.target.closest('[data-push-action]');
                if (!button || !root.contains(button)) return;
                const action = button.dataset.pushAction === 'enable'
                    ? enable(dotNetRef, publicKey)
                    : disable(dotNetRef);
                action.catch(error => dotNetRef.invokeMethodAsync('Failed', String(error?.message ?? error)));
            };
            root.addEventListener('click', onClick);
            listeners.set(root, onClick);
            const current = await state();
            if (current !== 'subscribed') return current;
            try {
                await resync(dotNetRef, publicKey);
                return 'subscribed';
            } catch {
                return 'not-subscribed';
            }
        },
        dispose(root) {
            const onClick = listeners.get(root);
            if (onClick) root.removeEventListener('click', onClick);
            listeners.delete(root);
        },
    };
})();
