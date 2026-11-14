import { redigir } from './redacao.mjs';
import { limitar } from './limites.mjs';

export const API_BASE = (process.env.BABA_API_BASE || 'https://api.easystok.online').replace(/\/$/, '');
export const ENDPOINTS = ['raiz', 'live', 'ready', 'api', 'dispatcher', 'notificacoes', 'disco', 'version'];

export async function consultar(nome, { base = API_BASE, fetchImpl = fetch, timeoutMs = 10_000 } = {}) {
  if (!ENDPOINTS.includes(nome)) throw new Error('endpoint de saude desconhecido');
  const url = `${base}/health${nome === 'raiz' ? '' : '/' + nome}`;
  const t0 = Date.now();
  try {
    const r = await fetchImpl(url, { signal: AbortSignal.timeout(timeoutMs) });
    const corpo = limitar(redigir(await r.text()), 4000);
    return { url, status: r.status, ms: Date.now() - t0, corpo };
  } catch (e) {
    return { url, status: null, ms: Date.now() - t0, erro: String(e.message ?? e) };
  }
}
