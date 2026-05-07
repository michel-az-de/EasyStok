// Login JWT + decode + persistência. Token vive em localStorage via store.

import * as store from './store.js';

export async function login(email, senha, empresaId) {
    const body = { email, senha };
    if (empresaId) body.empresaId = empresaId;

    let resp;
    try {
        resp = await fetch('/api/auth/login', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
            body: JSON.stringify(body)
        });
    } catch (e) {
        throw new Error(`Falha de rede: ${e.message || e}`);
    }

    if (!resp.ok) {
        let detail = '';
        try {
            const j = await resp.json();
            detail = j.error?.message || j.message || j.title || JSON.stringify(j);
        } catch {
            try { detail = await resp.text(); } catch {}
        }
        throw new Error(`HTTP ${resp.status} ${resp.statusText}${detail ? ' — ' + detail : ''}`);
    }

    let json;
    try { json = await resp.json(); }
    catch { throw new Error('Resposta inválida (não-JSON)'); }

    const data = json.data || json;
    const token = data.accessToken || data.token || data.access_token;
    if (!token) throw new Error('Resposta sem accessToken — verifique formato esperado');

    const claims = decodeJwt(token);
    const expiresAt = claims?.exp ? claims.exp * 1000 : null;

    store.set({
        auth: {
            token,
            user: {
                id: claims?.sub || claims?.id || null,
                name: claims?.unique_name || claims?.name || data.usuario?.nome || data.nome || email,
                email: claims?.email || data.usuario?.email || data.email || email,
                level: claims?.role || claims?.nivel || data.usuario?.nivel || null,
                empresaId: claims?.empresa_id || claims?.EmpresaId || empresaId || null
            },
            expiresAt
        }
    });
    return { token, user: store.get().auth.user, claims };
}

export function logout() {
    store.set({ auth: { token: null, user: null, expiresAt: null } });
}

export function isExpired() {
    const a = store.get().auth;
    return !!(a?.expiresAt && a.expiresAt < Date.now());
}

export function decodeJwt(token) {
    if (!token || typeof token !== 'string') return null;
    try {
        const parts = token.split('.');
        if (parts.length !== 3) return null;
        const b64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
        const padded = b64 + '==='.slice((b64.length + 3) % 4);
        const raw = atob(padded);
        const bytes = new Uint8Array(raw.length);
        for (let i = 0; i < raw.length; i++) bytes[i] = raw.charCodeAt(i);
        let utf8;
        try { utf8 = new TextDecoder('utf-8', { fatal: false }).decode(bytes); }
        catch { utf8 = raw; }
        return JSON.parse(utf8);
    } catch {
        return null;
    }
}

export function authHeader() {
    const t = store.get().auth?.token;
    return t ? { Authorization: `Bearer ${t}` } : {};
}

export function uuid() {
    if (typeof crypto !== 'undefined' && crypto.randomUUID) return crypto.randomUUID();
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c => {
        const r = (Math.random() * 16) | 0;
        const v = c === 'x' ? r : ((r & 0x3) | 0x8);
        return v.toString(16);
    });
}
