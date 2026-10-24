import { PAUSA_POR_ASSUMIR } from '../../dominio/automatico'
import {
  bloquearCadastro, conversasDoCadastro, desbloquearCadastro, novoBloqueio,
} from '../../dominio/conversa'
import { dataHora } from '../../dominio/formato'
import { apurar as apurarOcorrenciaDominio, encerrarSemEstorno } from '../../dominio/ocorrencia'
import * as acao from '../acoes'
import { comMensagem, mapear } from '../reducer/auxiliares'
import { ATENDENTE } from '../reducer/estadoInicial'

// SÓ DA DEMONSTRAÇÃO (carregado por `src/demonstracao/`, fora do build de produção): nota, regras automáticas,
// assumir e devolver, ocorrência e bloqueio feitos só no navegador. No modo API o EasyStok é a fonte.

export const casosAtendimentoLocal = {
  [acao.SALVAR_NOTA]: (estado, { id, texto, agora, notaId }) =>
    mapear(estado, id, (c) => ({
      ...c,
      cliente: {
        ...c.cliente,
        // `criadoEm` (#1441): a nota entra no fio da conversa como post-it pela hora.
        notas: [{ id: notaId, autor: ATENDENTE, em: dataHora(agora), criadoEm: new Date(agora).toISOString(), texto }, ...c.cliente.notas],
      },
    })),

  [acao.ALTERNAR_REGRA]: (estado, { regraId }) => ({
    ...estado,
    regras: estado.regras.map((r) => (r.id === regraId ? { ...r, ativa: !r.ativa } : r)),
  }),

  // UC-06 passos 3-4: a dona entra e apura.
  [acao.APURAR_OCORRENCIA]: (estado, { id, agora }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido?.ocorrencia) return estado
    const ocorrencia = apurarOcorrenciaDominio(conversa.pedido.ocorrencia, agora)
    return mapear(estado, id, (c) => ({ ...c, pedido: { ...c.pedido, ocorrencia } }))
  },

  // Alternativo A: não era defeito, é preferência. Fecha sem mexer na
  // cobrança e a preferência vira nota interna no cadastro.
  [acao.ENCERRAR_OCORRENCIA_SEM_ESTORNO]: (estado, { id, agora, preferencia, notaId }) => {
    const conversa = estado.conversas.find((c) => c.id === id)
    if (!conversa?.pedido?.ocorrencia) return estado
    const ocorrencia = encerrarSemEstorno(conversa.pedido.ocorrencia, { preferencia }, agora)
    if (ocorrencia === conversa.pedido.ocorrencia) return estado
    const nota = { id: notaId, autor: ATENDENTE, em: dataHora(agora), texto: `Sem estorno. ${preferencia}` }
    return mapear(estado, id, (c) => ({
      ...c,
      cliente: { ...c.cliente, notas: [nota, ...c.cliente.notas] },
      pedido: { ...c.pedido, ocorrencia },
    }))
  },

  // Assumir é um toque: a conversa fica com ela e o automático para na hora
  // (RN-04). Aviso de esteira continua saindo, que é a RN-05.
  [acao.ASSUMIR_ATENDIMENTO]: (estado, { id }) => ({
    ...mapear(estado, id, (c) => ({
      ...c,
      responsavel: ATENDENTE,
      estado: c.estado === 'Encerrado' ? c.estado : 'Em atendimento',
      passagem: c.passagem ? { ...c.passagem, assumida: true } : null,
    })),
    automaticoPausado: { ...estado.automaticoPausado, [id]: PAUSA_POR_ASSUMIR },
  }),

  // Devolver ao automático é decisão dela, nunca do relógio (D4).
  [acao.DEVOLVER_AUTOMATICO]: (estado, { id }) => ({
    ...mapear(estado, id, (c) => ({ ...c, passagem: null })),
    automaticoPausado: { ...estado.automaticoPausado, [id]: false },
  }),

  // Regra é dado: mudar o texto ou a espera é trocar um campo da entrada, e
  // nenhum componente precisa saber disso.
  [acao.EDITAR_REGRA]: (estado, { regraId, campos }) => ({
    ...estado,
    regras: estado.regras.map((r) => (r.id === regraId ? { ...r, ...campos } : r)),
  }),

  // RN-14: bloquear é do cadastro, não da conversa. Toda conversa do mesmo
  // cadastro, em qualquer canal, fica bloqueada junto e com o automático
  // desligado. Rodada 5 (seção 2, passo zero): o alvo é `cadastroId`, não
  // mais `nome` (renomear na edição em linha não pode quebrar o bloqueio).
  [acao.BLOQUEAR_CLIENTE]: (estado, { cadastroId, motivo, em, por, agora, mensagemId }) => {
    const alvos = conversasDoCadastro(estado.conversas, cadastroId)
    if (alvos.length === 0) return estado
    const idsAlvo = new Set(alvos.map((c) => c.id))
    const bloqueio = novoBloqueio({ motivo, em, por })
    const texto = `Cadastro bloqueado por ${por}. Motivo: ${bloqueio.motivo} `
      + 'Atendimento automático suspenso em todos os canais.'
    const conversas = bloquearCadastro(estado.conversas, cadastroId, bloqueio).map((c) =>
      (idsAlvo.has(c.id)
        ? comMensagem(c, { dir: 'sistema', texto }, { id: `${mensagemId}-${c.id}`, agora })
        : c))
    return {
      ...estado,
      conversas,
      automaticoPausado: {
        ...estado.automaticoPausado,
        ...Object.fromEntries(alvos.map((c) => [c.id, true])),
      },
    }
  },

  // Desbloquear devolve a escrita, nunca o automático: quem religa o robô para
  // um cadastro que já deu problema é ela, num segundo gesto.
  [acao.DESBLOQUEAR_CLIENTE]: (estado, { cadastroId, por, agora, mensagemId }) => {
    const alvos = conversasDoCadastro(estado.conversas, cadastroId)
    if (alvos.length === 0) return estado
    const idsAlvo = new Set(alvos.map((c) => c.id))
    const texto = `Bloqueio removido por ${por}. `
      + 'O atendimento automático segue desligado até você retomar.'
    const conversas = desbloquearCadastro(estado.conversas, cadastroId).map((c) =>
      (idsAlvo.has(c.id)
        ? comMensagem(c, { dir: 'sistema', texto }, { id: `${mensagemId}-${c.id}`, agora })
        : c))
    return { ...estado, conversas }
  },
}
