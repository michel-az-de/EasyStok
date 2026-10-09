// Testes — #1474 W5/W6: textos que o operador comum ve no PWA
//
// W5: pareamento mostrava "Pareado com loja 3fa85f64…" (GUID); toasts
// mandavam "Copiar trace"; o Diagnostico mostrava "Device" na secao App;
// "Peso obrigatorio (RDC 727)" nao dizia o que fazer; Ajustes tinha o bloco
// "White label… parecer produto, nao improviso"; mensagens sem acento.
// W6: codigo do lote no subtitulo do modal de producao vinha da abertura do
// modal; atravessando a meia-noite diferia do lote gravado e da etiqueta.
'use strict';

const fs = require('fs');
const path = require('path');

module.exports = function ({ test, sandbox, runInSandbox, assert }) {
  const HTML = fs.readFileSync(path.resolve(__dirname, '../../EasyStock.Api/wwwroot/pwa/index.html'), 'utf8');

  // Captura showToast/alert durante fn e devolve as mensagens.
  async function capturando(fn) {
    const msgs = [];
    const toastOriginal = sandbox.showToast;
    const alertOriginal = sandbox.alert;
    sandbox.showToast = (m) => { msgs.push(String(m)); };
    sandbox.alert = (m) => { msgs.push(String(m)); };
    try { await fn(); } finally {
      sandbox.showToast = toastOriginal;
      sandbox.alert = alertOriginal;
    }
    return msgs;
  }

  async function pareiaCom(resposta) {
    const syncOriginal = sandbox.cdbSync;
    const promptOriginal = sandbox.prompt;
    const diagOriginal = sandbox.openDiagnostics;
    sandbox.prompt = () => '123456';
    sandbox.openDiagnostics = () => {};
    sandbox.cdbSync = Object.assign({}, syncOriginal, { pairWithCode: async () => resposta });
    try {
      return await capturando(() => runInSandbox('pairDevicePrompt()'));
    } finally {
      sandbox.cdbSync = syncOriginal;
      sandbox.prompt = promptOriginal;
      sandbox.openDiagnostics = diagOriginal;
    }
  }

  test('#1474 W5: pareamento mostra o nome da loja, nao o GUID', async () => {
    const msgs = await pareiaCom({ lojaId: '3fa85f64-5717-4562-b3fc-2c963f66afa6',
                                   lojaNome: 'Casa da Baba | Centro', empresaNome: 'Casa da Baba' });
    const final = msgs[msgs.length - 1];
    assert.strictEqual(final, 'Pareado com Casa da Baba | Centro ✓');
  });

  test('#1474 W5: pareamento sem nome de loja cai no nome da empresa e nunca no GUID', async () => {
    const msgs = await pareiaCom({ lojaId: '3fa85f64-5717-4562-b3fc-2c963f66afa6', empresaNome: 'Casa da Baba' });
    assert.strictEqual(msgs[msgs.length - 1], 'Pareado com Casa da Baba ✓');
    const semNada = await pareiaCom({ lojaId: '3fa85f64-5717-4562-b3fc-2c963f66afa6' });
    assert.ok(!/3fa85f64/.test(semNada.join(' ')), 'sem GUID: ' + semNada.join(' | '));
  });

  test('#1474 W5: falha de pareamento nao manda o operador "Copiar trace"', async () => {
    const syncOriginal = sandbox.cdbSync;
    sandbox.cdbSync = Object.assign({}, syncOriginal, { forcePairNow: async () => null });
    let msgs;
    try {
      msgs = await capturando(() => runInSandbox('diagForcePairNow()'));
      sandbox.cdbSync = Object.assign({}, syncOriginal, { forcePairNow: async () => { throw new Error('HTTP 500'); } });
      msgs = msgs.concat(await capturando(() => runInSandbox('diagForcePairNow()')));
    } finally { sandbox.cdbSync = syncOriginal; }
    assert.ok(msgs.length >= 2);
    assert.ok(!msgs.some(m => /trace/i.test(m)), 'sem "trace" no toast: ' + msgs.join(' | '));
  });

  test('#1474 W5: "Device" e "Copiar trace" so no Diagnostico avancado', () => {
    const fonte = runInSandbox('openDiagnostics.toString()');
    const corte = fonte.indexOf('Diagnóstico avançado');
    assert.ok(corte > 0, 'existe a secao Diagnostico avancado');
    const antes = fonte.slice(0, corte);
    assert.ok(!/_diagRow\('Device'/.test(antes), 'linha Device fora da secao avancada');
    assert.ok(!/Copiar trace/.test(antes), 'botao Copiar trace fora da secao avancada');
    assert.ok(/<details/.test(fonte.slice(corte - 200, corte)), 'secao avancada fica recolhida');
  });

  test('#1474 W5: alerta de aparelho nao pareado fala "aparelho", nao "Device"', async () => {
    const syncOriginal = sandbox.cdbSync;
    sandbox.cdbSync = Object.assign({}, syncOriginal, { pushAllAndFlush: async () => ({}), getPairing: () => null });
    let msgs;
    try { msgs = await capturando(() => runInSandbox('diagPushAll()')); }
    finally { sandbox.cdbSync = syncOriginal; }
    assert.ok(msgs.length === 1 && !/Device/.test(msgs[0]) && /aparelho/.test(msgs[0]), msgs.join(' | '));
  });

  test('#1474 W5: peso faltando diz o que fazer e marca aria-invalid no campo', async () => {
    const atributos = {};
    const input = {
      value: '', classList: { add() {}, remove() {} }, focus() {}, scrollIntoView() {},
      setAttribute(k, v) { atributos[k] = v; }, removeAttribute(k) { delete atributos[k]; }
    };
    const pilula = { dataset: { d: '5' } };
    const row = {
      dataset: { pid: 'nhoque' }, classList: { add() {}, remove() {} },
      querySelector: (sel) => (/pd-weight/.test(sel) ? input : /button\.active/.test(sel) ? pilula : null)
    };
    const qsaOriginal = sandbox.document.querySelectorAll;
    sandbox.document.querySelectorAll = (sel) => (/pd-row/.test(sel) ? [row] : []);
    let msgs;
    try {
      msgs = await capturando(() => runInSandbox(`(function(){
        products = [{ id: 'nhoque', name: 'Nhoque', emoji: '', category: 'massa', unit: 'un',
                      price: 12, stock: 0, count: 1, tipoEmbalagem: 'Embalado' }];
        batches = [];
        _isSavingProduction = false;
        confirmProduction();
      })()`));
    } finally { sandbox.document.querySelectorAll = qsaOriginal; }
    assert.ok(msgs.includes('Falta o peso de: Nhoque (vai na etiqueta)'), msgs.join(' | '));
    assert.strictEqual(atributos['aria-invalid'], 'true');
  });

  test('#1474 W5: Ajustes sem o bloco "White label"', () => {
    assert.ok(!/White label/.test(HTML), 'eyebrow White label');
    assert.ok(!/parecer produto, não improviso/.test(HTML), 'copy do bloco');
  });

  test('#1474 W5: mensagens visiveis ao operador com acento', () => {
    const SEM_ACENTO = /\b(nao|Nao|indisponivel|possivel|espaco|versao|automatico|invalido|conexao|avancar|estao|Voce|ultima|catalogo|ja|ate|so)\b/;
    const re = /\b(showToast|alert|confirm)\((['`"])((?:(?!\2).)*)\2/g;
    const ruins = [];
    let m;
    while ((m = re.exec(HTML)) !== null) {
      if (SEM_ACENTO.test(m[3])) ruins.push(m[3]);
    }
    assert.deepStrictEqual(ruins, [], ruins.length + ' mensagem(ns) sem acento');
    assert.ok(!/Sync indispon/.test(HTML), '"Sync" e jargao: "Sincronização indisponível"');
  });

  // ── W6 ────────────────────────────────────────────────────────────────
  test('#1474 W6: modal aberto antes da meia-noite e confirmado depois usa o lote do confirmar', async () => {
    const RealDate = sandbox.Date;
    let agora = new RealDate(2026, 9, 8, 23, 59, 50).getTime();
    class FakeDate extends RealDate {
      constructor(...a) { if (a.length === 0) super(agora); else super(...a); }
      static now() { return agora; }
    }
    const subtitulo = { textContent: '' };
    const getOriginal = sandbox.document.getElementById;
    sandbox.Date = FakeDate;
    sandbox.document.getElementById = (id) => (id === 'batchDate' ? subtitulo : getOriginal(id));
    let r;
    try {
      runInSandbox(`products = [{ id: 'talharim', name: 'Talharim', emoji: '', category: 'massa', unit: 'un',
                    price: 12, stock: 0, count: 1, tipoEmbalagem: 'Avulso' }]; batches = []; _isSavingProduction = false;
                    openProductionConfirm();`);
      const naAbertura = subtitulo.textContent;
      agora = new RealDate(2026, 9, 9, 0, 0, 10).getTime();
      await capturando(() => runInSandbox('confirmProduction()'));
      r = JSON.parse(runInSandbox(`JSON.stringify({ codigo: codigoDoLote(batches[batches.length - 1]),
                                                    lote: batches[batches.length - 1].lote })`));
      r.naAbertura = naAbertura;
    } finally {
      sandbox.Date = RealDate;
      sandbox.document.getElementById = getOriginal;
    }
    assert.ok(/LOT-261008-/.test(r.naAbertura), 'abertura mostrou o lote do dia 8: ' + r.naAbertura);
    assert.strictEqual(r.lote, 'LOT-261009', 'lote gravado e o do confirmar');
    assert.ok(subtitulo.textContent.includes(r.codigo),
      'subtitulo mostra o mesmo codigo da etiqueta: ' + subtitulo.textContent + ' vs ' + r.codigo);
  });
};
