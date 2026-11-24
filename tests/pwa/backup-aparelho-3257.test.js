// Testes — #3257 (P3 da #3114): backup automático do aparelho e alerta de dado só local.
//
// 1. A última ação de uma rajada entra no arquivo de backup local (borda final), mesmo dentro
//    do intervalo mínimo de 60s.
// 2. O envio ao servidor é automático, 1x a cada 24h, depois do ciclo de sync.
// 3. dadoSoNoAparelho diz quando há dado sem cópia no servidor; produtos e clientes de
//    exemplo não contam.
// 4. O health check mostra o alerta depois da impressora e antes da idade do backup.
'use strict';

module.exports = function ({ test, sandbox, assert }) {
  const HORA = 60 * 60 * 1000;
  const LAST_LOCAL = 'cdb-last-local-backup-at';
  const LAST_SERVER = 'cdb-last-backup-at';

  // Relógio falso: setTimeout e Date.now do sandbox andam só com avancar(ms).
  async function comRelogio(fn) {
    const timers = [];
    let agora = Date.parse('2026-11-23T12:00:00Z');
    const original = { setTimeout: sandbox.setTimeout, clearTimeout: sandbox.clearTimeout, now: Date.now };
    sandbox.setTimeout = (cb, ms) => { const t = { cb, em: agora + (ms || 0) }; timers.push(t); return t; };
    sandbox.clearTimeout = (t) => { const i = timers.indexOf(t); if (i >= 0) timers.splice(i, 1); };
    Date.now = () => agora;
    async function avancar(ms) {
      const fim = agora + ms;
      for (;;) {
        timers.sort((a, b) => a.em - b.em);
        const proximo = timers[0];
        if (!proximo || proximo.em > fim) break;
        timers.shift();
        agora = proximo.em;
        proximo.cb();
        await new Promise(r => original.setTimeout(r, 0));
      }
      agora = fim;
    }
    try {
      await fn({ avancar, agora: () => agora });
    } finally {
      sandbox.setTimeout = original.setTimeout;
      sandbox.clearTimeout = original.clearTimeout;
      Date.now = original.now;
    }
  }

  async function comBackupNativo(log, fn) {
    const anterior = sandbox.window.Capacitor;
    sandbox.window.Capacitor = { Plugins: { PublicBackup: {
      async writePublicBackup() { log.push('arquivo'); return { documentsUri: 'content://documents', downloadsUri: null }; }
    } } };
    try { await fn(); } finally { sandbox.window.Capacitor = anterior; }
  }

  test('#3257 a última ação de uma rajada entra no backup local, mesmo dentro dos 60s', async () => {
    const log = [];
    // Outro teste pode ter deixado um backup agendado no relógio real.
    sandbox.window.cdbSync._internal.cancelarBackupLocalAgendado();
    await comBackupNativo(log, () => comRelogio(async ({ avancar, agora }) => {
      sandbox.localStorage.setItem(LAST_LOCAL, String(agora() - 30 * 1000));
      sandbox.window.cdbSync.scheduleLocalBackup('action');
      await avancar(5 * 1000);
      assert.deepStrictEqual(log, [], 'dentro dos 60s ainda não grava');
      await avancar(60 * 1000);
      assert.deepStrictEqual(log, ['arquivo'], 'a ação não pode ficar fora do arquivo');
    }));
  });

  test('#3257 sem gravação dobrada nem laço de timer', async () => {
    const log = [];
    const anterior = sandbox.localStorage.getItem(LAST_LOCAL);
    sandbox.window.cdbSync._internal.cancelarBackupLocalAgendado();
    try {
      await comBackupNativo(log, () => comRelogio(async ({ avancar, agora }) => {
        sandbox.localStorage.setItem(LAST_LOCAL, String(agora() - 30 * 1000));
        sandbox.window.cdbSync.scheduleLocalBackup('action');
        sandbox.localStorage.setItem(LAST_LOCAL, String(agora() + 1)); // pre-sync gravou depois da ação
        await avancar(120 * 1000);
        assert.deepStrictEqual(log, [], 'o pre-sync já levou a ação');

        sandbox.localStorage.setItem(LAST_LOCAL, String(agora() + 30 * 24 * HORA)); // relógio voltou
        sandbox.window.cdbSync.scheduleLocalBackup('action');
        await avancar(5 * 1000);
        assert.deepStrictEqual(log, ['arquivo'], 'marca no futuro não trava o backup');
      }));
    } finally {
      if (anterior === null) sandbox.localStorage.removeItem(LAST_LOCAL); else sandbox.localStorage.setItem(LAST_LOCAL, anterior);
    }
  });

  test('#3257 envio ao servidor é automático a cada 24h, não antes', async () => {
    const enviados = [];
    const pairing = sandbox.localStorage.getItem('cdb-pairing');
    sandbox.localStorage.setItem('cdb-pairing', JSON.stringify({ apiKey: 'chave-de-teste', deviceId: 'd' }));
    sandbox.AbortController = AbortController;
    sandbox.fetch = async (url) => {
      enviados.push(String(url));
      return { ok: true, status: 200, headers: { get() { return null; } }, json: async () => ({ id: 'b1' }) };
    };
    try {
      sandbox.localStorage.setItem(LAST_SERVER, String(Date.now() - 23 * HORA));
      await sandbox.window.cdbSync.maybeAutoBackup();
      assert.strictEqual(enviados.length, 0, 'com menos de 24h não envia');
      sandbox.localStorage.setItem(LAST_SERVER, String(Date.now() - 25 * HORA));
      await sandbox.window.cdbSync.maybeAutoBackup();
      assert.ok(enviados.length === 1 && enviados[0].endsWith('/devices/me/backup'), 'com mais de 24h envia: ' + enviados);
    } finally {
      if (pairing === null) sandbox.localStorage.removeItem('cdb-pairing'); else sandbox.localStorage.setItem('cdb-pairing', pairing);
      sandbox.fetch = () => Promise.reject(new Error('fetch nao implementado em testes'));
    }
  });

  const AGORA = Date.parse('2026-11-23T12:00:00Z');
  function estado(extra) {
    return Object.assign({
      agora: AGORA, pareado: true, pareamentoInvalido: false, retidoPorImportacao: false,
      fila: [], deadletter: 0, conflitos: 0, pedidos: 0, caixa: 0, lotes: 0
    }, extra);
  }

  test('#3257 dadoSoNoAparelho: sem dado só local, nada', () => {
    const avaliar = sandbox.window.cdbSync.dadoSoNoAparelho;
    assert.strictEqual(typeof avaliar, 'function', 'cdbSync.dadoSoNoAparelho precisa existir');
    assert.strictEqual(avaliar(estado()), null);
    assert.strictEqual(avaliar(estado({ fila: [{ ts: AGORA - HORA }] })), null, 'fila recente e pareado: o sync resolve');
    assert.strictEqual(avaliar(estado({ pareado: false })), null, 'aparelho vazio sem pareamento');
  });

  test('#3257 dadoSoNoAparelho: cada caso de dado sem cópia no servidor', () => {
    const avaliar = sandbox.window.cdbSync.dadoSoNoAparelho;
    const casos = [
      [{ pareado: false, fila: [{ ts: AGORA }] }, 'sem-pareamento'],
      [{ pareado: false, pedidos: 1 }, 'sem-pareamento'],
      [{ pareado: false, caixa: 1 }, 'sem-pareamento'],
      [{ pareado: false, lotes: 1 }, 'sem-pareamento'],
      [{ pareamentoInvalido: true, fila: [{ ts: AGORA }] }, 'pareamento-invalido'],
      [{ retidoPorImportacao: true, fila: [{ ts: AGORA }] }, 'importacao-retida'],
      [{ fila: [{ ts: AGORA - 25 * HORA }, { createdAt: AGORA }] }, 'fila-antiga'],
    ];
    assert.match(avaliar(estado({ fila: [{ ts: AGORA - 25 * HORA }, { createdAt: AGORA }] })).mensagem, /^1 alteração está/,
      'conta só os itens com mais de um dia');
    assert.strictEqual(avaliar(estado({ deadletter: 2, conflitos: 1 })), null,
      'recusadas e conflitos não têm tela para resolver: alerta permanente seria ruído');
    for (const [extra, motivo] of casos) {
      const r = avaliar(estado(extra));
      assert.ok(r && r.motivo === motivo, JSON.stringify(extra) + ' devia dar ' + motivo + ', deu ' + JSON.stringify(r));
      assert.ok(typeof r.mensagem === 'string' && r.mensagem.length > 0);
    }
  });

  test('#3257 health check mostra o alerta antes da idade do backup', async () => {
    const banners = [];
    const original = { mostrar: sandbox.showHealthBanner, esconder: sandbox.hideHealthBanner, avaliar: sandbox.window.cdbSync.avaliarDadoSoNoAparelho };
    sandbox.showHealthBanner = (nivel, msg, acao) => banners.push({ nivel, msg, acao });
    sandbox.hideHealthBanner = () => banners.push('escondido');
    sandbox.window.cdbSync.avaliarDadoSoNoAparelho = async () => ({ motivo: 'recusadas', mensagem: '2 alterações só neste aparelho' });
    try {
      await sandbox.runHealthCheck();
      assert.deepStrictEqual(banners, [{ nivel: 'warn', msg: '2 alterações só neste aparelho', acao: 'Diagnóstico' }]);
      banners.length = 0;
      sandbox.window.cdbSync.avaliarDadoSoNoAparelho = async () => null;
      await sandbox.runHealthCheck();
      assert.ok(!banners.some(b => b && b.nivel === 'warn'), 'sem dado só local não há alerta: ' + JSON.stringify(banners));
    } finally {
      sandbox.showHealthBanner = original.mostrar;
      sandbox.hideHealthBanner = original.esconder;
      if (original.avaliar) sandbox.window.cdbSync.avaliarDadoSoNoAparelho = original.avaliar;
      else delete sandbox.window.cdbSync.avaliarDadoSoNoAparelho;
    }
  });
};
