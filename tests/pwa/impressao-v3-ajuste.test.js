// #2247 (v3, G1 item 4): ajuste fino por impressora, em passos de 0,5 mm e
// faixa de ±10 mm, salvo em offsetMm do cdb-impressoras.v2. O Teste de
// alinhamento sai na cabeça com o ajuste aplicado.
'use strict';

module.exports = function ({ test, runInSandbox, sandbox, assert }) {
  const V2 = 'cdb-impressoras.v2';
  const MAC = 'DC:0D:30:83:81:0B';
  const ls = sandbox.localStorage;
  const puro = v => JSON.parse(JSON.stringify(v));

  function oasis(offsetMm) {
    guardar();
    ls.setItem(V2, JSON.stringify({
      versao: 2,
      impressoras: { [MAC]: { address: MAC, nome: 'OIA-8381-B', apelido: 'Oasis', linguagem: 'tspl', linguagemManual: false,
        midia: { preset: 'etiqueta-52', tipo: 'etiqueta', larguraMm: 52, alturaMm: 30, gapMm: 2 },
        densidade: 8, velocidade: 4, inverter: false, calibradaEm: null, offsetMm: offsetMm || { x: 0, y: 0 } } },
      papeis: { etiqueta: MAC, cupom: null }
    }));
  }
  const salvo = () => puro(sandbox.window.cdbImpressoras.ler(ls).impressoras[MAC].offsetMm);
  // salvar() espelha a impressora em cdb-printer e cdb-printers: volta as três.
  const CHAVES = [V2, 'cdb-printer', 'cdb-printers'];
  let antes = null;
  const guardar = () => { antes = CHAVES.map(k => ls.getItem(k)); };
  const devolver = () => CHAVES.forEach((k, i) => antes[i] === null ? ls.removeItem(k) : ls.setItem(k, antes[i]));

  test('v3 ajuste: ajusteFino anda 0,5 mm e para em ±10 mm', () => {
    const I = sandbox.window.cdbImpressoras;
    assert.deepStrictEqual(puro(I.ajusteFino({ x: 0, y: 0 }, 0.5, 0)), { x: 0.5, y: 0 });
    assert.deepStrictEqual(puro(I.ajusteFino({ x: 0.2, y: -1 }, 0, -0.5)), { x: 0, y: -1.5 });
    assert.deepStrictEqual(puro(I.ajusteFino({ x: 9.5, y: -9.5 }, 1, -1)), { x: 10, y: -10 });
    assert.deepStrictEqual(puro(I.ajusteFino(null, 0, 0)), { x: 0, y: 0 });
  });

  test('v3 ajuste: as setas do cartão gravam o offsetMm da impressora', () => {
    oasis();
    try {
      runInSandbox(`ajustarPosicao(${JSON.stringify(MAC)}, 0.5, 0)`);
      runInSandbox(`ajustarPosicao(${JSON.stringify(MAC)}, 0, -0.5)`);
      assert.deepStrictEqual(salvo(), { x: 0.5, y: -0.5 });
      runInSandbox(`zerarAjuste(${JSON.stringify(MAC)})`);
      assert.deepStrictEqual(salvo(), { x: 0, y: 0 });
    } finally { devolver(); }
  });

  test('v3 ajuste: o cartão diz o ajuste em palavras', () => {
    assert.strictEqual(runInSandbox(`descreverAjuste({ x: 0, y: 0 })`), 'Ajuste fino: nenhum');
    assert.strictEqual(runInSandbox(`descreverAjuste({ x: 1.5, y: -0.5 })`), 'Ajuste fino: 1,5 mm à direita, 0,5 mm para cima');
    assert.strictEqual(runInSandbox(`descreverAjuste({ x: -2, y: 1 })`), 'Ajuste fino: 2 mm à esquerda, 1 mm para baixo');
  });

  test('v3 ajuste: o Teste de alinhamento sai na cabeça com o ajuste salvo', () => {
    oasis({ x: 1, y: -0.5 });
    runInSandbox(`globalThis.__pvb = printViaBluetooth; globalThis.__enviado = null;
      printViaBluetooth = bytes => { __enviado = Array.from(bytes); };`);
    try {
      runInSandbox(`alinharImpressora(${JSON.stringify(MAC)})`);
      const txt = Buffer.from(runInSandbox('__enviado') || []).toString('latin1');
      assert.ok(txt.startsWith('SIZE 104 mm,30 mm\r\n'), txt.slice(0, 40));
      assert.match(txt, /SHIFT -4\r\n/);
      assert.match(txt, /\r\nBOX 224,8,623,231,2\r\n/);
    } finally { runInSandbox('printViaBluetooth = __pvb;'); devolver(); }
  });
};
