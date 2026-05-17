// Onda 2.2 — Web Push subscription para o PWA.
// Pede permission de notificacoes APENAS apos login (chamado pelo codigo que faz auth).
// Nao roda no boot pra evitar prompt incomodo de notifications antes do usuario interagir.

(function () {
  'use strict';

  const SUBSCRIBED_KEY = 'cdb-webpush-subscribed';
  const DECLINED_KEY = 'cdb-webpush-declined';

  function urlBase64ToUint8Array(base64String) {
    const padding = '='.repeat((4 - base64String.length % 4) % 4);
    const base64 = (base64String + padding).replace(/-/g, '+').replace(/_/g, '/');
    const raw = atob(base64);
    const out = new Uint8Array(raw.length);
    for (let i = 0; i < raw.length; i++) out[i] = raw.charCodeAt(i);
    return out;
  }

  function bufToBase64Url(buf) {
    const bytes = new Uint8Array(buf);
    let bin = '';
    for (let i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
    return btoa(bin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  }

  async function getVapidPublic() {
    const r = await fetch('/api/pwa/push/vapid-public', { credentials: 'same-origin' });
    if (!r.ok) throw new Error('vapid-public retornou ' + r.status);
    const data = await r.json();
    const inner = data.data ?? data;
    if (!inner.publicKey) throw new Error('publicKey ausente');
    return inner.publicKey;
  }

  async function getSwReg() {
    if (!('serviceWorker' in navigator)) throw new Error('Service Worker nao suportado neste browser.');
    return await navigator.serviceWorker.ready;
  }

  async function postSubscribe(sub) {
    const json = sub.toJSON();
    return fetch('/api/pwa/push/subscribe', {
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        endpoint: json.endpoint,
        p256dh: json.keys.p256dh,
        auth: json.keys.auth,
        userAgent: navigator.userAgent
      })
    });
  }

  /**
   * Solicita permission e cria/atualiza subscription. Idempotente — se ja subscrito,
   * apenas garante que o backend tem o registro atualizado.
   * @returns {Promise<{status: 'subscribed'|'denied'|'unsupported'|'error', error?: string}>}
   */
  async function enableWebPush() {
    try {
      if (!('Notification' in window) || !('PushManager' in window)) {
        return { status: 'unsupported' };
      }

      let permission = Notification.permission;
      if (permission === 'default') {
        permission = await Notification.requestPermission();
      }
      if (permission !== 'granted') {
        localStorage.setItem(DECLINED_KEY, '1');
        return { status: 'denied' };
      }

      const publicKey = await getVapidPublic();
      const reg = await getSwReg();
      let sub = await reg.pushManager.getSubscription();
      if (!sub) {
        sub = await reg.pushManager.subscribe({
          userVisibleOnly: true,
          applicationServerKey: urlBase64ToUint8Array(publicKey)
        });
      }

      const r = await postSubscribe(sub);
      if (!r.ok) {
        return { status: 'error', error: 'backend subscribe ' + r.status };
      }
      localStorage.setItem(SUBSCRIBED_KEY, '1');
      localStorage.removeItem(DECLINED_KEY);
      return { status: 'subscribed' };
    } catch (e) {
      return { status: 'error', error: e.message || String(e) };
    }
  }

  /** Desativa: avisa backend e remove subscription do browser. */
  async function disableWebPush() {
    try {
      const reg = await getSwReg();
      const sub = await reg.pushManager.getSubscription();
      if (sub) {
        const endpoint = sub.endpoint;
        try { await fetch('/api/pwa/push/unsubscribe?endpoint=' + encodeURIComponent(endpoint), { method: 'DELETE', credentials: 'same-origin' }); }
        catch { /* segue tentando unsub local */ }
        await sub.unsubscribe();
      }
      localStorage.removeItem(SUBSCRIBED_KEY);
      localStorage.setItem(DECLINED_KEY, '1');
      return { status: 'ok' };
    } catch (e) {
      return { status: 'error', error: e.message || String(e) };
    }
  }

  function isSubscribed() {
    return localStorage.getItem(SUBSCRIBED_KEY) === '1';
  }

  function wasDeclined() {
    return localStorage.getItem(DECLINED_KEY) === '1';
  }

  // API global — chamada pelo codigo do app apos login.
  window.cdbWebPush = {
    enable: enableWebPush,
    disable: disableWebPush,
    isSubscribed,
    wasDeclined
  };
})();
