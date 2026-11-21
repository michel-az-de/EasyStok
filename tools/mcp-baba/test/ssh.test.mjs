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
