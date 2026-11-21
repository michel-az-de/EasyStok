'use strict';
// Trocas de texto que o APK aplica na PWA (#2892). Cada uma precisa casar:
// troca sem alvo deixava o APK com o texto antigo, sem erro nenhum.
// `todas: true` troca todas as ocorrências; sem ele, só a primeira.

const TROCAS = [
  // A configuração precisa existir antes do onboarding inline do PWA.
  { de: '<head>', para: '<head>\n<script src="config.js"></script>' },
  { de: '<title>Easy Stock Mobile</title>', para: '<title>Casa da Baba Piloto</title>' },
  { de: '<h2>Registrar lote</h2>', para: '<h2>Produção</h2>' },
  {
    de: 'Selecione os itens produzidos, revise validade e gere etiquetas em seguida.',
    para: 'Informe a quantidade produzida. Depois, confira o lote e imprima as etiquetas.'
  },
  { de: 'Cadastre o primeiro produto para liberar produção, pedidos e estoque.', para: 'Cadastre um produto para começar.', todas: true },
  {
    de: 'O app costuma se atualizar sozinho ao reabrir. Se precisar, force a verificação manual.',
    para: 'O conteúdo é preparado automaticamente e aplicado na próxima abertura. Mudanças nativas exigem confirmar a instalação no Android. Não desinstale o app.'
  },
  { de: '</head>', para: '<link rel="stylesheet" href="pilot.css">\n</head>' },
  { de: 'PWA antigo', para: 'Sistema Antigo', todas: true },
  { de: 'PWA de origem', para: 'Sistema Antigo', todas: true },
  { de: 'Forçar flush', para: 'Sincronizar', todas: true },
  { de: 'Forçar pull', para: 'Atualizar', todas: true },
  {
    de: '</body>',
    para: '<script src="pilot-auth.js"></script>\n<script src="pilot.js"></script>\n<script src="pilot-ui.js"></script>\n<script src="pilot-settings.js"></script>\n</body>'
  }
];

function trocasSemAlvo(texto, trocas) {
  return trocas.filter(t => !texto.includes(t.de)).map(t => t.de);
}

function aplicarTrocas(texto, trocas) {
  for (const t of trocas) {
    if (!texto.includes(t.de)) throw new Error('Troca do APK sem alvo na PWA: ' + t.de);
    texto = t.todas ? texto.split(t.de).join(t.para) : texto.replace(t.de, () => t.para);
  }
  return texto;
}

module.exports = { TROCAS, aplicarTrocas, trocasSemAlvo };
