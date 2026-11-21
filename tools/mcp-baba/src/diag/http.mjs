#!/usr/bin/env node
// Entrada remota do baba-diag (ADR-0069): Streamable HTTP sem sessao, atras do Caddy em mcp.easystok.online/diag.
// Token Bearer por pessoa, arquivo "pessoa:sha256:validade" relido a cada pedido (revogar = apagar a linha).
// Host conferido, limite por token e por IP, auditoria em stdout sem o token e sem a saida das ferramentas.
import { createHash, timingSafeEqual } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { createServer } from 'node:http';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { StreamableHTTPServerTransport } from '@modelcontextprotocol/sdk/server/streamableHttp.js';
import { criarServidorDiag } from './fabrica.mjs';
import { redigir } from '../lib/redacao.mjs';

export const hashToken = t => createHash('sha256').update(String(t)).digest('hex');

function lerTokens(arquivo) {
  let bruto;
  try { bruto = readFileSync(arquivo, 'utf8'); } catch { return []; }
  return bruto.split('\n').map(l => l.trim()).filter(l => l && !l.startsWith('#')).map(l => l.split(':'))
    .filter(p => p.length === 3 && /^[a-z0-9._-]{1,40}$/.test(p[0]) && /^[0-9a-f]{64}$/.test(p[1]) && /^\d{4}-\d{2}-\d{2}$/.test(p[2]))
    .map(([pessoa, hash, validade]) => ({ pessoa, hash: Buffer.from(hash, 'hex'), validade }));
}

// Compara com todos os hashes, em tempo constante, sem sair no primeiro.
function autenticar(cabecalho, arquivo, hoje) {
  const m = /^Bearer ([A-Za-z0-9._~+/=-]{20,200})$/.exec(cabecalho ?? '');
  if (!m) return { presente: Boolean(cabecalho), pessoa: null };
  const h = createHash('sha256').update(m[1]).digest();
  let pessoa = null;
  for (const t of lerTokens(arquivo)) if (timingSafeEqual(h, t.hash) && t.validade >= hoje && !pessoa) pessoa = t.pessoa;
  return { presente: true, pessoa };
}

function responder(res, status, corpo, cabecalhos = {}) {
  res.writeHead(status, { 'content-type': 'application/json', 'cache-control': 'no-store', ...cabecalhos });
  res.end(JSON.stringify(corpo));
}

// O Caddy troca o X-Forwarded-For pelo IP real; o ultimo valor e o dele.
const ipDe = req => (req.headers['x-forwarded-for'] ?? '').split(',').at(-1).trim() || req.socket.remoteAddress || '?';
const hostDe = req => (req.headers.host ?? '').replace(/:\d+$/, '').toLowerCase();

function janela(mapa, chave, agora, ms) {
  let e = mapa.get(chave);
  if (!e || agora - e.inicio >= ms) { e = { inicio: agora, n: 0 }; mapa.set(chave, e); }
  return e;
}
function podar(mapa, agora, ms, teto = 5000) {
  if (mapa.size < teto) return;
  for (const [k, e] of mapa) if (agora - e.inicio >= ms) mapa.delete(k);
  // rotacao de IP (IPv6) sem nada vencido: descarta os mais antigos ate a metade, para o custo nao crescer por pedido
  for (const k of mapa.keys()) { if (mapa.size < teto / 2) break; mapa.delete(k); }
}

function lerCorpo(req, max) {
  return new Promise((ok, falha) => {
    if (Number(req.headers['content-length'] ?? 0) > max) { req.resume(); return falha(Object.assign(new Error('grande'), { status: 413, fechar: true })); }
    const partes = []; let n = 0;
    // passou do limite: para de ler e responde na hora, sem esperar o fim de um corpo chunked (#3078)
    const estourou = () => { req.removeAllListeners('data'); req.pause(); falha(Object.assign(new Error('grande'), { status: 413, fechar: true })); };
    req.on('data', d => { n += d.length; if (n > max) return estourou(); partes.push(d); });
    req.on('end', () => ok(Buffer.concat(partes).toString('utf8')));
    req.on('error', falha);
  });
}

