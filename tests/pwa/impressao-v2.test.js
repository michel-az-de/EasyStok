'use strict';
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// #2202 (impressão v2): várias impressoras (F1) e o resolvedor da etiqueta
// por tamanho (F2). O desenho em canvas e a leitura dos códigos ficam no
// simulador (tools/sim-impressora); aqui vai a lógica que não precisa de canvas.
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

// Medida de texto previsível: 0,55 da altura do corpo por caractere.
// Objetos do contexto vm têm outro protótipo: compara pelo JSON.
const puro = o => JSON.parse(JSON.stringify(o));

const medir = (t, pt) => String(t).length * pt * PT_MM * 0.55;

const OASIS = { name: 'OIA-8381-B', address: 'DC:0D:30:83:81:0B', profile: 'tspl' };
const MTP = { name: 'MTP-II', address: '66:22:B1:4C:58:01', profile: 'escpos' };

module.exports = function ({ test, runInSandbox, assert }) {
  const run = body => JSON.parse(runInSandbox(`(function(){ ${body} })()`));

  test('#2202 F1 migração: chaves antigas viram v2, a selecionada serve aos dois papéis, e repetir não muda nada', () => {
    const ctx = isolado(['impressao-papeis.js', 'impressoras.js']);
    const I = ctx.cdbImpressoras;
    const s = armazenamento({ 'cdb-printer': JSON.stringify(OASIS), 'cdb-printers': JSON.stringify([MTP, OASIS]) });
    const v2 = I.ler(s);
    assert.deepStrictEqual(puro(Object.keys(v2.impressoras).sort()), [MTP.address, OASIS.address].sort());
    assert.deepStrictEqual(puro(v2.papeis), { etiqueta: OASIS.address, cupom: OASIS.address });
    const oasis = v2.impressoras[OASIS.address];
    assert.strictEqual(oasis.linguagem, 'tspl');
    assert.strictEqual(oasis.midia.preset, 'continuo-80', 'D1: sem mídia salva, rolo contínuo');
    assert.strictEqual(oasis.midiaConfirmada, false, 'mídia presumida pede confirmação em Ajustes');
    assert.strictEqual(v2.impressoras[MTP.address].midia.preset, 'bobina-58');
    const gravado = s.getItem('cdb-impressoras.v2');
    I.ler(s);
    assert.strictEqual(s.getItem('cdb-impressoras.v2'), gravado, 'segunda leitura é idempotente');
    assert.strictEqual(s.getItem('cdb-printer'), JSON.stringify(OASIS), 'migração não reescreve as chaves antigas');
  });

  test('#2202 F1 rollback: mudança feita nas chaves antigas volta para o v2', () => {
    const ctx = isolado(['impressao-papeis.js', 'impressoras.js']);
    const I = ctx.cdbImpressoras;
    const s = armazenamento({ 'cdb-printer': JSON.stringify(OASIS), 'cdb-printers': JSON.stringify([OASIS]) });
    let v2 = I.ler(s);
    v2 = I.cadastrar(v2, Object.assign(I.deLegado(MTP), { midia: { preset: 'bobina-58', tipo: 'bobina', larguraMm: 58 } }));
    I.definirPapel(v2, 'cupom', MTP.address);
    I.salvar(s, v2);
    assert.strictEqual(JSON.parse(s.getItem('cdb-printers')).length, 2, 'salvar espelha a lista antiga');
    // App anterior seleciona a MTP-II direto em cdb-printer.
    s.setItem('cdb-printer', JSON.stringify(MTP));
    v2 = I.ler(s);
    assert.deepStrictEqual(puro(v2.papeis), { etiqueta: MTP.address, cupom: MTP.address });
    assert.strictEqual(v2.impressoras[OASIS.address].linguagem, 'tspl', 'a outra impressora continua cadastrada');
  });

  test('#2202 F1 regra: códigos pela regra, senão pelo modo de leitura, senão só o QR (D4)', () => {
    const ctx = isolado(['impressao-papeis.js', 'impressoras.js']);
    const I = ctx.cdbImpressoras;
    assert.deepStrictEqual(puro(I.regra(armazenamento(), 'etiqueta-producao').codigos), ['qr']);
    assert.deepStrictEqual(puro(I.regra(armazenamento({ 'cdb-prod-scan-mode': 'both' }), 'etiqueta-producao').codigos), ['qr', 'barras']);
    const r = I.regra(armazenamento({
      'cdb-prod-scan-mode': 'both',
      'cdb-regra-impressao.v1': JSON.stringify({ v: 1, documentos: { 'etiqueta-producao': { codigos: ['barras'], orientacao: 'horizontal', copias: 2 } } })
    }), 'etiqueta-producao');
    assert.deepStrictEqual(puro({ codigos: r.codigos, orientacao: r.orientacao, copias: r.copias, papel: r.papel }), { codigos: ['barras'], orientacao: 'horizontal', copias: 2, papel: 'etiqueta' });
  });

  test('#2202 F1 app: produção vai para a impressora de etiqueta e pedido para a de cupom', () => {
    const r = run(`
      const chaves=['cdb-printer','cdb-printers','cdb-impressoras.v2'];const antes={};chaves.forEach(k=>antes[k]=localStorage.getItem(k));
      try {
        chaves.forEach(k=>localStorage.removeItem(k));
        localStorage.setItem('cdb-printer', ${JSON.stringify(JSON.stringify(OASIS))});
        localStorage.setItem('cdb-printers', ${JSON.stringify(JSON.stringify([MTP, OASIS]))});
        const I=window.cdbImpressoras;const v2=impressorasV2();I.definirPapel(v2,'cupom',${JSON.stringify(MTP.address)});I.salvar(localStorage,v2);
        return JSON.stringify({prod:impressoraPara('etiqueta-producao').address,pedido:impressoraPara('etiqueta-pedido').address,
          salva:getSavedPrinter().address,perfis:getPrinterProfiles().length,midia:impressoraPara('etiqueta-producao').midia.preset});
      } finally { chaves.forEach(k=>antes[k]==null?localStorage.removeItem(k):localStorage.setItem(k,antes[k])); }
    `);
    assert.deepStrictEqual(r, { prod: OASIS.address, pedido: MTP.address, salva: OASIS.address, perfis: 2, midia: 'continuo-80' });
  });

  test('#2202 F1 fila: trabalho de outra impressora espera, troca a conexão e o da mesma é descartado', () => {
    const r = run(`
      const old={bs:window.bluetoothSerial,permission:ensureBluetoothPermission,timeout:setTimeout,clear:clearTimeout,toast:showToast};
      const timers=[],conexoes=[],writes=[],toasts=[];let enviados=0;
      try {
        releasePrinting();printViaBluetooth.fila=[];printViaBluetooth.ativa=null;
        ensureBluetoothPermission=cb=>cb(true);showToast=t=>toasts.push(t);
        setTimeout=(fn,ms)=>{timers.push({fn,ms});return timers.length;};clearTimeout=id=>{if(timers[id-1])timers[id-1].fn=null;};
        window.bluetoothSerial={disconnect:ok=>ok(),connect:(addr,ok)=>{conexoes.push(addr);ok();},write:(bytes,ok)=>writes.push({bytes,ok})};
        const A={name:'Oasis',address:'A',profile:'tspl'},B={name:'MTP-II',address:'B',profile:'escpos'};
        printViaBluetooth([1],null,null,A,()=>enviados++);
        printViaBluetooth([2],null,null,B,()=>enviados++);
        printViaBluetooth([3],null,null,A);
        const naFila=printViaBluetooth.fila.length;
        writes[0].ok();
        timers.filter(t=>t.ms===300&&t.fn).forEach(t=>{const f=t.fn;t.fn=null;f();});
        writes[1].ok();
        return JSON.stringify({naFila,conexoes,escritos:writes.map(w=>w.bytes[0]),enviados,
          fila:printViaBluetooth.fila.length,ocupado:isPrinting,avisoFila:toasts.some(t=>/Na fila/.test(t)),descarte:toasts.some(t=>/andamento/.test(t))});
      } finally {
        releasePrinting();printViaBluetooth.fila=[];printViaBluetooth.ativa=null;window.bluetoothSerial=old.bs;
        ensureBluetoothPermission=old.permission;setTimeout=old.timeout;clearTimeout=old.clear;showToast=old.toast;
      }
    `);
    assert.deepStrictEqual(r, { naFila: 1, conexoes: ['A', 'B'], escritos: [1, 2], enviados: 2, fila: 0, ocupado: false, avisoFila: true, descarte: true });
  });

  test('#2202 F1 fila: no máximo 4 trabalhos esperando', () => {
    const r = run(`
      const old={bs:window.bluetoothSerial,permission:ensureBluetoothPermission,timeout:setTimeout,clear:clearTimeout};
      try {
        releasePrinting();printViaBluetooth.fila=[];printViaBluetooth.ativa=null;
        ensureBluetoothPermission=cb=>cb(true);setTimeout=()=>0;clearTimeout=()=>{};
        window.bluetoothSerial={disconnect:ok=>ok(),connect:(addr,ok)=>ok(),write:()=>{}};
        printViaBluetooth([1],null,null,{name:'A',address:'A'});
        for(let i=0;i<6;i++) printViaBluetooth([2+i],null,null,{name:'B',address:'B'});
        return JSON.stringify({fila:printViaBluetooth.fila.length});
      } finally {
        releasePrinting();printViaBluetooth.fila=[];printViaBluetooth.ativa=null;window.bluetoothSerial=old.bs;
        ensureBluetoothPermission=old.permission;setTimeout=old.timeout;clearTimeout=old.clear;
      }
    `);
    assert.deepStrictEqual(r, { fila: 4 });
  });

  test('#2202 D4: a etiqueta leva a chave curta do lote só quando o lote tem número curto', () => {
    const r = run(`
      const old=products;
      try {
        products=[{id:'p1',name:'Talharim',sku:'TALHARIM'}];
        const base={id:'b1',lote:'261103',createdAt:'2026-11-03T12:00:00-03:00',items:[{productId:'p1',name:'Talharim',qty:2}]};
        const com=etiquetasFromBatch(Object.assign({numeroCurto:'2611-0370708-20'},base));
        const sem=etiquetasFromBatch(base);
        return JSON.stringify({com:com.map(e=>e.chaveCurta),sem:sem.map(e=>e.chaveCurta)});
      } finally { products=old; }
    `);
    assert.deepStrictEqual(r, { com: ['2611037070820001', '2611037070820002'], sem: [null, null] });
  });

  test('#2202 F2 resolvedor: rolo de 80 mm cabe no tamanho final, sem letra nem QR abaixo do piso', () => {
    const ctx = isolado(['qrcode.min.js', 'impressao-papeis.js', 'etiqueta-resolver.js']);
    const R = ctx.cdbResolverEtiqueta;
    const area = { w_mm: 80, h_mm: 112, continuo: true, girar: false };
    const campos = { empresa: 'Casa da Baba', nome: 'Talharim Fresco de Espinafre', validade: '06/11/2026', fabricacao: '03/11/2026', lote: 'LOT-261103-707082', peso: 'PESO 300g', qr: 'TALHARIM-LOT-261103-707082-001', barras: '2611037070820001' };
    const r = R.resolver(campos, area, { codigos: ['qr', 'barras'], medir });
    assert.strictEqual(r.recusa, null);
    assert.strictEqual(r.layout.size.w_mm, 80);
    assert.ok(r.layout.size.h_mm < 112, 'contínuo corta a altura no conteúdo');
    const textos = r.layout.elements.filter(e => e.type === 'text' && e.id !== 'empresa' && e.id !== 'hri');
    assert.ok(textos.every(e => e.size_pt >= R.MIN.dadoPt), 'nenhum dado abaixo de 7,5 pt');
    assert.ok(textos.filter(e => e.id === 'nome').every(e => e.size_pt >= R.MIN.nomePt));
    const qr = r.layout.elements.find(e => e.format === 'qr');
    assert.ok(qr._mod >= R.MIN.qrModulo * R.PONTO, 'módulo do QR com 3 pontos ou mais');
    const barras = r.layout.elements.find(e => e.format === 'barcode-code128');
    assert.strictEqual(barras.content, '2611037070820001');
    assert.ok(barras.quiet_zone_mm >= 10 * barras._mod - 1e-9 && barras._mod >= R.MIN.barraModulo * R.PONTO);
    for (const e of r.layout.elements) assert.ok(e.x_mm >= 0 && e.x_mm + e.w_mm <= 80 + 1e-9, `${e.id} dentro da largura`);
  });

  test('#2202 F2 resolvedor: papel pequeno demais recusa com o motivo, sem encolher', () => {
    const ctx = isolado(['qrcode.min.js', 'impressao-papeis.js', 'etiqueta-resolver.js']);
    const R = ctx.cdbResolverEtiqueta;
    const campos = { nome: 'Talharim Fresco de Espinafre', validade: '06/11/2026', fabricacao: '03/11/2026', lote: 'LOT-261103-707082', qr: 'TALHARIM-LOT-261103-707082-001' };
    const r = R.resolver(campos, { w_mm: 30, h_mm: 20, continuo: false, girar: false }, { codigos: ['qr'], medir });
    assert.strictEqual(r.layout, null);
    assert.match(r.recusa, /não cabe/);
  });

  test('#2202 F2 resolvedor: barra sem número curto sai do layout com aviso (D4)', () => {
    const ctx = isolado(['qrcode.min.js', 'impressao-papeis.js', 'etiqueta-resolver.js']);
    const R = ctx.cdbResolverEtiqueta;
    const campos = { nome: 'Talharim', validade: '06/11/2026', fabricacao: '03/11/2026', lote: 'LOT-1', qr: 'TALHARIM-LOT-1-001', barras: '' };
    const r = R.resolver(campos, { w_mm: 80, h_mm: 112, continuo: true, girar: false }, { codigos: ['qr', 'barras'], medir });
    assert.strictEqual(r.recusa, null);
    assert.ok(!r.layout.elements.some(e => e.format === 'barcode-code128'));
    assert.ok(r.avisos.some(a => /número curto/.test(a)));
  });

  test('#2202 F2 canvas: Code128 C com início 105, pares de dígitos, verificador mod 103 e parada', () => {
    const ctx = isolado(['impressao-papeis.js', 'etiqueta-resolver.js', 'etiqueta-canvas.js']);
    const tabela = Array.from({ length: 107 }, (_, i) => `[${i}]`);
    // 105 + 12*1 + 34*2 = 185; 185 mod 103 = 82.
    assert.strictEqual(ctx.cdbEtiquetaCanvas.code128C('1234', tabela), '[105][12][34][82][106]');
  });

  test('#2202 F2 canvas: bobina sem comprimento ganha teto proporcional e sai contínua', () => {
    const ctx = isolado(['impressao-papeis.js', 'etiqueta-resolver.js', 'etiqueta-canvas.js']);
    const C = ctx.cdbEtiquetaCanvas, P = ctx.cdbPapeis;
    const a = C.areaDoPapel(P.porId('bobina-80'), 'vertical');
    assert.deepStrictEqual(puro({ w: a.w_mm, h: a.h_mm, continuo: a.continuo, girar: a.girar }), { w: 72, h: 101, continuo: true, girar: false });
  });
};
