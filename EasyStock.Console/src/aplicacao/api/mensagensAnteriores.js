import * as acao from '../acoes'
import { listarMensagens } from '../../infra/api/conversasApi'
import { MENSAGENS_POR_LEITURA, mensagemDaApi } from '../../infra/api/traducaoConversas'

// #2005 (console-09): o fio mostra as 100 mensagens mais recentes (é o que a polling relê a cada volta). "Ver
// mensagens anteriores" abre a página de antes da mais antiga que a tela já tem, pelo cursor `antesDe` da API,
// e o reducer a mantém nas voltas seguintes. Uma leitura por conversa de cada vez; o segundo clique recebe a mesma.
const emCurso = (m) => m.status === 'enviando' || m.status === 'falhou' || Boolean(m.aguardandoSync)

export function criarAcoesMensagensAnterioresApi({ despachar, estadoRef }) {
  const lendo = new Map()

  async function ler(id) {
    const conversa = estadoRef.current.conversas.find((c) => c.id === id)
    // Só as que o EasyStok já gravou entram na conta: o balão ainda "enviando" não tem hora de servidor.
    const daApi = (conversa?.mensagens ?? []).filter((m) => !emCurso(m))
    if (daApi.length === 0) return false
    const maisAntiga = daApi.reduce((a, b) => (Date.parse(b.em) < Date.parse(a.em) ? b : a)).em
    try {
      const pagina = (await listarMensagens(id, { antesDe: maisAntiga })) ?? []
      const mensagens = pagina.map((m) => mensagemDaApi(m, id)).sort((a, b) => Date.parse(a.em) - Date.parse(b.em))
      despachar({ tipo: acao.ANTERIORES_CARREGADAS, id, mensagens, esgotou: pagina.length < MENSAGENS_POR_LEITURA })
      return true
    } catch (erro) {
      despachar({ tipo: acao.AVISO_API, mensagem: `Mensagens anteriores: ${erro.message}` })
      return false
    }
  }

  return {
    carregarMensagensAnteriores: (id) => {
      if (!lendo.has(id)) lendo.set(id, ler(id).finally(() => lendo.delete(id)))
      return lendo.get(id)
    },
  }
}
