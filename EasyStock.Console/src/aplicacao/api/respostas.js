import * as acao from '../acoes'
import {
  arquivarRespostaPronta, criarRespostaPronta, editarRespostaPronta, listarAutomacoes, listarRespostasProntas,
  salvarAutomacao,
} from '../../infra/api/respostasApi'
import { corpoDaResposta, regraDaApi, respostaDaApi } from '../../infra/api/traducaoRespostas'

// Respostas prontas e mensagens automáticas no modo API (#1441, S42). A lista vem do
// EasyStok ao entrar e é relida depois de cada gravação: a tela mostra o que ficou
// salvo, não o que o navegador supôs. Cada ação devolve `true` quando gravou, para a
// tela de configuração fechar o formulário só nesse caso.
export function criarAcoesRespostasApi({ despachar, estadoRef }) {
  const avisar = (mensagem) => despachar({ tipo: acao.AVISO_API, mensagem })

  async function recarregarRespostas() {
    const [respostas, automacoes] = await Promise.all([listarRespostasProntas(), listarAutomacoes()])
    despachar({ tipo: acao.RESPOSTAS_DA_API, respostas: (respostas ?? []).map(respostaDaApi) })
    despachar({ tipo: acao.REGRAS_DA_API, regras: (automacoes ?? []).map(regraDaApi) })
  }

  const gravar = (rotulo, chamada) => chamada()
    .then(() => recarregarRespostas().catch(() => {}))
    .then(() => true)
    .catch((erro) => {
      avisar(`${rotulo}: ${erro.message}`)
      return false
    })

  const regraDe = (id) => (estadoRef.current.regras ?? []).find((r) => r.id === id) ?? null
  const respostaDe = (id) => (estadoRef.current.catalogo?.respostasProntas ?? []).find((r) => r.id === id) ?? null

  return {
    recarregarRespostas: () => recarregarRespostas().catch((erro) => avisar(`Respostas prontas: ${erro.message}`)),

    incluirRespostaPronta: (dados) => gravar('Resposta pronta', () => criarRespostaPronta(corpoDaResposta(dados))),
    editarRespostaPronta: (id, dados) => {
      const atual = respostaDe(id) ?? {}
      return gravar('Resposta pronta', () => editarRespostaPronta(id, corpoDaResposta({ ...atual, ...dados })))
    },
    // Nunca apaga: arquivar some do seletor e a mesma ação restaura.
    alternarArquivamentoRespostaPronta: (id) =>
      gravar('Resposta pronta', () => arquivarRespostaPronta(id, !respostaDe(id)?.arquivada)),

    // A API guarda texto e ligada juntos: ligar sem texto ela recusa, então avisa antes.
    alternarRegra: (regraId) => {
      const regra = regraDe(regraId)
      if (!regra?.gatilhoApi) return Promise.resolve(false)
      if (!regra.ativa && !regra.texto?.trim()) {
        avisar(`Escreva o texto de "${regra.nome}" antes de ligar.`)
        return Promise.resolve(false)
      }
      return gravar('Mensagem automática', () => salvarAutomacao(regra.gatilhoApi, { texto: regra.texto, ligada: !regra.ativa }))
    },
    editarRegra: (regraId, campos) => {
      const regra = regraDe(regraId)
      if (!regra?.gatilhoApi) return Promise.resolve(false)
      const ligada = campos.ativa ?? regra.ativa
      return gravar('Mensagem automática', () => salvarAutomacao(regra.gatilhoApi, { texto: campos.texto ?? regra.texto, ligada }))
    },
  }
}
