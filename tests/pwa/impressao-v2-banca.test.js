'use strict';
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// #2202 (impressão v2): achados da banca revisora da F1 e da F2. Cada teste
// reproduz um achado; a lógica sem canvas roda isolada em vm.
const PWA = process.env.PWA_HTML ? path.dirname(process.env.PWA_HTML) : path.resolve(__dirname, '../../EasyStock.Api/wwwroot/pwa');
const PT_MM = 25.4 / 72;

function isolado(arquivos) {
  const ctx = {};
  ctx.globalThis = ctx;
  vm.createContext(ctx);
  for (const a of arquivos) vm.runInContext(fs.readFileSync(path.join(PWA, a), 'utf8') + (a === 'qrcode.min.js' ? ';globalThis.qrcode=qrcode;' : ''), ctx);
  return ctx;
}
function armazenamento(inicial) {
  const m = new Map(Object.entries(inicial || {}));
  return { getItem: k => (m.has(k) ? m.get(k) : null), setItem: (k, v) => m.set(k, String(v)), removeItem: k => m.delete(k), m };
}
const puro = o => JSON.parse(JSON.stringify(o));
const medir = (t, pt) => String(t).length * pt * PT_MM * 0.55;
const resolvedor = () => isolado(['qrcode.min.js', 'impressao-papeis.js', 'etiqueta-resolver.js', 'etiqueta-canvas.js']);

const CAMPOS = { empresa: 'Casa da Baba', nome: 'Talharim Fresco de Espinafre', validade: '06/11/2026', fabricacao: '03/11/2026', lote: 'LOT-261103-707082', peso: 'PESO 300g', qr: 'TALHARIM-LOT-261103-707082-001', barras: '2611037070820001' };
const ROLO_80 = { w_mm: 80, h_mm: 100, continuo: true, girar: false };
const OASIS = { name: 'OIA-8381-B', address: 'DC:0D:30:83:81:0B', profile: 'tspl' };
const MTP = { name: 'MTP-II', address: '66:22:B1:4C:58:01', profile: 'escpos' };
const fim = e => e.y_mm + e.h_mm;

