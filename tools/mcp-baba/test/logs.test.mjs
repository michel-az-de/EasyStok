import test from 'node:test';
import assert from 'node:assert/strict';
import { agruparErros, filtrar, converterHoras, formatar } from '../src/lib/logs.mjs';

const LOG = [
  '2026-11-14T02:00:01.123Z info: Pedido 10 ok',
  '2026-11-14T02:01:00.000Z fail: Falha no pedido 11 Password=abc123',
  '2026-11-14T02:05:00.000Z fail: Falha no pedido 99 Password=zzz',
  '2026-11-14T02:06:00.000Z warn: lento',
].join('\n');

test('agrupa erros por mensagem normalizada e conta', () => {
  const g = agruparErros(LOG);
  assert.equal(g.length, 1);
  assert.equal(g[0].n, 2);
  assert.ok(!JSON.stringify(g).includes('abc123'));
});
test('hora vai para Brasilia (UTC-3)', () => assert.match(converterHoras('2026-11-14T02:00:01Z x'), /13\/11\/2026,? 23:00:01 x/));
test('filtro de texto e literal, nao regex', () => assert.equal(filtrar('a.b\naxb', { texto: 'a.b' }).length, 1));
test('formatar redige', () => assert.ok(!formatar(LOG.split('\n')).includes('abc123')));
test('filtro de texto nao vira oraculo de segredo', () => {
  assert.equal(filtrar('x password=abc123 y', { texto: 'password=abc' }).length, 0);
});
test('JWT no erro agrupado nao vaza payload parcial', () => {
  const jwt = ['eyJhbGciOiJIUzI1NiJ9', 'eyJzdWIiOiJwZXNzb2EifQ', 'assinaturaAssinaturaAssinatura'].join('.');
  const g = agruparErros(`2026-11-14T02:00:00Z fail: ${'x'.repeat(150)} ${jwt}`);
  assert.ok(!JSON.stringify(g).includes('eyJzdWIi'));
});
