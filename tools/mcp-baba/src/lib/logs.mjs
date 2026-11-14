import { redigir } from './redacao.mjs';
import { limitar } from './limites.mjs';

const TS = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2})(?:\.\d+)?Z\s?/;
const ERRO = /\b(ERR|ERROR|FATAL|CRT|fail|crit)\b|Exception/;
const NIVEL = { erro: ERRO, aviso: /\b(WRN|WARN|warn)\b|\b(ERR|ERROR|FATAL|CRT|fail|crit)\b|Exception/ };

export function paraBrasilia(isoUtc) {
  const d = new Date(isoUtc + 'Z');
  return new Intl.DateTimeFormat('pt-BR', {
    timeZone: 'America/Sao_Paulo', dateStyle: 'short', timeStyle: 'medium',
  }).format(d);
}

/** Converte o prefixo UTC do `docker logs --timestamps` para Brasilia. */
export function converterHoras(linha) {
  const m = TS.exec(linha);
  return m ? `${paraBrasilia(m[1])} ${linha.slice(m[0].length)}` : linha;
}

/** Filtro e local (sem regex remoto). `texto` e literal, nao regex. */
export function filtrar(saida, { nivel, texto } = {}) {
  let linhas = saida.split(/\r?\n/).filter(Boolean);
  if (nivel && NIVEL[nivel]) linhas = linhas.filter(l => NIVEL[nivel].test(l));
  if (texto) {
    const t = texto.toLowerCase();
    linhas = linhas.filter(l => l.toLowerCase().includes(t));
  }
  return linhas;
}

export function formatar(linhas) {
  return limitar(redigir(linhas.map(converterHoras).join('\n')));
}

function normalizar(l) {
  return l.replace(TS, '').replace(/[0-9a-f]{8}-[0-9a-f-]{27}/gi, '<id>').replace(/\d+/g, 'N').trim().slice(0, 160);
}

/** Agrupa erros por mensagem normalizada, com contagem e primeira/ultima hora (Brasilia). */
export function agruparErros(saida) {
  const grupos = new Map();
  for (const l of saida.split(/\r?\n/)) {
    if (!ERRO.test(l)) continue;
    const chave = redigir(normalizar(l));
    const ts = TS.exec(l)?.[1];
    const g = grupos.get(chave) ?? { mensagem: chave, n: 0, primeira: ts, ultima: ts };
    g.n++; g.ultima = ts ?? g.ultima; g.primeira = g.primeira ?? ts;
    grupos.set(chave, g);
  }
  return [...grupos.values()].sort((a, b) => b.n - a.n).slice(0, 30).map(g => ({
    ...g,
    primeira: g.primeira && paraBrasilia(g.primeira),
    ultima: g.ultima && paraBrasilia(g.ultima),
  }));
}