module.exports = function ({ test, runInSandbox, assert }) {
  const run = body => JSON.parse(runInSandbox(`(function(){ ${body} })()`));

  // Achado 1 (Critical): "só barras" sem número curto saía sem código nenhum.
  test('#2202 banca 1: só barras sem número curto imprime o QR no lugar e avisa', () => {
    const R = resolvedor().cdbResolverEtiqueta;
    const r = R.resolver(Object.assign({}, CAMPOS, { barras: '' }), ROLO_80, { codigos: ['barras'], medir });
    assert.strictEqual(r.recusa, null);
    assert.ok(r.layout.elements.some(e => e.format === 'qr'), 'a etiqueta sai com código');
    assert.ok(!r.layout.elements.some(e => e.format === 'barcode-code128'));
    assert.match(R.resumo(r), /número curto/);
    assert.match(R.resumo(r), /QR/);
  });

  // Achado 11: chave ímpar ganhava um 0 e o leitor lia outro número.
  test('#2202 banca 11: chave com número ímpar de dígitos não vira barra com outro número', () => {
    const R = resolvedor().cdbResolverEtiqueta;
    const r = R.resolver(Object.assign({}, CAMPOS, { barras: '261103707082001' }), ROLO_80, { codigos: ['qr', 'barras'], medir });
    assert.ok(!r.layout.elements.some(e => e.format === 'barcode-code128'), 'Code128 C só leva pares de dígitos');
    assert.ok(!r.layout.elements.some(e => e.content === '0261103707082001'));
    assert.match(R.resumo(r), /ímpar/);
  });

  // Achado 2: nota longa derrubava peso, empresa e nota sem dizer nada.
  test('#2202 banca 2: nota longa quebra em 2 linhas e o que não cabe vira aviso', () => {
    const R = resolvedor().cdbResolverEtiqueta;
    const nota = 'Contém glúten e ovo. Manter refrigerado até 5 °C';
    const r = R.resolver(Object.assign({}, CAMPOS, { nota }), { w_mm: 52, h_mm: 75, continuo: true, girar: false }, { codigos: ['qr'], medir });
    const ids = r.layout.elements.map(e => e.id);
    assert.deepStrictEqual(puro(r.omitidos), []);
    assert.ok(ids.includes('peso') && ids.includes('empresa'), 'peso e empresa ficam');
    const linhas = r.layout.elements.filter(e => e.id === 'nota');
    assert.strictEqual(linhas.length, 2);
    assert.strictEqual(linhas.map(e => e.content).join(' '), nota);
    const apertada = R.resolver(Object.assign({}, CAMPOS, { nota: nota + ' ' + nota }), { w_mm: 52, h_mm: 75, continuo: true, girar: false }, { codigos: ['qr'], medir });
    assert.deepStrictEqual(puro(apertada.omitidos), ['nota'], 'só a observação sai; peso e empresa ficam');
    assert.match(R.resumo(apertada), /não coube: observação/i);
  });

  // Achado 6: o master mostrava "Validade —"; a v2 sumia com a linha.
  test('#2202 banca 6: sem validade a linha VAL fica com travessão de vazio', () => {
    const R = resolvedor().cdbResolverEtiqueta;
    const r = R.resolver(Object.assign({}, CAMPOS, { validade: '' }), ROLO_80, { codigos: ['qr'], medir });
    const val = r.layout.elements.find(e => e.id === 'val');
    assert.ok(val, 'linha da validade existe');
    assert.strictEqual(val.content, 'VAL —');
  });

  // Achado 7: no 10x15 recortado os dados desciam para o pé e abriam um vão.
  test('#2202 banca 7: 10x15 recortado põe os dados logo abaixo do nome e os códigos no pé', () => {
    const C = resolvedor();
    const R = C.cdbResolverEtiqueta, P = C.cdbPapeis;
    for (const ori of ['vertical', 'horizontal']) {
      const area = P.area(P.porId('etiqueta-100x150'), ori);
      const r = R.resolver(CAMPOS, area, { codigos: ['qr'], orientacao: ori, medir });
      const nome = r.layout.elements.filter(e => e.id === 'nome');
      const val = r.layout.elements.find(e => e.id === 'val');
      assert.ok(val.y_mm - fim(nome[nome.length - 1]) < 3, `${ori}: dados colados no nome`);
      const qr = r.qrCaixa;
      assert.ok(area.h_mm - (qr.y_mm + qr.lado_mm) < 3, `${ori}: QR no pé, a etiqueta inteira tem tinta`);
    }
  });

  // Achado 3: 50x30 em pé recusava sempre; 40x25 recusava sem dizer o que fazer.
  test('#2202 banca 3: não cabe na orientação pedida, tenta a outra; não cabe em nenhuma, diz o que fazer', () => {
    const C = resolvedor();
    const K = C.cdbEtiquetaCanvas, P = C.cdbPapeis, R = C.cdbResolverEtiqueta;
    // Produto de nome e lote curtos: em pé o QR não cabe em 30 mm; deitada cabe.
    const curto = Object.assign({}, CAMPOS, { nome: 'Talharim', lote: 'L-707082' });
    const pedida = R.resolver(curto, P.area(P.porId('etiqueta-50x30'), 'vertical'), { codigos: ['qr'], medir });
    assert.ok(pedida.recusa, 'premissa: em pé não cabe');
    const r = K.resolverNoPapel(curto, P.porId('etiqueta-50x30'), 'vertical', { codigos: ['qr'], medir });
    assert.strictEqual(r.recusa, null);
    assert.strictEqual(r.orientacao, 'horizontal');
    assert.match(R.resumo(r), /deitada/);
    const nao = K.resolverNoPapel(CAMPOS, P.porId('etiqueta-40x25'), 'vertical', { codigos: ['qr'], medir });
    assert.ok(nao.recusa);
    assert.match(nao.recusa, /papel maior/);
  });

  // Achado 4: o app gravava a v2 vazia ao abrir e o restore pulava a do backup.
  test('#2202 banca 4: v2 sem impressora não é gravada e o restore troca a v2 vazia pela do backup', () => {
    const I = isolado(['impressao-papeis.js', 'impressoras.js']).cdbImpressoras;
    const s = armazenamento();
    const v2 = I.ler(s);
    assert.deepStrictEqual(puro(Object.keys(v2.impressoras)), []);
    assert.strictEqual(s.getItem(I.CHAVE), null, 'ler sem impressora não grava');
    const r = run(`
      const antes=localStorage.getItem('cdb-impressoras.v2');
      try {
        localStorage.setItem('cdb-impressoras.v2', JSON.stringify({versao:2,impressoras:{},papeis:{etiqueta:null,cupom:null}}));
        const vazia=preferenciaAusente('cdb-impressoras.v2');
        localStorage.setItem('cdb-impressoras.v2', JSON.stringify({versao:2,impressoras:{A:{address:'A'}},papeis:{etiqueta:'A',cupom:null}}));
        const cheia=preferenciaAusente('cdb-impressoras.v2');
        localStorage.setItem('cdb-theme','dark');
        return JSON.stringify({vazia,cheia,tema:preferenciaAusente('cdb-theme')});
      } finally { antes==null?localStorage.removeItem('cdb-impressoras.v2'):localStorage.setItem('cdb-impressoras.v2',antes); localStorage.removeItem('cdb-theme'); }
    `);
    assert.deepStrictEqual(r, { vazia: true, cheia: false, tema: false });
  });

  // Achado 5: na janela de 300 ms do dreno, um toque novo passava na frente
  // (e o da fila se perdia); toque duplo enfileirava dois cupons.
  const fila = corpo => run(`
    const old={bs:window.bluetoothSerial,permission:ensureBluetoothPermission,timeout:setTimeout,clear:clearTimeout,toast:showToast};
    const timers=[],conexoes=[],writes=[],toasts=[];
    try {
      releasePrinting();printViaBluetooth.fila=[];printViaBluetooth.ativa=null;printViaBluetooth.drenando=false;
      ensureBluetoothPermission=cb=>cb(true);showToast=t=>toasts.push(t);
      setTimeout=(fn,ms)=>{timers.push({fn,ms});return timers.length;};clearTimeout=id=>{if(timers[id-1])timers[id-1].fn=null;};
      window.bluetoothSerial={disconnect:ok=>ok(),connect:(addr,ok)=>{conexoes.push(addr);ok();},write:(bytes,ok)=>writes.push({bytes,ok})};
      const dreno=()=>timers.filter(t=>t.ms===300&&t.fn).forEach(t=>{const f=t.fn;t.fn=null;f();});
      const A={name:'Oasis',address:'A',profile:'tspl'},B={name:'MTP-II',address:'B',profile:'escpos'};
      ${corpo}
    } finally {
      releasePrinting();printViaBluetooth.fila=[];printViaBluetooth.ativa=null;printViaBluetooth.drenando=false;window.bluetoothSerial=old.bs;
      ensureBluetoothPermission=old.permission;setTimeout=old.timeout;clearTimeout=old.clear;showToast=old.toast;
    }
  `);
  test('#2202 banca 5: toque durante o dreno da fila espera a vez e o da fila não se perde', () => {
    const r = fila(`
      printViaBluetooth([1],null,null,A);
      printViaBluetooth([2],null,null,B);
      writes[0].ok();
      printViaBluetooth([3],null,null,A);
      dreno(); writes[1].ok(); dreno(); writes[2].ok();
      return JSON.stringify({conexoes,escritos:writes.map(w=>w.bytes[0]),fila:printViaBluetooth.fila.length});
    `);
    assert.deepStrictEqual(r, { conexoes: ['A', 'B', 'A'], escritos: [1, 2, 3], fila: 0 });
  });
  test('#2202 banca 5: toque duplo para a impressora da fila não enfileira dois trabalhos', () => {
    const r = fila(`
      printViaBluetooth([1],null,null,A);
      printViaBluetooth([2],null,null,B);
      printViaBluetooth([2],null,null,B);
      return JSON.stringify({fila:printViaBluetooth.fila.length,aviso:toasts.some(t=>/andamento/.test(t))});
    `);
    assert.deepStrictEqual(r, { fila: 1, aviso: true });
  });

  // Achado 10: o erro útil do Bluetooth era trocado por "Sem conexão BT".
  test('#2202 banca 10: falha que já foi explicada não troca a mensagem pelo "Sem conexão BT"', () => {
    const r = fila(`
      window.bluetoothSerial.connect=(addr,ok,erro)=>erro('Dispositivo desligado');
      const recebido=[];const oldErr=showError;showError=(e)=>toasts.push('erro:'+e);
      try {
        printViaBluetooth([1],explicado=>recebido.push(explicado),null,A);
        window.bluetoothSerial=null;
        printViaBluetooth([1],explicado=>recebido.push(explicado),null,A);
      } finally { showError=oldErr; }
      return JSON.stringify({recebido,erro:toasts.some(t=>/Dispositivo desligado/.test(t))});
    `);
    assert.deepStrictEqual(r, { recebido: [true, false], erro: true });
  });

  // Achado 9: o cartão dizia "nada ainda" e o status dizia "Cupom: MTP-II".
  test('#2202 banca 9: o cartão mostra o que o app manda para a impressora, sem repetir o nome', () => {
    const r = run(`
      const chaves=['cdb-printer','cdb-printers','cdb-impressoras.v2'];const antes={};chaves.forEach(k=>antes[k]=localStorage.getItem(k));
      const cartoes=[];const host={innerHTML:'',appendChild:c=>cartoes.push(c)};const origGet=document.getElementById;
      document.getElementById=id=>id==='impressorasLista'?host:origGet.call(document,id);
      try {
        chaves.forEach(k=>localStorage.removeItem(k));
        const I=window.cdbImpressoras;
        let v2=I.ler(localStorage);
        v2=I.cadastrar(v2,I.deLegado(${JSON.stringify(OASIS)}));
        v2=I.cadastrar(v2,I.deLegado(${JSON.stringify(MTP)}));
        I.definirPapel(v2,'etiqueta',${JSON.stringify(OASIS.address)});
        renderImpressorasLista(v2);
        return JSON.stringify({cartoes:cartoes.map(c=>c.innerHTML)});
      } finally { document.getElementById=origGet; chaves.forEach(k=>antes[k]==null?localStorage.removeItem(k):localStorage.setItem(k,antes[k])); }
    `);
    assert.strictEqual(r.cartoes.length, 2);
    assert.match(r.cartoes[0], /Imprime: etiqueta</);
    assert.match(r.cartoes[1], /Imprime: cupom</, 'a única de bobina recebe o cupom, como na linha de status');
    assert.ok(!/MTP-II<\/b> <span[^>]*>MTP-II/.test(r.cartoes[1]), 'apelido vazio: o nome aparece uma vez');
  });

  // Achado 12: getSavedPrinter lançava com impressao-papeis.js fora do cache.
  test('#2202 banca 12: sem o catálogo de papéis a impressora salva ainda é lida', () => {
    const r = run(`
      const papeis=window.cdbPapeis;const antes=localStorage.getItem('cdb-printer');
      try {
        localStorage.setItem('cdb-printer', ${JSON.stringify(JSON.stringify(OASIS))});
        window.cdbPapeis=undefined;
        return JSON.stringify({address:getSavedPrinter().address});
      } finally { window.cdbPapeis=papeis; antes==null?localStorage.removeItem('cdb-printer'):localStorage.setItem('cdb-printer',antes); }
    `);
    assert.deepStrictEqual(r, { address: OASIS.address });
  });

  // Achado 8: papel presumido (midiaConfirmada false) só aparecia em Ajustes.
  test('#2202 banca 8: papel não confirmado vira aviso depois do envio', () => {
    const r = run(`
      return JSON.stringify({
        presumido: avisoDaImpressao({name:'OIA-8381-B',address:'A',profile:'tspl',midiaConfirmada:false,midia:{tipo:'continuo',larguraMm:80}}, ''),
        confirmado: avisoDaImpressao({name:'OIA-8381-B',address:'A',profile:'tspl',midiaConfirmada:true,midia:{tipo:'continuo',larguraMm:80}}, ''),
        junto: avisoDaImpressao({name:'OIA-8381-B',address:'A',profile:'tspl',midiaConfirmada:true}, 'Saiu deitada.')
      });
    `);
    assert.match(r.presumido, /Confira o papel em Ajustes/);
    assert.match(r.presumido, /80 mm/);
    assert.strictEqual(r.confirmado, '');
    assert.strictEqual(r.junto, 'Saiu deitada.');
  });

  // Rodada 2. Achado 1: o QR nascia grande e a 52 x 30 recusava nas duas orientações.
  test('#2202 banca r2 1: QR encolhe até o mínimo legível antes de recusar a etiqueta pequena', () => {
    const C = resolvedor();
    const K = C.cdbEtiquetaCanvas, P = C.cdbPapeis;
    const r = K.resolverNoPapel(CAMPOS, P.porId('etiqueta-52x30'), 'horizontal', { codigos: ['qr'], medir });
    assert.strictEqual(r.recusa, null);
    const qr = r.layout.elements.find(e => e.format === 'qr');
    assert.ok(qr, 'sai com QR');
    assert.ok(qr._mod >= 3 * 0.125 - 1e-9, 'módulo do QR no mínimo de 3 pontos');
    assert.ok(r.layout.elements.some(e => e.id === 'peso'), 'o peso não sai para o QR ficar grande');
    // Quem já cabia não muda: no rolo de 80 mm o QR segue no módulo máximo.
    const rolo = C.cdbResolverEtiqueta.resolver(CAMPOS, ROLO_80, { codigos: ['qr'], medir });
    assert.strictEqual(rolo.layout.elements.find(e => e.format === 'qr')._mod, 4 * 0.125);
    assert.ok(!('_qrMod' in rolo));
  });

  // Achado 4: sem lote a linha sumia; o master sempre imprimiu "LOTE:".
  test('#2202 banca r2 4: sem lote a linha LOTE fica com travessão de vazio', () => {
    const R = resolvedor().cdbResolverEtiqueta;
    const r = R.resolver(Object.assign({}, CAMPOS, { lote: '' }), ROLO_80, { codigos: ['qr'], medir });
    assert.ok(r.layout.elements.some(e => e.id === 'lote' && e.content === 'LOTE —'));
  });

  // Achados 3 e 6: espaço em volta da chave derrubava a barra; nota só de espaços ocupava altura.
  test('#2202 banca r2 3: espaço em volta da chave e nota em branco não mudam a etiqueta', () => {
    const R = resolvedor().cdbResolverEtiqueta;
    const r = R.resolver(Object.assign({}, CAMPOS, { barras: ' 2611037070820001\n', nota: '   ' }), ROLO_80, { codigos: ['qr', 'barras'], medir });
    const barra = r.layout.elements.find(e => e.format === 'barcode-code128');
    assert.ok(barra, 'a barra sai');
    assert.strictEqual(barra.content, '2611037070820001');
    assert.ok(!r.layout.elements.some(e => e.id === 'nota'));
    assert.deepStrictEqual(puro(r.avisos), []);
  });

  // Achado fluxo 1: o teste de impressão trocava o erro explicado pela impressão do sistema.
  test('#2202 banca r2 fluxo 1: teste de impressão com erro explicado não abre a impressão do sistema', () => {
    const r = fila(`
      window.bluetoothSerial.connect=(addr,ok,erro)=>erro('Dispositivo desligado');
      const oldErr=showError,oldArea=printAreaHtml,oldSaved=getSavedPrinter;const area=[];
      showError=e=>toasts.push('erro:'+e);printAreaHtml=h=>area.push(h);getSavedPrinter=()=>B;
      try { testPrint(); } finally { showError=oldErr;printAreaHtml=oldArea;getSavedPrinter=oldSaved; }
      return JSON.stringify({erro:toasts.some(t=>/Dispositivo desligado/.test(t)),sistema:area.length,troca:toasts.some(t=>/indisponível/.test(t))});
    `);
    assert.deepStrictEqual(r, { erro: true, sistema: 0, troca: false });
  });
  // Achado impressão 1: no rolo de 80 mm deitado com barras, o QR subia junto com
  // as barras e ficava a 0,6 mm do topo, sem a zona quieta de 4 módulos.
  test('#2202 banca r2 impressão 1: no rolo deitado com barras o QR mantém a zona quieta no topo', () => {
    const C = resolvedor();
    const r = C.cdbEtiquetaCanvas.resolverNoPapel(CAMPOS, C.cdbPapeis.porId('continuo-80'), 'horizontal', { codigos: ['qr', 'barras'], medir });
    assert.strictEqual(r.recusa, null);
    const qr = r.layout.elements.find(e => e.format === 'qr');
    const barra = r.layout.elements.find(e => e.format === 'barcode-code128');
    assert.ok(qr && barra, 'sai com QR e barra');
    // A caixa do QR leva 1 módulo de zona quieta; o símbolo começa depois dele.
    assert.ok(qr.y_mm + qr._mod >= 4 * qr._mod - 1e-9, `símbolo a ${(qr.y_mm + qr._mod).toFixed(2)} mm do topo`);
    assert.ok(barra.y_mm >= 0 && fim(barra) <= r.layout.size.h_mm + 1e-9, 'a barra fica dentro da etiqueta');
  });
};
