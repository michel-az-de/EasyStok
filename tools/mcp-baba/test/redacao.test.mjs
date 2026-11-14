import test from 'node:test';
import assert from 'node:assert/strict';
import { redigir } from '../src/lib/redacao.mjs';

const casos = [
  ['Authorization: Bearer abc.DEF-123_xyz', 'abc.DEF'],
  ['jwt eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.SflKxwRJSMeKKF2QT4fw', 'eyJhbGci'],
  ['password=hunter2 ok', 'hunter2'],
  ['"Senha": "s3gr3d0!"', 's3gr3d0'],
  ['Host=db;Port=5432;Database=x;Username=u;Password=p4ss', 'p4ss'],
  ['postgres://u:pw@host:5432/db', 'pw@'],
  ['cliente maria@example.com ligou', 'maria@example.com'],
  ['cpf 123.456.789-09', '123.456.789-09'],
  ['tel (11) 98765-4321', '98765-4321'],
  ['apiKey: sk-live-abcdef', 'sk-live-abcdef'],
];

for (const [entrada, segredo] of casos) {
  test(`redige: ${entrada.slice(0, 30)}`, () => {
    const s = redigir(entrada);
    assert.ok(!s.includes(segredo), `vazou: ${s}`);
  });
}
test('texto comum fica igual', () => assert.equal(redigir('Pedido 42 confirmado'), 'Pedido 42 confirmado'));
