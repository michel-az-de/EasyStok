// #2247 (v3, G1 item 6): catálogo único de papéis. O `impressao-papeis.js` é a
// fonte; `docs/contracts/papeis.v1.json` é gerado dele
// (`node tools/contratos/gerar-papeis.js`) e o servidor confere o mesmo arquivo
// (PapeisContratoTests). Arquivo desatualizado reprova aqui.
'use strict';

const fs = require('fs');
const path = require('path');

module.exports = function ({ test, sandbox, assert }) {
  const P = () => sandbox.window.cdbPapeis;
  const I = () => sandbox.window.cdbImpressoras;
  const ARQ = path.join(__dirname, '..', '..', 'docs', 'contracts', 'papeis.v1.json');

  test('v3 papéis: o contrato no repositório é o gerado do catálogo do app', () => {
    assert.ok(fs.existsSync(ARQ), 'falta docs/contracts/papeis.v1.json; rode node tools/contratos/gerar-papeis.js');
    const noRepo = JSON.parse(fs.readFileSync(ARQ, 'utf8'));
    assert.deepStrictEqual(noRepo, JSON.parse(JSON.stringify(P().contrato())),
      'contrato desatualizado; rode node tools/contratos/gerar-papeis.js');
  });

  test('v3 papéis: os papéis da regra da loja são os do normalizador do app', () => {
    assert.deepStrictEqual(P().contrato().loja.map(p => p.id), I().CATALOGO_REGRA.papeis);
  });

  test('v3 papéis: cada papel da loja aponta para um papel do aparelho com a mesma largura', () => {
    for (const l of P().contrato().loja) {
      if (!l.aparelho) { assert.strictEqual(l.classe, 'folha', l.id); continue; }
      const a = P().porId(l.aparelho);
      assert.ok(a, l.id + ' -> ' + l.aparelho);
      assert.strictEqual(a.classe, l.classe, l.id);
      assert.strictEqual(a.larguraMm, l.larguraMm, l.id);
      assert.strictEqual(l.utilMm, a.tipo === 'bobina' ? a.utilMm : a.larguraMm, l.id);
    }
  });
};
