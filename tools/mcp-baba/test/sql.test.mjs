import test from 'node:test';
import assert from 'node:assert/strict';
import { validarSql, comLimite } from '../src/lib/sql.mjs';

const ok = ['select 1', 'SELECT * FROM pedidos;', 'with a as (select 1) select * from a', 'explain select 1', "select 'a;b' as x", 'select 1 -- ; drop table x',
  'select * from pedidos where nome = \'insert\'', '(select 1) union (select 2)'];
const ruim = ['select 1; select 2', 'delete from pedidos', 'update pedidos set x=1', 'drop table x', 'select 1; drop table x', 'select * into t from pedidos',
  'explain analyze select 1', 'select * from pedidos for update', 'with x as (delete from pedidos returning *) select * from x', 'select pg_sleep(100)',
  'select set_config(\'app.bypass_rls\',\'on\',false)', 'select 1 \\! id', 'copy pedidos to program \'id\'', 'select $a$;$a$; drop table x', 'do $$ begin end $$', '', '   ',
  'select 1 /* x */; delete from t', 'select dblink(\'x\',\'y\')', 'select lo_export(1,\'/x\')', 'x'.repeat(20_001)];

for (const s of ok) test(`aceita: ${s.slice(0, 40)}`, () => assert.ok(validarSql(s)));
for (const s of ruim) test(`recusa: ${s.slice(0, 40)}`, () => assert.throws(() => validarSql(s)));
test('limite embrulha em subselect', () => assert.match(comLimite('select 1', 50), /LIMIT 50$/));
test('explain nao embrulha', () => assert.equal(comLimite('explain select 1', 50), 'explain select 1'));
