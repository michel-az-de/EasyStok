'use strict';
// #3251 (P2 da #3114): o conteúdo web da homologação declara o ambiente em todo fetch à API, e a
// API da produção recusa (TravaDeAmbienteMiddleware). Carrega logo depois do config.js, antes do PWA.
// O oficial não muda: o fetch fica o original e nenhum cabeçalho novo sai.
(function (raiz) {
  function instalarAmbiente(alvo, config) {
    if (!config || !config.ambiente || config.ambiente === 'oficial' || !config.apiBaseUrl || typeof alvo.fetch !== 'function') return false;
    const origem = new URL(config.apiBaseUrl).origin;
    const original = alvo.fetch;
    alvo.fetch = function (input, init) {
      const pedido = new Request(input, init);
      if (new URL(pedido.url).origin !== origem) return original.call(alvo, input, init);
      const cabecalhos = new Headers(pedido.headers);
      cabecalhos.set('X-Cdb-Ambiente', config.ambiente);
      return original.call(alvo, new Request(pedido, { headers: cabecalhos }));
    };
    return true;
  }
  if (typeof module !== 'undefined' && module.exports) module.exports = { instalarAmbiente };
  else instalarAmbiente(raiz, raiz.CDB_CONFIG);
})(typeof window !== 'undefined' ? window : globalThis);
