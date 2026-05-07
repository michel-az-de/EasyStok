// Hash router com pushState pra preservar back/forward com state.
// Formato: #/   |  #/m/{moduleId}   |   #/m/{moduleId}/{METHOD}/{operationId}   |   #/schemas[/{name}]   |   #/boot
// Query: ?tab=try&body=<base64>

import * as store from './store.js';

export function parseHash(hash) {
    hash = hash || '#/';
    if (!hash.startsWith('#/')) hash = '#/';
    const [pathPart, queryPart] = hash.slice(2).split('?');
    const segments = pathPart.split('/').filter(Boolean);
    const params = new URLSearchParams(queryPart || '');

    const route = {
        path: hash,
        page: 'home',
        module: null,
        method: null,
        endpointId: null,
        tab: params.get('tab') || 'briefing',
        body: params.get('body') ? safeDecodeBody(params.get('body')) : null,
        schema: null
    };

    if (segments[0] === 'm' && segments[1]) {
        route.page = 'module';
        route.module = decodeURIComponent(segments[1]);
        if (segments[2] && segments[3]) {
            route.method = segments[2].toUpperCase();
            route.endpointId = decodeURIComponent(segments[3]);
            route.page = 'endpoint';
        }
    } else if (segments[0] === 'schemas') {
        route.page = 'schemas';
        if (segments[1]) route.schema = decodeURIComponent(segments[1]);
    } else if (segments[0] === 'boot') {
        route.page = 'boot';
    }
    return route;
}

function safeDecodeBody(b64) {
    try {
        const decoded = atob(b64);
        return decodeURIComponent(escape(decoded));
    } catch {
        return null;
    }
}

function safeEncodeBody(text) {
    try {
        return btoa(unescape(encodeURIComponent(text)));
    } catch {
        return '';
    }
}

export function buildHash(route) {
    let h = '#/';
    if (route.page === 'module' || route.page === 'endpoint') {
        h = `#/m/${encodeURIComponent(route.module)}`;
        if (route.page === 'endpoint' && route.method && route.endpointId) {
            h += `/${route.method}/${encodeURIComponent(route.endpointId)}`;
        }
    } else if (route.page === 'schemas') {
        h = '#/schemas';
        if (route.schema) h += '/' + encodeURIComponent(route.schema);
    } else if (route.page === 'boot') {
        h = '#/boot';
    }
    const params = new URLSearchParams();
    if (route.tab && route.tab !== 'briefing') params.set('tab', route.tab);
    if (route.body) params.set('body', safeEncodeBody(route.body));
    const q = params.toString();
    return q ? `${h}?${q}` : h;
}

export function navigate(routePartial) {
    const current = store.get().route;
    const next = { ...current, ...routePartial };
    const hash = buildHash(next);
    next.path = hash;
    if (location.hash !== hash) {
        history.pushState({ route: next }, '', hash);
    }
    store.set({ route: next });
}

export function replace(routePartial) {
    const current = store.get().route;
    const next = { ...current, ...routePartial };
    const hash = buildHash(next);
    next.path = hash;
    history.replaceState({ route: next }, '', hash);
    store.set({ route: next });
}

export function init() {
    const handler = () => {
        const route = parseHash(location.hash);
        if (route.path !== store.get().route.path) {
            store.set({ route });
        }
    };
    window.addEventListener('hashchange', handler);
    window.addEventListener('popstate', handler);
    handler();
}
