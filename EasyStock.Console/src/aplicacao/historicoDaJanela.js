// #2944 (E1-18): a janela "Novo pedido" ganha uma entrada própria no histórico (sem mudar a hash); o "voltar" do navegador
// consome essa entrada e fecha só a janela, e a tela de origem fica como estava. O rascunho já é guardado pela janela.
// Fechar pela tela desfaz a entrada que sobrou. Avançar não reabre nada: ninguém escuta o estado da entrada.
//
// `history.back()` é assíncrono: no StrictMode do dev o cleanup chama back() e a remontagem já ouve o popstate dele. Por isso
// o back que a própria janela pede é contado e engolido por um ouvinte único, e um popstate que cai numa entrada da janela
// (estado com o token de alguma montagem) nunca fecha nada: voltar para fora da janela é cair numa entrada sem token.
export const ESTADO_DA_JANELA = 'easystok-janela-do-pedido'

const registros = new WeakMap()
let montagens = 0

function registroDa(janelaDoNavegador) {
  let registro = registros.get(janelaDoNavegador)
  if (!registro) {
    registro = { voltasProprias: 0, engolidos: new WeakSet() }
    // Registrado antes de qualquer ouvinte de janela: engole o popstate do back() que o cleanup pediu.
    janelaDoNavegador.addEventListener('popstate', (evento) => {
      if (registro.voltasProprias <= 0) return
      registro.voltasProprias -= 1
      registro.engolidos.add(evento)
    })
    registros.set(janelaDoNavegador, registro)
  }
  return registro
}

export function ligarVoltarDaJanela(janelaDoNavegador, aoVoltar) {
  const { history: historico } = janelaDoNavegador
  const registro = registroDa(janelaDoNavegador)
  const token = `${Date.now()}-${(montagens += 1)}`
  let consumida = false
  const ouvir = (evento) => {
    if (consumida || registro.engolidos.has(evento)) return
    if (evento?.state?.[ESTADO_DA_JANELA]) return
    consumida = true
    aoVoltar()
  }
  historico.pushState({ [ESTADO_DA_JANELA]: token }, '')
  janelaDoNavegador.addEventListener('popstate', ouvir)
  return () => {
    janelaDoNavegador.removeEventListener('popstate', ouvir)
    if (!consumida && historico.state?.[ESTADO_DA_JANELA]) {
      registro.voltasProprias += 1
      historico.back()
    }
    consumida = true
  }
}
