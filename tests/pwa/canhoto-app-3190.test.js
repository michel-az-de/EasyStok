'use strict';
// #3190: "Enviar canhoto" no modal de cobrança. A imagem vem do servidor pelo id do pedido do app e sai pelo plugin
// nativo para o WhatsApp do celular da dona. O registro "aberto" só sai depois que o compartilhamento abriu.
module.exports = function ({ test, sandbox, reset, assert, runInSandbox }) {
  const PNG = new Uint8Array([0x89, 0x50, 0x4e, 0x47]);

  async function cenario({ status = 200, shareFalha = null, semPlugin = false }, fn) {
    const old = { cap: sandbox.window.Capacitor, fetch: sandbox.fetch, online: sandbox.navigator.onLine, toast: runInSandbox('showToast'), reader: sandbox.FileReader };
    const log = { fetch: [], escrito: [], share: [], toasts: [] };
    reset();
    // O FileReader do sandbox não preenche result; aqui ele devolve o PNG em base64 como o WebView.
    sandbox.FileReader = class { readAsDataURL() { this.result = 'data:image/png;base64,iVBORw=='; setTimeout(() => this.onload && this.onload(), 0); } };
    runInSandbox(`orders = [{ id: 'ord-3190', total: 45, items: [] }]; currentCobrancaOrderId = 'ord-3190';`);
    sandbox.__toast3190 = (m) => log.toasts.push(m);
    runInSandbox('showToast = (m) => __toast3190(m);');
    sandbox.navigator.onLine = true;
    sandbox.window.Capacitor = semPlugin ? undefined : {
      Plugins: {
        Filesystem: { async writeFile(a) { log.escrito.push(a); return { uri: 'file:///cache/' + a.path }; } },
        Share: { async share(a) { log.share.push(a); if (shareFalha) throw new Error(shareFalha); } },
      },
    };
    sandbox.fetch = async (url, init) => {
      log.fetch.push({ url: String(url), method: (init && init.method) || 'GET' });
      return { ok: status >= 200 && status < 300, status, blob: async () => new (sandbox.Blob || Blob)([PNG], { type: 'image/png' }) };
    };
    try { await fn(log); }
    finally {
      sandbox.window.Capacitor = old.cap; sandbox.fetch = old.fetch; sandbox.navigator.onLine = old.online; sandbox.FileReader = old.reader;
      sandbox.__toast3190 = old.toast; runInSandbox('showToast = __toast3190;');
      reset();
    }
  }
  const enviar = () => runInSandbox('shareCanhotoCliente()');
  const aguardar = () => new Promise((r) => setTimeout(r, 0));

  test('#3190: pedido sincronizado abre o canhoto em imagem e registra que abriu', async () => {
    await cenario({}, async (log) => {
      await enviar(); await aguardar();
      assert.ok(log.fetch[0].url.endsWith('/api/mobile/pedidos/ord-3190/canhoto-cliente'), 'pede pelo id do app');
      assert.strictEqual(log.escrito[0].directory, 'CACHE');
      assert.strictEqual(JSON.stringify(log.share[0].files), '["file:///cache/canhoto-ord-3190.png"]', 'vai como arquivo pelo plugin nativo');
      assert.ok(log.fetch.some((f) => f.method === 'POST' && f.url.endsWith('/canhoto-cliente/aberto')), 'registra a abertura');
    });
  });

  test('#3190: pedido ainda não sincronizado pede o sync e não abre nada', async () => {
    await cenario({ status: 409 }, async (log) => {
      await enviar(); await aguardar();
      assert.strictEqual(log.share.length, 0);
      assert.ok(log.toasts.some((t) => /sincroniz/i.test(t)), 'avisa para sincronizar');
      assert.ok(!log.fetch.some((f) => f.method === 'POST'), 'sem registro');
    });
  });

  test('#3190: cancelado no seletor não registra abertura', async () => {
    await cenario({ shareFalha: 'Share canceled' }, async (log) => {
      await enviar(); await aguardar();
      assert.strictEqual(log.share.length, 1);
      assert.ok(!log.fetch.some((f) => f.method === 'POST'), 'cancelou: não conta como aberto');
      assert.strictEqual(log.toasts.length, 0, 'cancelar não é erro');
    });
  });

  test('#3190: fora do APK cai no texto da cobrança, sem pedir a imagem', async () => {
    await cenario({ semPlugin: true }, async (log) => {
      await enviar(); await aguardar();
      assert.ok(!log.fetch.some((f) => f.url.includes('canhoto-cliente')), 'não baixa a imagem');
      assert.ok(log.toasts.some((t) => /texto/i.test(t)));
    });
  });
};
