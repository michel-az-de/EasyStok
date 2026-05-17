// OpenAPI parser: fetch swagger.json, normaliza tags, indexa para busca.
// Não faz deref upfront (custo) — expõe resolveRef pra resolver sob demanda com cycle detection.
// Cache do JSON cru em IndexedDB, chaveado por idioma; tira segunda visita do request.

const DB_NAME = 'es-console';
const DB_VERSION = 1;
const STORE = 'specs';

function openDb() {
    return new Promise((resolve, reject) => {
        const req = indexedDB.open(DB_NAME, DB_VERSION);
        req.onupgradeneeded = (e) => {
            const db = e.target.result;
            if (!db.objectStoreNames.contains(STORE)) db.createObjectStore(STORE);
        };
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => reject(req.error);
    });
}

async function dbGet(key) {
    try {
        const db = await openDb();
        return await new Promise((resolve, reject) => {
            const tx = db.transaction(STORE, 'readonly');
            const req = tx.objectStore(STORE).get(key);
            req.onsuccess = () => resolve(req.result);
            req.onerror = () => reject(req.error);
        });
    } catch { return null; }
}

async function dbPut(key, value) {
    try {
        const db = await openDb();
        await new Promise((resolve, reject) => {
            const tx = db.transaction(STORE, 'readwrite');
            tx.objectStore(STORE).put(value, key);
            tx.oncomplete = () => resolve();
            tx.onerror = () => reject(tx.error);
        });
    } catch { /* falha silenciosa de cache não derruba app */ }
}

export async function fetchSpec(lang = 'ptbr') {
    const url = `/swagger/v1-${lang}/swagger.json`;
    const cacheKey = `spec:${lang}`;
    const cached = await dbGet(cacheKey);

    const headers = {};
    if (cached?.etag) headers['If-None-Match'] = cached.etag;
    if (cached?.lastModified) headers['If-Modified-Since'] = cached.lastModified;

    let resp;
    try {
        resp = await fetch(url, { headers });
    } catch (e) {
        if (cached?.raw) {
            const parsed = parseSpec(cached.raw);
            parsed._fromCache = true;
            parsed._offline = true;
            return parsed;
        }
        throw new Error(`Não foi possível buscar ${url}: ${e.message || e}`);
    }

    if (resp.status === 304 && cached?.raw) {
        const parsed = parseSpec(cached.raw);
        parsed._fromCache = true;
        return parsed;
    }
    // TTL fallback — se cache tem mais de 30min e o server não suporta etag/304, força re-parse
    const STALE_TTL = 30 * 60 * 1000;
    if (cached?.raw && cached.ts && (Date.now() - cached.ts) < STALE_TTL && !resp.ok) {
        const parsed = parseSpec(cached.raw);
        parsed._fromCache = true;
        parsed._stale = `HTTP ${resp.status}`;
        return parsed;
    }
    if (!resp.ok) {
        if (cached?.raw) {
            const parsed = parseSpec(cached.raw);
            parsed._fromCache = true;
            parsed._stale = `HTTP ${resp.status}`;
            return parsed;
        }
        throw new Error(`Falha ao buscar ${url}: HTTP ${resp.status}`);
    }

    const json = await resp.json();
    const etag = resp.headers.get('etag');
    const lastModified = resp.headers.get('last-modified');
    await dbPut(cacheKey, { etag, lastModified, raw: json, ts: Date.now() });
    return parseSpec(json);
}

function parseSpec(spec) {
    const endpoints = [];
    const byTag = new Map();
    const byOperationId = new Map();

    const paths = spec.paths || {};
    const httpMethods = ['get', 'post', 'put', 'patch', 'delete', 'options', 'head'];
    for (const path of Object.keys(paths)) {
        const pathItem = paths[path];
        if (!pathItem || typeof pathItem !== 'object') continue;
        const pathLevelParameters = pathItem.parameters || [];
        for (const method of httpMethods) {
            const op = pathItem[method];
            if (!op) continue;
            const tag = (op.tags && op.tags[0]) || 'Outros';
            const id = op.operationId || `${method.toUpperCase()}:${path}`;
            const endpoint = {
                id,
                operationId: op.operationId || null,
                path,
                method: method.toUpperCase(),
                tag,
                summary: op.summary || '',
                description: op.description || '',
                parameters: [...pathLevelParameters, ...(op.parameters || [])],
                requestBody: op.requestBody || null,
                responses: op.responses || {},
                security: op.security || spec.security || [],
                deprecated: !!op.deprecated,
                tags: op.tags || [tag],
                raw: op
            };
            endpoints.push(endpoint);
            byOperationId.set(id, endpoint);
            if (!byTag.has(tag)) byTag.set(tag, []);
            byTag.get(tag).push(endpoint);
        }
    }

    const tagMeta = new Map();
    for (const t of (spec.tags || [])) {
        tagMeta.set(t.name, { name: t.name, description: t.description || '' });
    }
    for (const tag of byTag.keys()) {
        if (!tagMeta.has(tag)) tagMeta.set(tag, { name: tag, description: '' });
    }

    const schemas = (spec.components && spec.components.schemas) || {};
    const securitySchemes = (spec.components && spec.components.securitySchemes) || {};

    return {
        raw: spec,
        info: spec.info || {},
        servers: spec.servers || [],
        securitySchemes,
        endpoints,
        byTag,
        byOperationId,
        tagMeta,
        schemas,
        counts: {
            endpoints: endpoints.length,
            tags: byTag.size,
            schemas: Object.keys(schemas).length,
            paths: Object.keys(paths).length
        }
    };
}

// Resolve UM nó com $ref (sem recursão profunda). Retorna { __ref, __resolved } ou { __ref, __circular } ou { __ref, __unresolved }.
// stack é Set<string> com nomes de schemas já abertos no caminho atual da árvore (quem chama controla).
export function resolveRef(spec, node, stack) {
    if (!node || typeof node !== 'object') return node;
    if (typeof node.$ref !== 'string') return node;
    const refName = node.$ref.split('/').pop();
    if (stack && stack.has(refName)) {
        return { __ref: refName, __circular: true };
    }
    const target = spec.schemas[refName];
    if (!target) return { __ref: refName, __unresolved: true };
    return { __ref: refName, __resolved: target };
}

// Indexa endpoints pra busca rápida por substring.
export function buildSearchIndex(parsed) {
    return parsed.endpoints.map(ep => ({
        endpoint: ep,
        haystack: `${ep.method} ${ep.path} ${ep.summary} ${ep.description} ${ep.tag} ${ep.operationId || ''}`.toLowerCase()
    }));
}

// Quais endpoints referenciam um schema (por nome). Útil pro Schemas browser.
export function endpointsUsingSchema(parsed, schemaName) {
    const used = [];
    const target = `#/components/schemas/${schemaName}`;
    for (const ep of parsed.endpoints) {
        const json = JSON.stringify(ep.raw);
        if (json.includes(target)) used.push(ep);
    }
    return used;
}

export async function clearCache() {
    try {
        const db = await openDb();
        await new Promise((resolve, reject) => {
            const tx = db.transaction(STORE, 'readwrite');
            tx.objectStore(STORE).clear();
            tx.oncomplete = () => resolve();
            tx.onerror = () => reject(tx.error);
        });
    } catch {}
}
