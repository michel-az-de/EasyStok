// Avisos no aparelho (#1426): a inscrição Web Push do console, do clique até a API.
// O navegador e a API chegam por parâmetro: a prova roda isto no node com os dois falsos.

export const ESTADOS_AVISO = {
  INDISPONIVEL: 'indisponivel',
  DESLIGADO: 'desligado',
  ATIVANDO: 'ativando',
  ATIVO: 'ativo',
  BLOQUEADO: 'bloqueado',
  SEM_CHAVE: 'sem-chave',
  ERRO: 'erro',
}

const situacao = (estado, mensagem = null) => ({ estado, mensagem })

const semPermissao = (permissao) =>
  situacao(permissao === 'denied' ? ESTADOS_AVISO.BLOQUEADO : ESTADOS_AVISO.DESLIGADO)

// Ao abrir o console: com permissão e assinatura já feitas, grava de novo na API (idempotente),
// que religa a inscrição ao usuário que entrou agora. Nunca pede permissão sozinho.
export async function conferirAvisosNoAparelho({ navegador, api }) {
  if (!navegador.suportado()) return situacao(ESTADOS_AVISO.INDISPONIVEL)
  const permissao = navegador.permissao()
  if (permissao !== 'granted') return semPermissao(permissao)
  try {
    const inscricao = await navegador.inscricaoAtual()
    if (!inscricao) return situacao(ESTADOS_AVISO.DESLIGADO)
    await api.inscreverPush(inscricao)
    return situacao(ESTADOS_AVISO.ATIVO)
  } catch (erro) {
    return situacao(ESTADOS_AVISO.ERRO, erro.message)
  }
}

// O clique do botão. A permissão é o primeiro passo e sai antes de qualquer espera: o navegador
// só aceita o pedido dentro do gesto.
export async function ativarAvisosNoAparelho({ navegador, api }) {
  if (!navegador.suportado()) return situacao(ESTADOS_AVISO.INDISPONIVEL)
  const permissao = await navegador.pedirPermissao()
  if (permissao !== 'granted') return semPermissao(permissao)

  let chave
  try {
    chave = (await api.obterChavePush())?.publicKey
  } catch (erro) {
    if (erro.status === 404) return situacao(ESTADOS_AVISO.SEM_CHAVE)
    return situacao(ESTADOS_AVISO.ERRO, erro.message)
  }
  if (!chave) return situacao(ESTADOS_AVISO.SEM_CHAVE)

  try {
    await api.inscreverPush(await navegador.inscrever(chave))
    return situacao(ESTADOS_AVISO.ATIVO)
  } catch (erro) {
    return situacao(ESTADOS_AVISO.ERRO, erro.message)
  }
}
