import * as acao from '../acoes'
import { estaAberta } from '../../dominio/funcionamento'
import { fecharLojaPedeJustificativa } from '../../dominio/aberturaDaLoja'
import { abrirCaixa } from '../../infra/api/caixaApi'
import {
  CONTROLE, atualizarExpediente, definirControleExpediente, expedienteDaApi, horariosParaApi, obterExpediente,
} from '../../infra/api/expedienteApi'

// Espera entre a última mudança de horário e o PUT: o <input type="time"> muda a cada
// dígito, e uma semana inteira editada não precisa virar vinte chamadas.
const ESPERA_HORARIO_MS = 800

const SO_ADMINISTRADOR = 'Só gerente ou dona abre e fecha a loja; o horário é só da dona.'

// Expediente no modo API (F02, S40). O despacho local vem antes (a tela responde na hora);
// a resposta da API substitui o estado local, porque ela é a verdade. Erro vai para a FaixaApi
// e o estado volta ao que a API tem.
export function criarAcoesExpedienteApi({ despachar, estadoRef }) {
  const sincronizar = (expediente) => despachar({ tipo: acao.SINCRONIZAR_EXPEDIENTE, ...expedienteDaApi(expediente) })
  const avisar = (prefixo) => (erro) => despachar({ tipo: acao.AVISO_API, mensagem: `${prefixo}: ${erro.message}` })

  // F13, item 5 (#1243): o expediente é só de Admin na API. O operador comum não administra o
  // horário, então ler com 403 não é erro dele; agir diz de quem é a decisão.
  const recarregarExpediente = () => obterExpediente()
    .then(sincronizar)
    .catch((erro) => { if (erro.status !== 403) avisar('Expediente não carregou')(erro) })

  const definirControle = (controle, desfazer = () => {}) => definirControleExpediente(controle)
    .then(sincronizar)
    .catch((erro) => {
      if (erro.status === 403) {
        despachar({ tipo: acao.AVISO_API, mensagem: SO_ADMINISTRADOR })
        desfazer()
        return
      }
      avisar('Loja não mudou no EasyStok')(erro)
      recarregarExpediente()
    })

  let esperaHorario = null
  const gravarHorarios = () => {
    esperaHorario = null
    const horarios = horariosParaApi(estadoRef.current.funcionamento)
    if (!horarios) return
    atualizarExpediente({ horarios })
      // Chegou outra edição enquanto a chamada ia: a próxima resposta traz a semana certa.
      .then((expediente) => { if (!esperaHorario) sincronizar(expediente) })
      .catch(avisar('Horário não salvo'))
  }

  return {
    recarregarExpediente,

    // F07, item 9: quem monta as ações chama ao desmontar. Sem isso o PUT pendente saía
    // depois da troca de conta, com o horário da empresa A e o token da B.
    cancelarHorarioPendente: () => {
      clearTimeout(esperaHorario)
      esperaHorario = null
    },

    // #1443: abrir a loja é abrir o caixa, e fechar dentro do horário pede justificativa. Os dois
    // casos viram um gesto com conferência (`features/loja`); só fechar fora do horário segue direto.
    alternarLoja: (agora) => {
      const { funcionamento, lojaAberta } = estadoRef.current
      const abrir = !estaAberta(agora, { funcionamento, lojaAberta })
      if (abrir || fecharLojaPedeJustificativa(agora, funcionamento)) {
        despachar({ tipo: acao.PEDIR_GESTO_LOJA, gesto: abrir ? 'abrir' : 'fechar' })
        return
      }
      despachar({ tipo: acao.ALTERNAR_LOJA, agora })
      definirControle(CONTROLE.FECHAR, () => despachar({ tipo: acao.ALTERNAR_LOJA, agora }))
    },

    // O caixa abre primeiro; se a API recusar, a loja não abre. Devolve a promessa: o modal mostra
    // o erro junto do botão e só fecha quando as duas pontas gravaram.
    abrirLojaComCaixa: async ({ saldoInicial = 0, observacoes, caixaJaAberto = false } = {}) => {
      if (!caixaJaAberto) await abrirCaixa({ saldoInicial, observacoes })
      sincronizar(await definirControleExpediente(CONTROLE.ABRIR))
      despachar({ tipo: acao.FECHAR_GESTO_LOJA })
    },

    fecharLojaComJustificativa: async (justificativa) => {
      sincronizar(await definirControleExpediente(CONTROLE.FECHAR, (justificativa ?? '').trim()))
      despachar({ tipo: acao.FECHAR_GESTO_LOJA })
    },

    // Só a dona devolve a loja ao relógio (S40); o botão mora na aba Atendimento da Gestão.
    voltarAoHorario: () => definirControle(CONTROLE.AUTOMATICO),

    editarFuncionamento: (dia, campos) => {
      despachar({ tipo: acao.EDITAR_FUNCIONAMENTO, dia, campos })
      clearTimeout(esperaHorario)
      esperaHorario = setTimeout(gravarHorarios, ESPERA_HORARIO_MS)
    },

    // Devolve a promessa: a aba mostra "salvando" e o erro junto do botão.
    salvarMensagensExpediente: ({ mensagemForaDoHorario, mensagemLojaFechada }) =>
      atualizarExpediente({ mensagemForaDoHorario, mensagemLojaFechada }).then(sincronizar),
  }
}
