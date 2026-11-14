// Guarda de forma do SQL do baba-db. A barreira real e o papel baba_ro (so SELECT); isto evita erro e abuso barato.
export const MAX_SQL = 20_000;

const PROIBIDAS = /\b(insert|update|delete|truncate|drop|alter|create|grant|revoke|copy|call|do|vacuum|reindex|cluster|lock|listen|notify|set|reset|begin|commit|rollback|savepoint|prepare|execute|refresh|comment|security|pg_sleep|pg_read_file|pg_ls_dir|lo_import|lo_export|dblink|set_config|pg_terminate_backend|pg_cancel_backend)\b/i;

/** Varredura sequencial (string, identificador, dollar-quote e comentario na ordem em que aparecem): devolve so o esqueleto. */
function esqueleto(sql) {
  let o = '', i = 0;
  const n = sql.length;
  while (i < n) {
    const c = sql[i], d = sql[i + 1];
    if (c === '-' && d === '-') { while (i < n && sql[i] !== '\n') i++; o += ' '; continue; }
    if (c === '/' && d === '*') {
      let nivel = 1; i += 2;
      while (i < n && nivel > 0) { if (sql[i] === '/' && sql[i + 1] === '*') { nivel++; i += 2; } else if (sql[i] === '*' && sql[i + 1] === '/') { nivel--; i += 2; } else i++; }
      if (nivel > 0) throw new Error('comentario de bloco nao fechado.');
      o += ' '; continue;
    }
    if (c === "'" || c === '"') {
      i++;
      for (;;) {
        if (i >= n) throw new Error('aspas nao fechadas.');
        if (sql[i] === c) { if (sql[i + 1] === c) { i += 2; continue; } i++; break; }
        i++;
      }
      o += c + c; continue;
    }
    if (c === '$') {
      const m = /^\$([A-Za-z_][A-Za-z_0-9]*)?\$/.exec(sql.slice(i));
      if (m) {
        const fim = sql.indexOf(m[0], i + m[0].length);
        if (fim < 0) throw new Error('dollar-quote nao fechado.');
        i = fim + m[0].length; o += "''"; continue;
      }
    }
    o += c; i++;
  }
  return o;
}

/** Devolve o SQL pronto ou lanca erro. Uma unica instrucao SELECT/WITH/EXPLAIN (sem ANALYZE). */
export function validarSql(entrada) {
  if (typeof entrada !== 'string') throw new Error('sql deve ser texto.');
  const sql = entrada.trim().replace(/;+\s*$/, '');
  if (!sql) throw new Error('sql vazio.');
  if (Buffer.byteLength(sql) > MAX_SQL) throw new Error(`sql maior que ${MAX_SQL} bytes.`);
  if (sql.includes('\\')) throw new Error('barra invertida nao e permitida (meta-comandos do psql).');
  if (sql.includes('\0')) throw new Error('byte nulo.');
  const e = esqueleto(sql);
  if (e.includes(';')) throw new Error('apenas uma instrucao por chamada.');
  if (!/^\s*\(*\s*(select|with|values|table|explain)\b/i.test(e)) throw new Error('so SELECT, WITH, VALUES, TABLE ou EXPLAIN.');
  if (/^\s*explain\b[^]*\banalyze\b/i.test(e)) throw new Error('EXPLAIN ANALYZE executa a consulta: nao permitido.');
  if (/\binto\b/i.test(e)) throw new Error('SELECT INTO nao permitido.');
  if (/\bfor\s+(update|share|no\s+key\s+update|key\s+share)\b/i.test(e)) throw new Error('bloqueio de linha nao permitido.');
  const norm = sql.replace(/\/\*[\s\S]*?\*\//g, '').replace(/[\s"]/g, '').toLowerCase();
  if (/app\.(bypass_rls|empresa_id)|set_config|query_to_xml|cursor_to_xml|table_to_xml|schema_to_xml|database_to_xml|ts_stat|lo_create|pg_read_|pg_ls_/.test(norm)) throw new Error('funcao ou parametro nao permitido.');
  const m = e.match(PROIBIDAS);
  if (m) throw new Error(`palavra nao permitida: ${m[1].toLowerCase()}.`);
  return sql;
}

/** Limita linhas: embrulha em subselect com LIMIT. EXPLAIN nao e embrulhado. */
export function comLimite(sql, max) {
  if (/^\s*explain\b/i.test(sql)) return sql;
  return `SELECT * FROM (\n${sql}\n) AS _q LIMIT ${Number(max)}`;
}
