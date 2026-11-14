#!/usr/bin/env node
// MCP baba-diag: diagnostico somente leitura da Casa da Baba. Spec: docs/plan/mcp-casa-da-baba (#2521).
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { z } from 'zod';
import { executarRemoto, ALVO } from '../lib/ssh.mjs';
import { consultar, ENDPOINTS } from '../lib/saude.mjs';
import { filtrar, formatar, agruparErros } from '../lib/logs.mjs';
import { redigir } from '../lib/redacao.mjs';
import { limitar } from '../lib/limites.mjs';

const texto = t => ({ content: [{ type: 'text', text: t }] });
const falha = e => ({ isError: true, ...texto(`Falha: ${redigir(String(e.message ?? e))}`) });

function shaLocal() {
  try {
    const raiz = fileURLToPath(new URL('../../../..', import.meta.url));
    return execFileSync('git', ['-C', raiz, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
  } catch { return 'desconhecido'; }
}

const since = z.string().regex(/^\d{1,4}[smh]$/).default('1h').describe('Janela: 5m, 2h...');
const tail = z.number().int().min(1).max(5000).default(500);

const server = new McpServer({ name: 'baba-diag', version: '0.1.0' });
const L = { readOnlyHint: true, destructiveHint: false, openWorldHint: false };

server.registerTool('sobre', { description: 'Commit local deste servidor MCP e alvo SSH.', annotations: L, inputSchema: {} },
  async () => texto(`baba-diag 0.1.0\ncommit=${shaLocal()}\nssh=${ALVO}\nmodo=somente-leitura`));

server.registerTool('saude', {
  description: 'GET em /health/* da API publica (status, tempo, corpo redigido).',
  annotations: L,
  inputSchema: { endpoint: z.enum(ENDPOINTS).default('raiz') },
}, async ({ endpoint }) => texto(JSON.stringify(await consultar(endpoint), null, 1)));

server.registerTool('disco_e_fila', {
  description: 'Atalho: /health/disco, /health/dispatcher e /health/notificacoes.', annotations: L, inputSchema: {},
}, async () => texto(JSON.stringify(await Promise.all(['disco', 'dispatcher', 'notificacoes'].map(e => consultar(e))), null, 1)));

server.registerTool('containers', {
  description: 'Estado, health, reinicios e GIT_SHA de ez-api, ez-worker e ez-web (via gate.sh).', annotations: L, inputSchema: {},
}, async () => { try { return texto(limitar(redigir(await executarRemoto('containers')))); } catch (e) { return falha(e); } });

function ferramentaLogs(nome, container) {
  server.registerTool(nome, {
    description: `Logs de ${container} (horas em Brasilia, segredo e PII redigidos, filtro local).`,
    annotations: L,
    inputSchema: {
      since, tail,
      nivel: z.enum(['erro', 'aviso']).optional(),
      texto: z.string().max(100).optional().describe('Trecho literal (nao e regex)'),
    },
  }, async ({ since: s, tail: n, nivel, texto: tx }) => {
    try {
      const bruto = await executarRemoto(`logs ${container} ${s} ${n}`);
      return texto(formatar(filtrar(bruto, { nivel, texto: tx })) || '(sem linhas)');
    } catch (e) { return falha(e); }
  });
}
ferramentaLogs('logs_api', 'ez-api');
ferramentaLogs('logs_worker', 'ez-worker');

server.registerTool('erros_recentes', {
  description: 'Erros e excecoes agrupados por mensagem, com contagem e primeira/ultima hora (Brasilia).',
  annotations: L,
  inputSchema: { alvo: z.enum(['api', 'worker']).default('api'), since, tail: z.number().int().min(1).max(5000).default(3000) },
}, async ({ alvo, since: s, tail: n }) => {
  try {
    const bruto = await executarRemoto(`logs ez-${alvo} ${s} ${n}`);
    return texto(JSON.stringify(agruparErros(bruto), null, 1));
  } catch (e) { return falha(e); }
});

server.registerTool('deploy_historico', {
  description: 'Ultimas linhas do deploy-history.log (data, modo, sha, rc, quem, motivo).', annotations: L,
  inputSchema: { linhas: z.number().int().min(1).max(200).default(20) },
}, async ({ linhas }) => { try { return texto(limitar(redigir(await executarRemoto(`deploy_historico ${linhas}`)))); } catch (e) { return falha(e); } });

await server.connect(new StdioServerTransport());
