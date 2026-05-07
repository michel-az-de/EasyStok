// Store central minimalista. Imutabilidade rasa, pub/sub, persistência seletiva em localStorage.

const STORAGE_PREFIX = 'es_console_';
const PERSISTED = ['lang', 'theme', 'soundOn', 'visited', 'auth'];

const listeners = new Set();
let state = loadInitial();

function loadInitial() {
    const defaults = {
        lang: 'ptbr',
        theme: 'dark',
        soundOn: false,
        visited: false,
        auth: { token: null, user: null, expiresAt: null },
        spec: null,
        parserError: null,
        parsing: true,
        booting: true,
        route: { path: '#/', page: 'home', module: null, method: null, endpointId: null, tab: 'briefing', body: null, schema: null },
        health: { ok: null, lastCheck: null, ms: null },
        toast: null
    };
    for (const key of PERSISTED) {
        try {
            const raw = localStorage.getItem(STORAGE_PREFIX + key);
            if (raw !== null) defaults[key] = JSON.parse(raw);
        } catch { /* ignora chave corrompida */ }
    }
    return defaults;
}

export function get() {
    return state;
}

export function set(patch) {
    state = { ...state, ...patch };
    for (const key of Object.keys(patch)) {
        if (PERSISTED.includes(key)) {
            try {
                localStorage.setItem(STORAGE_PREFIX + key, JSON.stringify(state[key]));
            } catch { /* quota cheia ou modo privado */ }
        }
    }
    for (const fn of listeners) {
        try { fn(state); }
        catch (err) { console.error('[store] listener error', err); }
    }
}

export function subscribe(fn) {
    listeners.add(fn);
    return () => listeners.delete(fn);
}

export function update(key, fn) {
    set({ [key]: fn(state[key]) });
}

export function reset(key) {
    try { localStorage.removeItem(STORAGE_PREFIX + key); } catch {}
    state = { ...state, [key]: loadInitial()[key] };
    for (const fn of listeners) fn(state);
}
