export const ESTADOS_AVISO = {
  INDISPONIVEL: 'indisponivel', DESLIGADO: 'desligado', ATIVANDO: 'ativando',
  ATIVO: 'ativo', BLOQUEADO: 'bloqueado', SEM_CHAVE: 'sem-chave', ERRO: 'erro',
}
const situacao = (estado, mensagem = null) => ({ estado, mensagem })
const semPermissao = (permissao) => situacao(permissao === 'denied' ? ESTADOS_AVISO.BLOQUEADO : ESTADOS_AVISO.DESLIGADO)

async function registrar(inscricao, { navegador, api, identidade, atual }, nova = false) {
  try {
    if (!atual()) throw new Error('A sessão mudou. Entre novamente antes de ativar os avisos.')
    await api.inscreverPush(inscricao)
    if (!atual()) throw new Error('A sessão mudou. Os avisos desta tentativa foram desligados.')
    navegador.associar(identidade, inscricao.endpoint)
    return situacao(ESTADOS_AVISO.ATIVO)
  } catch (erro) {
    // Uma ativação sem confirmação não pode continuar recebendo em segundo plano.
    if (nova || !atual()) await navegador.desinscrever(null, inscricao.endpoint)
    throw erro
  }
}

// Só reinscreve a assinatura que já pertence à mesma pessoa e empresa.
export async function conferirAvisosNoAparelho(contexto) {
  const { navegador, identidade } = contexto
  if (!navegador.suportado()) return situacao(ESTADOS_AVISO.INDISPONIVEL)
  const permissao = navegador.permissao()
  if (permissao !== 'granted') return semPermissao(permissao)
  try {
    const inscricao = await navegador.inscricaoAtual(identidade)
    return inscricao ? await registrar(inscricao, contexto) : situacao(ESTADOS_AVISO.DESLIGADO)
  } catch (erro) { return situacao(ESTADOS_AVISO.ERRO, erro.message) }
}

export async function ativarAvisosNoAparelho(contexto) {
  const { navegador, api, identidade, atual } = contexto
  if (!navegador.suportado()) return situacao(ESTADOS_AVISO.INDISPONIVEL)
  try {
    // A permissão precisa ser pedida dentro do gesto, antes da primeira espera.
    const permissao = await navegador.pedirPermissao()
    if (permissao !== 'granted') return semPermissao(permissao)
    if (!atual()) return situacao(ESTADOS_AVISO.DESLIGADO)
    let chave
    try { chave = (await api.obterChavePush())?.publicKey }
    catch (erro) {
      if (erro.status === 404) return situacao(ESTADOS_AVISO.SEM_CHAVE)
      throw erro
    }
    if (!chave) return situacao(ESTADOS_AVISO.SEM_CHAVE)
    if (!atual()) return situacao(ESTADOS_AVISO.DESLIGADO)
    const inscricao = await navegador.inscrever(chave, identidade)
    return await registrar(inscricao, contexto, true)
  } catch (erro) { return situacao(ESTADOS_AVISO.ERRO, erro.message) }
}
