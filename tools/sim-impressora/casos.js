'use strict';
// Matriz do gate 4 (spec impressao-v2 §4): 3 tamanhos x 2 orientações x
// 3 opções de código x 2 mídias = 36 casos, todos na Oasis (TSPL, 104 mm).

// Mídia física carregada na impressora em cada caso.
const TAMANHOS = {
  '52': { continuo: { tipo: 'continuo', larguraMm: 52, preset: 'continuo-52' }, recortado: { tipo: 'recortado', larguraMm: 52, alturaMm: 80, gapMm: 2, preset: 'etiqueta-52x80' } },
  '80': { continuo: { tipo: 'continuo', larguraMm: 80, preset: 'continuo-80' }, recortado: { tipo: 'recortado', larguraMm: 80, alturaMm: 50, gapMm: 2, preset: 'etiqueta-80x50' } },
  '10x15': { continuo: { tipo: 'continuo', larguraMm: 100, preset: 'continuo-100' }, recortado: { tipo: 'recortado', larguraMm: 100, alturaMm: 150, gapMm: 3, preset: 'etiqueta-100x150' } }
};
const ORIENTACOES = ['vertical', 'horizontal'];
const CODIGOS = { qr: ['qr'], barras: ['barras'], ambos: ['qr', 'barras'] };
const MIDIAS = ['continuo', 'recortado'];

// Dados do protótipo da auditoria 2 (mesmo QR). #2209: a chave curta segue o
// servidor, número curto de 8 dígitos + unidade com 4 = 12 dígitos.
const DADOS = {
  empresa: 'Casa da Baba',
  produto: { id: 'talharim-espinafre', name: 'Talharim Fresco de Espinafre', sku: 'TALHARIM' },
  // 03/11/2026 20:40 em São Paulo; validade em 06/11/2026.
  criadoEm: Date.UTC(2026, 10, 3, 23, 40),
  validade: Date.UTC(2026, 10, 6, 15, 0),
  batchId: 'lote-0001-707082',
  loteDoDia: 'LOT-261103',
  numeroCurto: '00707082',
  pesoG: 300,
  qr: 'TALHARIM-LOT-261103-707082-001',
  barras: '007070820001'
};

function casos() {
  const out = [];
  for (const tamanho of Object.keys(TAMANHOS)) for (const orientacao of ORIENTACOES)
    for (const codigos of Object.keys(CODIGOS)) for (const midia of MIDIAS) {
      out.push({ id: `${tamanho}-${orientacao}-${codigos}-${midia}`, tamanho, orientacao, codigos, lista: CODIGOS[codigos], midia, fisica: TAMANHOS[tamanho][midia] });
    }
  return out;
}

module.exports = { casos, DADOS, TAMANHOS };
