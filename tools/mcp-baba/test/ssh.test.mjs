import test from 'node:test';
import assert from 'node:assert/strict';
import { argvSsh } from '../src/lib/ssh.mjs';

test('recusa a chave de deploy', () => assert.throws(() => argvSsh('containers', 'hostinger')));
test('recusa alvo com opcao ou espaco', () => {
  assert.throws(() => argvSsh('containers', '-oProxyCommand=x'));
  assert.throws(() => argvSsh('containers', 'a b'));
});
test('recusa comando com metacaractere', () => {
  for (const c of ['logs ez-api 1h 5; id', 'logs $(id)', 'containers && id', 'a|b', 'logs ez-api\nid'])
    assert.throws(() => argvSsh(c, 'baba-mcp'), c);
});
test('comando valido', () => assert.deepEqual(argvSsh('logs ez-api 1h 50', 'baba-mcp').slice(-2), ['baba-mcp', 'logs ez-api 1h 50']));

// #3078: ssh que morre antes de ler o stdin (EPIPE) rejeita a promessa em vez de derrubar o processo.
test('executarRemoto: ssh que sai sem ler o stdin rejeita sem excecao nao tratada', async () => {
  const { executarRemoto } = await import('../src/lib/ssh.mjs');
  const naoTratadas = [];
  const pega = e => naoTratadas.push(e);
  process.on('uncaughtException', pega);
  try {
    // node -e sai na hora com codigo 255, como um ssh com chave recusada; stdin grande forca o EPIPE.
    const falso = process.execPath;
    await assert.rejects(executarRemoto('containers', { ssh: falso, stdin: 'x'.repeat(8 * 1024 * 1024), timeoutMs: 10_000 }));
    await new Promise(r => setTimeout(r, 200));
  } finally { process.off('uncaughtException', pega); }
  assert.deepEqual(naoTratadas.map(e => e.code), []);
});
