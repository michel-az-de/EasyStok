// Casos de reducer da Frente 5 · Encerrar atendimento com resumo (rodada 5,
// seção 5). Arquivo da F5: nenhuma outra frente edita este arquivo, e a F5
// nunca edita `reducer.js` (ele já importa e espalha `casosEncerramento` no
// objeto composto). ABRIR_ENCERRAMENTO/FECHAR_ENCERRAMENTO (o `ui.encerrando`
// que abre esta modal de qualquer lugar) já vêm prontos do passo zero, em
// `reducer.js`: a F5 só cuida do CONTEÚDO do resumo.
import * as acao from '../acoes'
import { dataHora } from '../../dominio/formato'
import { montarResumoAtendimento } from '../../dominio/resumoAtendimento'

const ATENDENTE = 'Thatiane'

const mapear = (estado, id, transformar) => ({
  ...estado,
  conversas: estado.conversas.map((c) => (c.id === id ? transformar(c) : c)),
})

// Rascunho do fechamento (avaliação do cliente, autoavaliação, anotação):
// vive dentro da própria conversa, como `rascunhos` (composer) já vive fora
// dela — aqui mora junto porque é por conversa, não por tela, e sobrevive a
// fechar/reabrir a modal sem precisar de um campo novo em `estadoInicial`
// (edição vedada para a F5). "Guardar também nas notas" nasce marcada
// (seção 5, "Anotação").
const RASCUNHO_PADRAO = { avaliacaoCliente: null, autoavaliacao: null, anotacao: '', guardarNota: true }
const rascunhoDe = (conversa) => conversa.fechamentoRascunho ?? RASCUNHO_PADRAO

