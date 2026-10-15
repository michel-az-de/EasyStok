import * as acao from '../acoes'
import { estaAberta } from '../../dominio/funcionamento'
import { fecharLojaPedeJustificativa } from '../../dominio/aberturaDaLoja'
import { abrirCaixa } from '../../infra/api/caixaApi'
import { identidadeDaSessao, lerSessao } from '../../infra/api/sessao'
import {
  CONTROLE, atualizarExpediente, definirControleExpediente, expedienteDaApi, horariosParaApi, obterExpediente,
} from '../../infra/api/expedienteApi'

// Espera entre a última mudança de horário e o PUT: o <input type="time"> muda a cada
// dígito, e uma semana inteira editada não precisa virar vinte chamadas.
const ESPERA_HORARIO_MS = 800

const SO_ADMINISTRADOR = 'Só gerente ou dona abre e fecha a loja; o horário é só da dona.'

// O provider desmonta ao visitar o Hall; uma gravação já enviada continua na mesma sessão.
let leituraDaSessao = null

// Expediente no modo API (F02, S40). O despacho local vem antes (a tela responde na hora);
// a resposta da API substitui o estado local, porque ela é a verdade. Erro vai para a FaixaApi
// e o estado volta ao que a API tem.
export function criarAcoesExpedienteApi({ despachar, estadoRef }) {
  const identidade = identidadeDaSessao(lerSessao())
  if (leituraDaSessao?.identidade !== identidade) leituraDaSessao = { identidade, leitura: null }
  const memoria = leituraDaSessao
  memoria.despachar = despachar
  const gravar = async (chamada) => {
    memoria.leitura = null
    let falhou = false
    try { return await chamada() }
    catch (erro) { falhou = true; throw erro }
    finally {
      memoria.leitura = null
      if (falhou && memoria.despachar !== despachar) memoria.recarregar()
    }
  }
  const sincronizar = (expediente) => {
    const confirmado = expedienteDaApi(expediente)
    memoria.despachar({ tipo: acao.SINCRONIZAR_EXPEDIENTE, ...confirmado })
    return confirmado
  }
  const avisar = (prefixo) => (erro) => memoria.despachar({ tipo: acao.AVISO_API, mensagem: `${prefixo}: ${erro.message}` })

  // F13, item 5 (#1243): o expediente é só de Admin na API. O operador comum não administra o
  // horário, então ler com 403 não é erro dele; agir diz de quem é a decisão.
  const recarregarExpediente = () => {
    const leitura = {}
    memoria.leitura = leitura
    leitura.promessa = obterExpediente()
      .then((expediente) => memoria.leitura === leitura ? sincronizar(expediente) : memoria.leitura?.promessa)
      .catch((erro) => {
        if (memoria.leitura !== leitura) return memoria.leitura?.promessa
        if (erro.status !== 403) avisar('Expediente não carregou')(erro)
      })
    return leitura.promessa
  }
  memoria.recarregar = recarregarExpediente

  const definirControle = (controle, desfazer = () => {}) => gravar(() => definirControleExpediente(controle))
    .then(sincronizar)
    .catch((erro) => {
      if (erro.status === 403) {
        memoria.despachar({ tipo: acao.AVISO_API, mensagem: SO_ADMINISTRADOR })
        desfazer()
        return
      }
      avisar('Loja não mudou no EasyStok')(erro)
      if (memoria.despachar === despachar) memoria.recarregar()
    })

  let esperaHorario = null
  const gravarHorarios = () => {
    esperaHorario = null
    const horarios = horariosParaApi(estadoRef.current.funcionamento)
    if (!horarios) return
    gravar(() => atualizarExpediente({ horarios }))
      // Chegou outra edição enquanto a chamada ia: a próxima resposta traz a semana certa.
      .then((expediente) => { if (!esperaHorario) sincronizar(expediente) })
      .catch(avisar('Horário não salvo'))
  }

  return {
    recarregarExpediente,

    salvarHorariosExpediente: async (funcionamento) => {
      const horarios = horariosParaApi(funcionamento)
      if (!horarios) throw new Error('Preencha a abertura e o fechamento de cada dia aberto.')
      const confirmado = await gravar(() => atualizarExpediente({ horarios }, { identidadeEsperada: identidade }))
      if (!Array.isArray(confirmado?.horarios)) throw new Error('O EasyStok não confirmou os horários. Sua edição continua guardada; tente novamente.')
      return sincronizar(confirmado)
    },

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
      sincronizar(await gravar(() => definirControleExpediente(CONTROLE.ABRIR)))
      despachar({ tipo: acao.FECHAR_GESTO_LOJA })
    },

    fecharLojaComJustificativa: async (justificativa) => {
      sincronizar(await gravar(() => definirControleExpediente(CONTROLE.FECHAR, (justificativa ?? '').trim())))
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
      gravar(() => atualizarExpediente({ mensagemForaDoHorario, mensagemLojaFechada }, { identidadeEsperada: identidade })).then(sincronizar),
  }
}
