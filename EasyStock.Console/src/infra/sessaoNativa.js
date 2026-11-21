const criarErro = (codigo, mensagem, status = null) => Object.assign(new Error(mensagem), { codigo, status })

// A bridge existe apenas no frame principal da origem HTTPS autorizada pelo APK.
// Refresh e credencial do Google ficam com o dono nativo da sessão.
export function criarCanalSessaoNativa(ponte, { tempoMaximo = 20000 } = {}) {
  const pendentes = new Map()
  let sequencia = 0
  let restauracao = null
  let rejeicaoAtual = null
  let fechado = false

  ponte.onmessage = ({ data }) => {
    let resposta
    try { resposta = JSON.parse(data) } catch { return }
    const pedido = pendentes.get(resposta?.id)
    if (!pedido) return
    pendentes.delete(resposta.id)
    clearTimeout(pedido.prazo)
    if (resposta.erro) pedido.rejeitar(criarErro(resposta.erro.codigo, resposta.erro.mensagem, resposta.erro.status))
    else pedido.resolver(resposta.dados)
  }

  function interromper() {
    for (const pedido of pendentes.values()) {
      clearTimeout(pedido.prazo)
      pedido.rejeitar(criarErro('SESSAO_ALTERADA', 'A sessão foi encerrada ou trocada.'))
    }
    pendentes.clear()
    restauracao = null
    rejeicaoAtual = null
  }

  function pedir(acao, dados = {}) {
    if (fechado) return Promise.reject(criarErro('NATIVO_INDISPONIVEL', 'Reabra o atendimento pelo aplicativo.'))
    const id = `sessao-${++sequencia}`
    return new Promise((resolver, rejeitar) => {
      const prazo = setTimeout(() => {
        pendentes.delete(id)
        rejeitar(criarErro('NATIVO_NAO_RESPONDEU', 'O aplicativo não respondeu. Tente de novo.'))
      }, acao === 'entrar' ? Math.max(tempoMaximo, 180000) : tempoMaximo)
      pendentes.set(id, { resolver, rejeitar, prazo, acao })
      try { ponte.postMessage(JSON.stringify({ id, acao, ...dados })) }
      catch {
        clearTimeout(prazo)
        pendentes.delete(id)
        rejeitar(criarErro('NATIVO_INDISPONIVEL', 'Reabra o atendimento pelo aplicativo.'))
      }
    })
  }

  return {
    obter({ tokenRejeitado = null } = {}) {
      if (tokenRejeitado && tokenRejeitado !== rejeicaoAtual) {
        for (const [id, pedido] of pendentes) if (pedido.acao === 'obter') {
          clearTimeout(pedido.prazo)
          pedido.rejeitar(criarErro('SESSAO_ALTERADA', 'A conta está sendo revalidada.'))
          pendentes.delete(id)
        }
        restauracao = null
      }
      if (!restauracao) {
        rejeicaoAtual = tokenRejeitado
        const atual = pedir('obter', tokenRejeitado ? { tokenRejeitado } : {})
        restauracao = atual
        const limpar = () => { if (restauracao === atual) restauracao = null }
        atual.then(limpar, limpar)
      }
      return restauracao
    },
    entrar() { interromper(); return pedir('entrar') },
    sair() { interromper(); return pedir('sair') },
    fechar() { fechado = true; interromper(); ponte.onmessage = null },
  }
}

let ponteAtual = null
let canalAtual = null
export const temSessaoNativa = () => typeof globalThis.window?.EasyStokSession?.postMessage === 'function'

function canal() {
  const ponte = globalThis.window?.EasyStokSession
  if (!temSessaoNativa()) return null
  if (ponteAtual !== ponte) {
    canalAtual?.fechar()
    ponteAtual = ponte
    canalAtual = criarCanalSessaoNativa(ponte)
  }
  return canalAtual
}

export const obterSessaoNativa = (opcoes) => canal()?.obter(opcoes) ?? Promise.resolve(null)
export const entrarGoogleNativo = () => canal()?.entrar() ?? Promise.reject(criarErro('NATIVO_INDISPONIVEL', 'Abra o atendimento pelo aplicativo.'))
export const sairSessaoNativa = () => canal()?.sair() ?? Promise.resolve(null)
