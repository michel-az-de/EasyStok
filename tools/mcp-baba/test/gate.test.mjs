import test from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const GATE = fileURLToPath(new URL('../../../scripts/mcp/gate.sh', import.meta.url));
const temBash = spawnSync('bash', ['-c', 'true']).status === 0;
const gate = cmd => spawnSync('bash', [GATE], { env: { ...process.env, GATE_DRY: '1', SSH_ORIGINAL_COMMAND: cmd }, encoding: 'utf8' });

test('gate.sh: aceita comandos da lista', { skip: !temBash }, () => {
  assert.match(gate('logs ez-api 1h 100').stdout, /logs --timestamps --since 1h --tail 100 ez-api/);
  assert.match(gate('containers').stdout, /ps -a --filter name=ez-/);
  assert.match(gate('deploy_historico 5').stdout, /tail -n 5 /);
});

test('gate.sh: recusa injecao e fora da lista', { skip: !temBash }, () => {
  for (const c of [
    'logs ez-api 1h 100; id', 'logs ez-api $(id) 100', 'logs ez-db 1h 100', 'logs ez-api 1h 99999',
    'logs ez-api 1h', 'logs ez-api 1h 100 extra', 'containers x', 'deploy_historico 9999',
    'exec ez-api sh', 'bash', '', 'logs ez-api 1h `id`', 'logs ez-api 1h 100 && id', 'logs ez-api -1h 100',
  ]) {
    const r = gate(c);
    assert.equal(r.status, 64, `deveria recusar: ${JSON.stringify(c)} (saiu ${r.status}: ${r.stdout})`);
  }
});
