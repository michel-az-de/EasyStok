import * as acao from '../acoes'
import { estaAberta } from '../../dominio/funcionamento'
import {
  CONTROLE, atualizarExpediente, definirControleExpediente, expedienteDaApi, horariosParaApi, obterExpediente,
} from '../../infra/api/expedienteApi'

// Espera entre a última mudança de horário e o PUT: o <input type="time"> muda a cada
// dígito, e uma semana inteira editada não precisa virar vinte chamadas.
const ESPERA_HORARIO_MS = 800

// Expediente no modo API (F02, S40). O despacho local vem antes (a tela responde na hora);
// a resposta da API substitui o estado local, porque ela é a verdade. Erro vai para a FaixaApi
// e o estado volta ao que a API tem.
export function criarAcoesExpedienteApi({ despachar, estadoRef }) {
  const sincronizar = (expediente) => despachar({ tipo: acao.SINCRONIZAR_EXPEDIENTE, ...expedienteDaApi(expediente) })
  const avisar = (prefixo) => (erro) => despachar({ tipo: acao.AVISO_API, mensagem: `${prefixo}: ${erro.message}` })

  const recarregarExpediente = () => obterExpediente()
    .then(sincronizar)
    .catch(avisar('Expediente não carregou'))

  const definirControle = (controle) => definirControleExpediente(controle)
    .then(sincronizar)
    .catch((erro) => {
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

    alternarLoja: (agora) => {
      const { funcionamento, lojaAberta } = estadoRef.current
      const abrir = !estaAberta(agora, { funcionamento, lojaAberta })
      despachar({ tipo: acao.ALTERNAR_LOJA, agora })
      definirControle(abrir ? CONTROLE.ABRIR : CONTROLE.FECHAR)
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
