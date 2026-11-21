import test from 'node:test';
import assert from 'node:assert/strict';
import { redigir } from '../src/lib/redacao.mjs';

const casos = [
  ['Authorization: Bearer abc.DEF-123_xyz', 'abc.DEF'],
  [['jwt', ['eyJhbGciOiJIUzI1NiJ9', 'eyJzdWIiOiIxMjM0NTY3ODkwIn0', 'SflKxwRJSMeKKF2QT4fw'].join('.')].join(' '), 'eyJhbGci'],
  ['Cookie: .AspNetCore.Cookies=abcd1234; x=y', 'abcd1234'],
  ['GET /x?sig=SEGREDO123&a=1', 'SEGREDO123'],
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
// LGPD (ADR-0069): nome, endereco e CEP por chave, antes do primeiro token remoto.
const pessoais = [
  ['{"Nome":"Maria da Silva","Pedido":42}', 'Maria'],
  ['nomeCliente=Joao Pereira; total=10', 'Pereira'],
  ['"ClienteNome": "Ana Souza"', 'Souza'],
  ['"Endereco": "Rua das Flores, 100"', 'Flores'],
  ['enderecoEntrega: Av Brasil', 'Brasil'],
  ['logradouro: Av Paulista', 'Paulista'],
  ['bairro=Centro Velho', 'Centro'],
  ['"Complemento":"apto 12 bloco B"', 'bloco'],
  ['"Cep":"01310100"', '01310100'],
  ['entrega no 01310-100 amanha', '01310-100'],
  // revisao da #3066: JSON escapado dentro do log, virgula dentro do valor entre aspas, snake_case
  ['Body: {\\"nome\\":\\"Maria Silva\\",\\"cep\\":\\"01310100\\"}', 'Silva'],
  ['Body: {\\"nome\\":\\"Maria Silva\\",\\"cep\\":\\"01310100\\"}', '01310100'],
  ['"Endereco": "Rua das Flores, 100 - Jardim Paulista, Sao Paulo"', 'Jardim'],
  ["{'endereco': 'Rua A, 5 - Vila Nova'}", 'Vila'],
  ['nome_cliente=Joao Pereira', 'Pereira'],
];
for (const [entrada, dado] of pessoais) {
  test(`redige pessoal: ${entrada.slice(0, 30)}`, () => assert.ok(!redigir(entrada).includes(dado), `vazou: ${redigir(entrada)}`));
}
test('pessoal: preserva ids, produto e numeros de diagnostico', () => {
  for (const t of ['"ClienteId":"3f2a9c1e-0000-4000-8000-000000000001"', '"NomeProduto":"Bolo de cenoura"', 'total=10', '2026-11-21T14:09:19.1234567-03:00', 'Pedido 42 confirmado'])
    assert.equal(redigir(t), t);
  assert.match(redigir('{"Nome":"Maria","Pedido":42}'), /"Pedido":42/);
  // chave=valor sem aspas para antes da proxima chave: o diagnostico da linha fica
  assert.match(redigir('cep=01310100 pedido=42 status=500 rota=/api/x'), /pedido=42 status=500 rota=\/api\/x$/);
  assert.match(redigir('Body: {\\"nome\\":\\"Maria\\",\\"pedido\\":42}'), /\\"pedido\\":42/);
});

test('texto comum fica igual', () => assert.equal(redigir('Pedido 42 confirmado'), 'Pedido 42 confirmado'));
test('linha gigante nao trava (sem ReDoS)', () => {
  const t0 = Date.now();
  redigir('a'.repeat(400_000));
  assert.ok(Date.now() - t0 < 1000);
});
