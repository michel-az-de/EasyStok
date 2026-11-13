'use strict';

// #2202 (impressão v2, F0): a configuração da Oasis chega. O seletor mostra o
// papel salvo, o papel vem do catálogo único (impressao-papeis.js), rolo
// contínuo é o padrão (D1) e a etiqueta recortada tem calibração opcional e
// teste de alinhamento. Desde a F1 o seletor tem 3 passos: impressora, o que
// ela imprime e o papel.
const OASIS = { name: 'OIA-8381-B', address: 'DC:0D:30:83:81:0B', profile: 'tspl' };
const MTP = { name: 'MTP-II', address: '66:22:B1:4C:58:01', profile: 'escpos' };

module.exports = function ({ test, runInSandbox, assert }) {
  // Roda com DOM que guarda valor por id e chaves de impressora isoladas.
  const run = body => JSON.parse(runInSandbox(`(function(){
    const chaves=['cdb-printer','cdb-printers','cdb-impressoras.v2'];const antes={};chaves.forEach(k=>antes[k]=localStorage.getItem(k));
    const els={};const origGet=document.getElementById;const origPrint=printViaBluetooth;const origToast=showToast;const origLoad=loadPrinterDevices;
    document.getElementById=id=>els[id]||(els[id]=Object.assign(origGet.call(document,id),{checked:false}));
    const enviados=[];printViaBluetooth=(bytes,fb,job,imp,onSent)=>{enviados.push(String.fromCharCode.apply(null,Array.from(bytes)));if(onSent)onSent();};
    const toasts=[];showToast=t=>toasts.push(t);loadPrinterDevices=()=>{};
    const abrir=(p,uso)=>{openPrinterPicker();escolherNoSeletor(p.address,p.name);escolherUso(uso);
      const sel=document.getElementById('printerLabelSize');
      // O navegador usa a opção selected como valor; o DOM do teste não.
      const marcada=sel.innerHTML.match(/value="([^"]+)" selected/);sel.value=marcada?marcada[1]:'';
      return {papel:sel.value,opcoes:(sel.innerHTML.match(/value="[^"]+"/g)||[]).length,perfil:document.getElementById('printerProfile').value,inverter:document.getElementById('printerInvert').checked};};
    try { chaves.forEach(k=>localStorage.removeItem(k)); ${body} }
    finally { document.getElementById=origGet;printViaBluetooth=origPrint;showToast=origToast;loadPrinterDevices=origLoad;
      chaves.forEach(k=>antes[k]==null?localStorage.removeItem(k):localStorage.setItem(k,antes[k])); }
  })()`));
  const salvar = p => `localStorage.removeItem('cdb-impressoras.v2');localStorage.setItem('cdb-printer', ${JSON.stringify(JSON.stringify(p))});localStorage.setItem('cdb-printers', ${JSON.stringify(JSON.stringify([p]))});`;
  const com = (p, extra) => Object.assign({}, p, extra);

  test('#2202 F0 papel salvo: o papel de cada impressora sai do catálogo, contínuo quando não há altura', () => {
    const papel = p => `cdbImpressoras.deLegado(${JSON.stringify(p)}).midia.preset`;
    const r = run(`return JSON.stringify({
      semMidia: ${papel(OASIS)},
      recortada: ${papel(com(OASIS, { etiqueta: { larguraMm: 50, alturaMm: 30, gapMm: 2, continuo: false } }))},
      escolhida: ${papel(com(OASIS, { midia: { preset: 'etiqueta-80x40', tipo: 'etiqueta', larguraMm: 80, alturaMm: 40, gapMm: 2 }, etiqueta: { larguraMm: 80, alturaMm: 40, gapMm: 2, continuo: false } }))},
      continua: ${papel(com(OASIS, { etiqueta: { larguraMm: 80, alturaMm: 0, gapMm: 0, continuo: true } }))},
      preset: ${papel(com(OASIS, { midia: { preset: 'continuo-52', tipo: 'continuo', larguraMm: 52 } }))},
      cupom: ${papel(MTP)} });`);
    assert.deepStrictEqual(r, { semMidia: 'continuo-80', recortada: 'etiqueta-50x30', escolhida: 'etiqueta-80x40', continua: 'continuo-80', preset: 'continuo-52', cupom: 'bobina-58' });
  });

  // O master abria o seletor sempre em 80 x 40 recortada e gravava isso sem o
  // operador escolher. Sem midia, esse registro vale o padrão D1.
  test('#2202 F0 Oasis gravada pelo master em 80 x 40 sem escolha imprime em rolo contínuo', () => {
    const legado = com(OASIS, { etiqueta: { larguraMm: 80, alturaMm: 40, gapMm: 2, continuo: false, inverter: false } });
    const r = run(`${salvar(legado)}
      const cfg=tsplConfig(${JSON.stringify(legado)});const acoes=acoesTspl(${JSON.stringify(legado)});
      const sel=abrir(${JSON.stringify(legado)},'etiqueta');
      return JSON.stringify({cfg,acoes,papel:sel.papel,salva:tsplConfig(getSavedPrinter())});`);
    assert.deepStrictEqual(r.cfg, { larguraMm: 80, alturaMm: 0, gapMm: 0, continuo: true, inverter: false });
    assert.deepStrictEqual(r.acoes, { alinhar: true, calibrar: false });
    assert.equal(r.papel, 'continuo-80');
    assert.deepStrictEqual(r.salva, { larguraMm: 80, alturaMm: 0, gapMm: 0, continuo: true, inverter: false }, 'a migração para o v2 mantém o contínuo');
  });

  test('#2202 F0 passo 3 lista só o papel da linguagem: etiqueta e rolo na Oasis, bobina de 58 mm na MTP', () => {
    const r = run(`${salvar(MTP)}
      const mtp=abrir(${JSON.stringify(MTP)},'cupom');
      const oasis=abrir(${JSON.stringify(OASIS)},'etiqueta');
      return JSON.stringify({mtp:[mtp.opcoes,mtp.papel],oasis:[oasis.opcoes,oasis.papel]});`);
    assert.deepStrictEqual(r, { mtp: [1, 'bobina-58'], oasis: [13, 'continuo-80'] });
  });

  test('#2202 F0 seletor: abrir mostra o papel, a linguagem e a inversão salvos, e salvar de novo não muda nada', () => {
    const salva = com(OASIS, { perfilManual: true, etiqueta: { larguraMm: 52, alturaMm: 30, gapMm: 2, continuo: false, inverter: true } });
    const r = run(`${salvar(salva)}
      const sel=abrir(${JSON.stringify(salva)},'etiqueta');
      const de_novo=impressoraDoSeletor(${JSON.stringify(OASIS.address)},${JSON.stringify(OASIS.name)});
      return JSON.stringify({sel,etiqueta:de_novo.etiqueta,config:tsplConfig(de_novo)});`);
    assert.deepStrictEqual(r.sel, { papel: 'etiqueta-52x30', opcoes: 13, perfil: 'tspl', inverter: true }, 'só papéis de etiqueta para a Oasis');
    assert.deepStrictEqual(r.etiqueta, { larguraMm: 52, alturaMm: 30, gapMm: 2, continuo: false, inverter: true });
    assert.deepStrictEqual(r.config, { larguraMm: 52, alturaMm: 30, gapMm: 2, continuo: false, inverter: true });
  });

  test('#2202 F0 contínuo é o padrão: Oasis sem papel salvo usa GAP 0,0 e altura do conteúdo', () => {
    const r = run(`${salvar(OASIS)}
      const sel=abrir(${JSON.stringify(OASIS)},'etiqueta');
      const nova=impressoraDoSeletor(${JSON.stringify(OASIS.address)},${JSON.stringify(OASIS.name)});
      return JSON.stringify({papel:sel.papel,legado:tsplConfig(${JSON.stringify(OASIS)}),nova:tsplConfig(nova),midia:nova.midia,desc:descreverPerfil(nova)});`);
    assert.strictEqual(r.papel, 'continuo-80');
    assert.deepStrictEqual(r.legado, { larguraMm: 80, alturaMm: 0, gapMm: 0, continuo: true, inverter: false }, 'D1: sem etiqueta salva, rolo contínuo');
    assert.deepStrictEqual(r.nova, { larguraMm: 80, alturaMm: 0, gapMm: 0, continuo: true, inverter: false });
    assert.deepStrictEqual(r.midia, { preset: 'continuo-80', tipo: 'continuo', larguraMm: 80 });
    assert.strictEqual(r.desc, 'TSPL bobina contínua');
  });

  test('#2202 F0 seletor: papel de etiqueta não vai para a impressora de cupom', () => {
    const r = run(`${salvar(OASIS)}
      abrir(${JSON.stringify(OASIS)},'etiqueta');
      const mtp=impressoraDoSeletor(${JSON.stringify(MTP.address)},${JSON.stringify(MTP.name)});
      return JSON.stringify({profile:mtp.profile,midia:mtp.midia,desc:descreverPerfil(mtp)});`);
    assert.deepStrictEqual(r, { profile: 'escpos', midia: { preset: 'bobina-58', tipo: 'bobina', larguraMm: 58 }, desc: 'ESC/POS 58 mm' });
  });

  test('#2202 F0 calibrar e alinhar: recortada manda GAPDETECT; alinhamento imprime moldura, cruz e régua', () => {
    const recortada = com(OASIS, { etiqueta: { larguraMm: 52, alturaMm: 30, gapMm: 2, continuo: false } });
    const continua = com(OASIS, { etiqueta: { larguraMm: 80, alturaMm: 0, gapMm: 0, continuo: true } });
    const r = run(`${salvar(recortada)}
      calibrarImpressora(); alinharImpressora();
      const rec=enviados.slice();enviados.length=0;
      ${salvar(continua)}
      calibrarImpressora(); alinharImpressora();
      return JSON.stringify({rec,cont:enviados.slice(),acoesRec:acoesTspl(${JSON.stringify(recortada)}),acoesCont:acoesTspl(${JSON.stringify(continua)}),acoesCupom:acoesTspl(${JSON.stringify(MTP)})});`);
    assert.match(r.rec[0], /SIZE 52 mm,30 mm\r\nGAP 2 mm,0 mm\r\nSET TEAR ON\r\nGAPDETECT/);
    // #2247 (v3): o teste sai na cabeça de 104 mm, com a moldura no papel centralizado.
    assert.match(r.rec[1], /SIZE 104 mm,30 mm\r\nGAP 2 mm,0 mm\r\nSET TEAR ON/);
    assert.match(r.rec[1], /BOX 216,8,615,231,2/);
    assert.match(r.rec[1], /PRINT 1,1/);
    assert.equal(r.cont.length, 1, 'contínuo não calibra: só o alinhamento sai');
    assert.match(r.cont[0], /SIZE 104 mm,30 mm\r\nGAP 0,0/);
    assert.match(r.cont[0], /BOX 104,8,727,231,2/);
    assert.deepStrictEqual(r.acoesRec, { alinhar: true, calibrar: true });
    assert.deepStrictEqual(r.acoesCont, { alinhar: true, calibrar: false }, 'contínuo não precisa medir recorte');
    assert.deepStrictEqual(r.acoesCupom, { alinhar: false, calibrar: false });
  });
};
