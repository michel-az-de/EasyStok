
// #2247 (v3): o motor da etiqueta só desenha com a sua fonte. Carrega já no
// boot (woff2 local, no cache do service worker) e refaz a miniatura de Ajustes.
window.cdbResolverEtiqueta && window.cdbResolverEtiqueta.carregarFonte({ base: 'fontes/' }).then(function (ok) {
  if (ok && typeof redrawAjustesEtqThumb === 'function') { try { redrawAjustesEtqThumb(); } catch (e) { /* tela fechada */ } }
});