export const casosEncerramento = {
  // Clicar de novo na opção marcada desmarca — escolha única, mas nada
  // obriga ela a decidir agora (a direção prevê "sem resposta ainda").
  [acao.SALVAR_AVALIACAO_CLIENTE]: (estado, { id, valor }) =>
    mapear(estado, id, (c) => {
      const rascunho = rascunhoDe(c)
      return {
        ...c,
        fechamentoRascunho: { ...rascunho, avaliacaoCliente: rascunho.avaliacaoCliente === valor ? null : valor },
      }
    }),

  [acao.SALVAR_AUTOAVALIACAO]: (estado, { id, valor }) =>
    mapear(estado, id, (c) => {
      const rascunho = rascunhoDe(c)
      return {
        ...c,
        fechamentoRascunho: { ...rascunho, autoavaliacao: rascunho.autoavaliacao === valor ? null : valor },
      }
    }),

  // Um caso só para os dois campos da anotação (texto e a caixa "guardar nas
  // notas"): a tela manda só o que mudou, o reducer preenche o resto do
  // rascunho por cima.
  [acao.SALVAR_ANOTACAO_FECHAMENTO]: (estado, { id, texto, guardarNota }) =>
    mapear(estado, id, (c) => ({
      ...c,
      fechamentoRascunho: {
        ...rascunhoDe(c),
        ...(texto !== undefined ? { anotacao: texto } : {}),
        ...(guardarNota !== undefined ? { guardarNota } : {}),
      },
    })),

  // O clique em "Encerrar" (seção 5, ampliado pela decisão 46 · "Encerrar"
  // v2): congela o resumo do trecho inteiro, fecha a conversa de verdade (RN
  // do cabeçalho: sai de "Precisa de você", fica em "Encerrado") e, se ela
  // marcou "guardar nas notas", grava a anotação também no cadastro — mesmo
  // formato de SALVAR_NOTA (reducer.js), duplicado aqui pelo mesmo motivo de
  // `comMensagemLocal` em casos/simulacao.js: nenhuma frente lê de dentro do
  // reducer.
  //
  // Mensagem de encerramento, e-mail e SMS (decisão 46): a modal já decidiu
  // o quê (texto editado, avisos ligados no cadastro); aqui só entra no fio
  // ANTES do resumo ser calculado, porque são parte DESTE atendimento, não
  // do próximo — mesma ordem que o agradecimento pós-entrega já usa.
  //
  // Número do atendimento (seção 5, "Nº AT-0412"): sequencial simples,
  // contando quantos resumos já existem em QUALQUER conversa — não é um
  // registro fiscal, é só o carimbo que a folha impressa mostra de relance.
  [acao.ENCERRAR_COM_RESUMO]: (estado, {
    id, agora, enviarMensagem = false, textoMensagem = '', avisoEmail = false, avisoSms = false,
  }) => {
    const conversaOriginal = estado.conversas.find((c) => c.id === id)
    if (!conversaOriginal) return estado

    const textoLimpo = textoMensagem.trim()
    const mensagemEnviada = Boolean(enviarMensagem && textoLimpo)
    const registros = []
    if (mensagemEnviada) {
      registros.push({
        dir: 'out', status: 'lida', automatica: true, regra: 'encerramento', texto: textoLimpo,
      })
    }
    if (avisoEmail) registros.push({ dir: 'sistema', texto: 'E-mail de encerramento enviado (simulado).' })
    if (avisoSms) registros.push({ dir: 'sistema', texto: 'SMS de encerramento enviado (simulado).' })

    const comRegistros = registros.length === 0 ? conversaOriginal : {
      ...conversaOriginal,
      ultimaEm: mensagemEnviada ? new Date(agora).toISOString() : conversaOriginal.ultimaEm,
      mensagens: [
        ...conversaOriginal.mensagens,
        ...registros.map((r, i) => ({ id: `encerramento-${id}-${agora}-${i}`, em: new Date(agora).toISOString(), ...r })),
      ],
    }

    const rascunho = rascunhoDe(comRegistros)
    const totalAntes = estado.conversas.reduce((soma, c) => soma + (c.atendimentos?.length ?? 0), 0)
    const numero = `AT-${String(totalAntes + 1).padStart(4, '0')}`
    const resumo = {
      numero,
      ...montarResumoAtendimento(comRegistros, estado.catalogo.cardapio, agora, rascunho),
      encerradoPor: ATENDENTE,
      encerradoEm: agora,
      // Fronteira por índice, não por horário (ver dominio/conversa.js,
      // `atendimentoDesde`): o relógio simulado anda em passos, e a mensagem
      // que reabre pode nascer no mesmo instante gravado deste encerramento.
      ateIndice: comRegistros.mensagens.length - 1,
      desfecho: 'manual',
      mensagemEnviada,
      avisoEmailEnviado: Boolean(avisoEmail),
      avisoSmsEnviado: Boolean(avisoSms),
    }
    const anotacaoLimpa = rascunho.anotacao.trim()
    const notaNova = rascunho.guardarNota && anotacaoLimpa
      ? [{ id: `nota-fechamento-${numero}`, autor: ATENDENTE, em: dataHora(agora), texto: anotacaoLimpa }]
      : []
    return mapear(estado, id, () => ({
      ...comRegistros,
      estado: 'Encerrado',
      // Resposta automática ainda "digitando" (registro 92) é deste
      // atendimento: encerrar descarta a fila, como Assumir já faz, para nada
      // sair em nome da casa depois do fechamento (registro 101).
      respostaPendente: [],
      atendimentos: [...(comRegistros.atendimentos ?? []), resumo],
      fechamentoRascunho: RASCUNHO_PADRAO,
      cliente: notaNova.length ? { ...comRegistros.cliente, notas: [...notaNova, ...comRegistros.cliente.notas] } : comRegistros.cliente,
    }))
  },

  // Encerramento automático por janela (decisão 46): o Provider já filtrou
  // QUAIS conversas têm a janela fechada (`conversasParaEncerrarPorJanela`,
  // dominio/janela.js); aqui só fecha cada uma, sem mensagem nenhuma (a
  // direção pede silêncio: "sem mandar mensagem") e com o desfecho que
  // identifica a causa no log. Mesmo carimbo de número sequencial do
  // encerramento manual, um por conversa da lista.
  [acao.ENCERRAR_POR_JANELA]: (estado, { ids, agora }) => ids.reduce((acumulado, id) => {
    const conversa = acumulado.conversas.find((c) => c.id === id)
    if (!conversa || conversa.estado === 'Encerrado') return acumulado
    const rascunho = rascunhoDe(conversa)
    const totalAntes = acumulado.conversas.reduce((soma, c) => soma + (c.atendimentos?.length ?? 0), 0)
    const numero = `AT-${String(totalAntes + 1).padStart(4, '0')}`
    const resumo = {
      numero,
      ...montarResumoAtendimento(conversa, acumulado.catalogo.cardapio, agora, rascunho),
      encerradoPor: 'Sistema, por tempo',
      encerradoEm: agora,
      ateIndice: conversa.mensagens.length - 1,
      desfecho: 'tempo',
      mensagemEnviada: false,
      avisoEmailEnviado: false,
      avisoSmsEnviado: false,
    }
    return mapear(acumulado, id, (c) => ({
      ...c,
      estado: 'Encerrado',
      respostaPendente: [], // mesma regra do encerramento manual (registro 101)
      atendimentos: [...(c.atendimentos ?? []), resumo],
      fechamentoRascunho: RASCUNHO_PADRAO,
    }))
  }, estado),
}
