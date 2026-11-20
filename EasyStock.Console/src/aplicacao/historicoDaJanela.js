// #2944 (E1-18): a janela "Novo pedido" ganha uma entrada própria no histórico (sem mudar a hash); o "voltar" do navegador
// consome essa entrada e fecha só a janela, e a tela de origem fica como estava. O rascunho já é guardado pela janela.
// Fechar pela tela desfaz a entrada que sobrou. Avançar não reabre nada: ninguém escuta o estado da entrada.
export const ESTADO_DA_JANELA = 'easystok-janela-do-pedido'

export function ligarVoltarDaJanela(janelaDoNavegador, aoVoltar) {
  const { history: historico } = janelaDoNavegador
  let consumida = false
  const ouvir = () => {
    if (consumida) return
    consumida = true
    aoVoltar()
  }
  historico.pushState({ [ESTADO_DA_JANELA]: true }, '')
  janelaDoNavegador.addEventListener('popstate', ouvir)
  return () => {
    janelaDoNavegador.removeEventListener('popstate', ouvir)
    if (!consumida && historico.state?.[ESTADO_DA_JANELA]) historico.back()
    consumida = true
  }
}
