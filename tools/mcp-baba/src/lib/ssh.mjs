import { spawn } from 'node:child_process';

// Alvo SSH: entrada do ~/.ssh/config da pessoa que usa a chave RESTRITA ao gate.sh (usuario baba-mcp).
// Nunca o alias de deploy "hostinger". Ver README.
export const ALVO = process.env.BABA_SSH_TARGET || 'baba-mcp';

const NEGADOS = new Set(['hostinger']);

/** Monta o argv do ssh. Exportado para teste. */
export function argvSsh(comando, alvo = ALVO) {
  if (NEGADOS.has(alvo)) throw new Error('BABA_SSH_TARGET nao pode ser a chave de deploy (hostinger).');
  if (!/^[A-Za-z0-9._@-]+$/.test(alvo)) throw new Error('BABA_SSH_TARGET invalido.');
  if (!/^[a-z_]+(?: [A-Za-z0-9-]+)*$/.test(comando)) throw new Error('comando remoto invalido.');
  return ['-o', 'BatchMode=yes', '-o', 'ConnectTimeout=10', '-T', alvo, comando];
}

export function executarRemoto(comando, { timeoutMs = 30_000, ssh = 'ssh' } = {}) {
  return new Promise((resolve, reject) => {
    const p = spawn(ssh, argvSsh(comando), { stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true });
    let out = '', err = '';
    const t = setTimeout(() => { p.kill(); reject(new Error(`timeout de ${timeoutMs} ms`)); }, timeoutMs);
    p.stdout.on('data', d => { out += d; if (out.length > 4_000_000) p.kill(); });
    p.stderr.on('data', d => { err += d; });
    p.on('error', e => { clearTimeout(t); reject(e); });
    p.on('close', code => {
      clearTimeout(t);
      code === 0 ? resolve(out) : reject(new Error(`ssh saiu com ${code}: ${err.trim().slice(0, 300)}`));
    });
  });
}