export function criarApp({
  hosts = ['mcp.easystok.online'], origens = [], tokens = '/run/mcp/tokens', criarServidor = criarServidorDiag,
  limitePorMinuto = 60, falhasPorIp = 10, bloqueioMs = 15 * 60_000, maxCorpo = 64 * 1024, agora = Date.now,
  auditar = l => console.log(JSON.stringify(l)),
} = {}) {
  const porPessoa = new Map(), falhas = new Map();
  return async (req, res) => {
    const caminho = (req.url ?? '').split('?')[0];
    if (caminho === '/saude' && req.method === 'GET') return responder(res, 200, { ok: true });
    if (caminho !== '/diag') return responder(res, 404, { erro: 'nao encontrado' });
    if (!hosts.includes(hostDe(req))) return responder(res, 403, { erro: 'host' });
    if (req.headers.origin && !origens.includes(req.headers.origin)) return responder(res, 403, { erro: 'origem' });

    const t = agora(), ip = ipDe(req), quando = new Date(t).toISOString();
    podar(falhas, t, bloqueioMs); podar(porPessoa, t, 60_000);
    const f = falhas.get(ip);
    if (f && t - f.inicio < bloqueioMs && f.n >= falhasPorIp) return responder(res, 429, { erro: 'tente mais tarde' }, { 'retry-after': '900' });

    const { presente, pessoa } = autenticar(req.headers.authorization, tokens, quando.slice(0, 10));
    if (!pessoa) {
      if (presente) janela(falhas, ip, t, bloqueioMs).n++;
      auditar({ quando, ip, evento: presente ? 'token-invalido' : 'sem-token' });
      return responder(res, 401, { erro: 'nao autorizado' }, { 'www-authenticate': 'Bearer' });
    }
    if (++janela(porPessoa, pessoa, t, 60_000).n > limitePorMinuto) return responder(res, 429, { erro: 'limite por minuto' }, { 'retry-after': '60' });
    // Sem sessao nao ha fluxo SSE de GET nem DELETE de sessao: o cliente aceita 405.
    if (req.method !== 'POST') return responder(res, 405, { erro: 'so POST' }, { allow: 'POST' });

    let corpo;
    try { corpo = JSON.parse(await lerCorpo(req, maxCorpo)); } catch (e) {
      return responder(res, e.status ?? 400, { erro: e.status ? 'corpo grande' : 'json invalido' }, e.fechar ? { connection: 'close' } : {});
    }
    const servidor = criarServidor({
      aoChamar: c => auditar({ quando: new Date(agora()).toISOString(), pessoa, ip, ferramenta: c.ferramenta,
        args: redigir(JSON.stringify(c.args ?? {})).slice(0, 300), ms: c.ms, ok: c.ok }),
    });
    const transporte = new StreamableHTTPServerTransport({ sessionIdGenerator: undefined, enableJsonResponse: true });
    res.on('close', () => { transporte.close(); servidor.close(); });
    try {
      await servidor.connect(transporte);
      await transporte.handleRequest(req, res, corpo);
    } catch (e) {
      auditar({ quando, pessoa, ip, evento: 'erro', erro: redigir(String(e?.message ?? e)).slice(0, 200) });
      if (!res.headersSent) responder(res, 500, { erro: 'interno' });
    }
  };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const porta = Number(process.env.PORT ?? 8080);
  const hosts = (process.env.MCP_HOSTS ?? 'mcp.easystok.online').split(',').map(s => s.trim().toLowerCase()).filter(Boolean);
  const app = criarApp({ hosts, tokens: process.env.MCP_TOKENS ?? '/run/mcp/tokens' });
  createServer({ requestTimeout: 60_000, headersTimeout: 15_000 }, app)
    .listen(porta, '0.0.0.0', () => console.log(JSON.stringify({ evento: 'no-ar', porta, hosts, commit: process.env.GIT_SHA ?? 'desconhecido' })));
}
