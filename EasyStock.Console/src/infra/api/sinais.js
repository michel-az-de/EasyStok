// #2005 (console-06): `AbortSignal.any` só existe no Chrome 116, Safari 17.4 e Firefox 124, acima do alvo
// do build (Chrome 111, Safari 16.4, Firefox 114). No tablet mais velho a exceção saía antes do `fetch`
// e virava "Sem conexão com o EasyStok". Quem precisa de "o que abortar primeiro" chama daqui: o nativo
// quando existe, uma ponte pequena quando não.
export function combinarSinais(sinais) {
  if (typeof AbortSignal.any === 'function') return AbortSignal.any(sinais)
  const controle = new AbortController()
  const abortado = sinais.find((sinal) => sinal.aborted)
  if (abortado) {
    controle.abort(abortado.reason)
    return controle.signal
  }
  const abortar = (evento) => {
    for (const sinal of sinais) sinal.removeEventListener('abort', abortar)
    controle.abort(evento.target.reason)
  }
  for (const sinal of sinais) sinal.addEventListener('abort', abortar, { once: true })
  return controle.signal
}
