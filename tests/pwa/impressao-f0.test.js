'use strict';

// #2202 (impressão v2, F0): a configuração da Oasis chega. O seletor mostra o
// papel salvo, o papel vem do catálogo único (impressao-papeis.js), rolo
// contínuo é o padrão (D1) e a etiqueta recortada tem calibração opcional e
// teste de alinhamento.
const OASIS = { name: 'OIA-8381-B', address: 'DC:0D:30:83:81:0B', profile: 'tspl' };
const MTP = { name: 'MTP-II', address: '66:22:B1:4C:58:01', profile: 'escpos' };

module.exports = function ({ test, runInSandbox, assert }) {
  // Roda com DOM que guarda valor por id e chaves de impressora isoladas.
  const run = body => JSON.parse(runInSandbox(`(function(){
    const chaves=['cdb-printer','cdb-printers'];const antes={};chaves.forEach(k=>antes[k]=localStorage.getItem(k));
    const els={};const origGet=document.getElementById;const origPrint=printViaBluetooth;const origToast=showToast;const origLoad=loadPrinterDevices;
    document.getElementById=id=>els[id]||(els[id]=Object.assign(origGet.call(document,id),{checked:false}));
    const enviados=[];printViaBluetooth=(bytes,fb,job,imp,onSent)=>{enviados.push(String.fromCharCode.apply(null,Array.from(bytes)));if(onSent)onSent();};
    const toasts=[];showToast=t=>toasts.push(t);loadPrinterDevices=()=>{};
    try { chaves.forEach(k=>localStorage.removeItem(k)); ${body} }
    finally { document.getElementById=origGet;printViaBluetooth=origPrint;showToast=origToast;loadPrinterDevices=origLoad;
      chaves.forEach(k=>antes[k]==null?localStorage.removeItem(k):localStorage.setItem(k,antes[k])); }
  })()`));
  const salvar = p => `localStorage.setItem('cdb-printer', ${JSON.stringify(JSON.stringify(p))});localStorage.setItem('cdb-printers', ${JSON.stringify(JSON.stringify([p]))});`;
  const com = (p, extra) => Object.assign({}, p, extra);

  test('#2202 F0 papel salvo: o papel de cada impressora sai do catálogo, contínuo quando não há altura', () => {
    const r = run(`return JSON.stringify({
      semMidia: papelDaImpressora(${JSON.stringify(OASIS)}),
      recortada: papelDaImpressora(${JSON.stringify(com(OASIS, { etiqueta: { larguraMm: 80, alturaMm: 40, gapMm: 2, continuo: false } }))}),
      continua: papelDaImpressora(${JSON.stringify(com(OASIS, { etiqueta: { larguraMm: 80, alturaMm: 0, gapMm: 0, continuo: true } }))}),
      preset: papelDaImpressora(${JSON.stringify(com(OASIS, { midia: { preset: 'continuo-52', tipo: 'continuo', larguraMm: 52 } }))}),
      cupom: papelDaImpressora(${JSON.stringify(MTP)}) });`);
    assert.deepStrictEqual(r, { semMidia: 'continuo-80', recortada: 'etiqueta-80x40', continua: 'continuo-80', preset: 'continuo-52', cupom: 'bobina-58' });
  });

  test('#2202 F0 seletor: abrir mostra o papel, a linguagem e a inversão salvos, e salvar de novo não muda nada', () => {
    const salva = com(OASIS, { perfilManual: true, etiqueta: { larguraMm: 52, alturaMm: 30, gapMm: 2, continuo: false, inverter: true } });
    const r = run(`${salvar(salva)}
      openPrinterPicker();
      const sel={perfil:document.getElementById('printerProfile').value,papel:document.getElementById('printerLabelSize').value,inverter:document.getElementById('printerInvert').checked,
        opcoes:(document.getElementById('printerLabelSize').innerHTML.match(/value="[^"]+"/g)||[]).length};
      const de_novo=impressoraDoSeletor(${JSON.stringify(OASIS.address)},${JSON.stringify(OASIS.name)});
      return JSON.stringify({sel,etiqueta:de_novo.etiqueta,config:tsplConfig(de_novo)});`);
    assert.deepStrictEqual(r.sel, { perfil: 'tspl', papel: 'etiqueta-52x30', inverter: true, opcoes: 13 }, 'só papéis de etiqueta para a Oasis');
    assert.deepStrictEqual(r.etiqueta, { larguraMm: 52, alturaMm: 30, gapMm: 2, continuo: false, inverter: true });
    assert.deepStrictEqual(r.config, { larguraMm: 52, alturaMm: 30, gapMm: 2, continuo: false, inverter: true });
  });

  test('#2202 F0 contínuo é o padrão: Oasis sem papel salvo usa GAP 0,0 e altura do conteúdo', () => {
    const r = run(`${salvar(OASIS)}
      openPrinterPicker();
      const nova=impressoraDoSeletor(${JSON.stringify(OASIS.address)},${JSON.stringify(OASIS.name)});
      return JSON.stringify({papel:document.getElementById('printerLabelSize').value,legado:tsplConfig(${JSON.stringify(OASIS)}),nova:tsplConfig(nova),midia:nova.midia,desc:descreverPerfil(nova)});`);
    assert.strictEqual(r.papel, 'continuo-80');
    assert.deepStrictEqual(r.legado, { larguraMm: 80, alturaMm: 0, gapMm: 0, continuo: true, inverter: false }, 'D1: sem etiqueta salva, rolo contínuo');
    assert.deepStrictEqual(r.nova, { larguraMm: 80, alturaMm: 0, gapMm: 0, continuo: true, inverter: false });
    assert.deepStrictEqual(r.midia, { preset: 'continuo-80', tipo: 'continuo', larguraMm: 80 });
    assert.strictEqual(r.desc, 'TSPL bobina contínua');
  });

  test('#2202 F0 seletor: papel de etiqueta não vai para a impressora de cupom', () => {
    const r = run(`${salvar(OASIS)}
      openPrinterPicker();
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
      alinharImpressora();
      return JSON.stringify({rec,cont:enviados.slice(),acoesRec:acoesTspl(${JSON.stringify(recortada)}),acoesCont:acoesTspl(${JSON.stringify(continua)}),acoesCupom:acoesTspl(${JSON.stringify(MTP)})});`);
    assert.match(r.rec[0], /SIZE 52 mm,30 mm\r\nGAP 2 mm,0 mm\r\nSET TEAR ON\r\nGAPDETECT/);
    assert.match(r.rec[1], /SIZE 52 mm,30 mm\r\nGAP 2 mm,0 mm\r\nSET TEAR ON/);
    assert.match(r.rec[1], /BOX /);
    assert.match(r.rec[1], /PRINT 1,1/);
    assert.match(r.cont[0], /SIZE 80 mm,30 mm\r\nGAP 0,0/);
    assert.deepStrictEqual(r.acoesRec, { alinhar: true, calibrar: true });
    assert.deepStrictEqual(r.acoesCont, { alinhar: true, calibrar: false }, 'contínuo não precisa medir recorte');
    assert.deepStrictEqual(r.acoesCupom, { alinhar: false, calibrar: false });
  });
};
