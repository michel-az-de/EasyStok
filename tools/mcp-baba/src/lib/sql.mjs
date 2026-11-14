// Guarda de forma do SQL do baba-db. A barreira real e o papel baba_ro (so SELECT); isto evita erro e abuso barato.
export const MAX_SQL = 20_000;

const PROIBIDAS = /\b(insert|update|delete|truncate|drop|alter|create|grant|revoke|copy|call|do|vacuum|reindex|cluster|lock|listen|notify|set|reset|begin|commit|rollback|savepoint|prepare|execute|refresh|comment|security|pg_sleep|pg_read_file|pg_ls_dir|lo_import|lo_export|dblink|set_config|pg_terminate_backend|pg_cancel_backend)\b/i;

/** Remove comentarios e literais para inspecionar so o esqueleto do SQL. */
function esqueleto(sql) {
  return sql
    .replace(/--[^\n]*/g, ' ')
    .replace(/\/\*[\s\S]*?\*\//g, ' ')
    .replace(/'(?:[^']|'')*'/g, "''")
    .replace(/"(?:[^"]|"")*"/g, '""')
    .replace(/\$([A-Za-z_]*)\$[\s\S]*?\$\1\$/g, "''");
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
  const m = e.match(PROIBIDAS);
  if (m) throw new Error(`palavra nao permitida: ${m[1].toLowerCase()}.`);
  return sql;
}

/** Limita linhas: embrulha em subselect com LIMIT. EXPLAIN nao e embrulhado. */
export function comLimite(sql, max) {
  if (/^\s*explain\b/i.test(sql)) return sql;
  return `SELECT * FROM (\n${sql}\n) AS _q LIMIT ${Number(max)}`;
}
