'use strict';
// #3251 (P2 da #3114): o conteúdo web da homologação declara o ambiente em todo fetch à API, e a
// API da produção recusa (TravaDeAmbienteMiddleware). Carrega logo depois do config.js, antes do PWA.
// O oficial não muda: o fetch fica o original e nenhum cabeçalho novo sai.
(function (raiz) {
  function instalarAmbiente(alvo, config) {
    if (!config || !config.ambiente || config.ambiente === 'oficial' || !config.apiBaseUrl || typeof alvo.fetch !== 'function') return false;
    const origem = new URL(config.apiBaseUrl).origin;
    const original = alvo.fetch;
    const base = alvo.location && alvo.location.href;
    alvo.fetch = function (input, init) {
      // Decide pela URL sem montar Request: fora da API o fetch recebe exatamente o que o chamador passou.
      let url;
      try { url = new URL(input instanceof Request ? input.url : String(input), base); } catch (_) { url = null; }
      if (!url || url.origin !== origem) return original.call(alvo, input, init);
      try {
        const cabecalhos = new Headers(init && init.headers !== undefined ? init.headers : input instanceof Request ? input.headers : undefined);
        cabecalhos.set('X-Cdb-Ambiente', config.ambiente);
        return original.call(alvo, input, Object.assign({}, init, { headers: cabecalhos }));
      } catch (erro) {
        return Promise.reject(erro);
      }
    };
    return true;
  }
  if (typeof module !== 'undefined' && module.exports) module.exports = { instalarAmbiente };
  else instalarAmbiente(raiz, raiz.CDB_CONFIG);
})(typeof window !== 'undefined' ? window : globalThis);
