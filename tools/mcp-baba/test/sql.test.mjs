import test from 'node:test';
import assert from 'node:assert/strict';
import { validarSql, comLimite } from '../src/lib/sql.mjs';

const ok = [
  "select date_trunc('day', \"CriadoEm\") d, count(*), sum(total)::numeric(12,2) from pedidos group by 1 order by 1", 'select coalesce(a,0) from t where x in (1,2) and exists (select 1 from u)','select 1', 'SELECT * FROM pedidos;', 'with a as (select 1) select * from a', 'explain select 1', "select 'a;b' as x",
  'select * from pedidos where nome = \'insert\'', '(select 1) union (select 2)'];
const ruim = [
  "select (select ts_rewrite('a'::tsquery, 'select 1')) as x", "select 1 from t where u&'x' uescape '!'", 'select "ts_rewrite"(1)', 'select ts_rewrite /* x */ (1)', 'select 1 -- ; drop table x', 'select count(*), pg_catalog.pg_sleep(1) from t',
  "SELECT '--' ) x; COMMIT; SET app . bypass_rls = 'true'; SELECT * FROM (SELECT * FROM clientes", "select 1 /* x */ ; set app . empresa_id = 'x'", "select $$'$$; delete from t",
  "select query_to_xml('select 1', true, true, '')", "select set_config('app'||'.x','1',false)", 'select app/**/.bypass_rls', 'select 1 /* nao fecha', "select 'nao fecha",'select 1; select 2', 'delete from pedidos', 'update pedidos set x=1', 'drop table x', 'select 1; drop table x', 'select * into t from pedidos',
  'explain analyze select 1', 'select * from pedidos for update', 'with x as (delete from pedidos returning *) select * from x', 'select pg_sleep(100)',
  'select set_config(\'app.bypass_rls\',\'on\',false)', 'select 1 \\! id', 'copy pedidos to program \'id\'', 'select $a$;$a$; drop table x', 'do $$ begin end $$', '', '   ',
  'select 1 /* x */; delete from t', 'select dblink(\'x\',\'y\')', 'select lo_export(1,\'/x\')', 'x'.repeat(20_001)];

for (const s of ok) test(`aceita: ${s.slice(0, 40)}`, () => assert.ok(validarSql(s)));
for (const s of ruim) test(`recusa: ${s.slice(0, 40)}`, () => assert.throws(() => validarSql(s)));
test('limite embrulha em subselect', () => assert.match(comLimite('select 1', 50), /LIMIT 50$/));
test('explain nao embrulha', () => assert.equal(comLimite('explain select 1', 50), 'explain select 1'));
