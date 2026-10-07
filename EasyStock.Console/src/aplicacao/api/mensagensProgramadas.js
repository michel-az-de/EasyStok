import {
  agendarMensagem, cancelarMensagemProgramada, listarMensagensProgramadas,
} from '../../infra/api/mensagensProgramadasApi'
import { canalParaApi, programadaDaApi } from '../../infra/api/traducaoConversas'

// Programar mensagem ao cliente no modo API (#1424, S39). O EasyStok agenda e dispara
// (MensagensProgramadasBackgroundService); o console não guarda nada na memória do navegador.
// As três devolvem a Promise: o erro da API (400 de janela, consentimento, sem telefone,
// horário no passado) chega à modal com a mensagem dela, que fica na tela.
export const SEM_CLIENTE = 'Cadastre o cliente desta conversa antes de programar: a mensagem programada vai para um cliente.'

// Texto ou modelo, nunca os dois (a API recusa). `agendadaPara` já chega em ISO UTC.
export function corpoDaProgramada(conversa, { finalidade, texto, modelo, agendadaPara }) {
  return {
    clienteId: conversa.clienteId,
    conversaId: conversa.id,
    canal: canalParaApi(conversa.canal),
    finalidade,
    ...(modelo
      ? { modelo: { nome: modelo.nome.trim(), idioma: modelo.idioma?.trim() || 'pt_BR', parametros: modelo.parametros ?? [] } }
      : { texto: texto.trim() }),
    agendadaPara,
  }
}

// A API lista por cliente; a conversa mostra as dela e as do mesmo canal agendadas sem conversa.
export const daConversa = (conversa) => (p) =>
  p.conversaId === conversa.id || (!p.conversaId && p.canal === conversa.canal)

export function criarAcoesMensagensProgramadasApi({ estadoRef }) {
  const conversaDe = (id) => estadoRef.current.conversas.find((c) => c.id === id) ?? null
  const exigirCliente = (id) => {
    const conversa = conversaDe(id)
    if (!conversa?.clienteId) throw new Error(SEM_CLIENTE)
    return conversa
  }

  return {
    programarMensagem: async (id, dados) =>
      programadaDaApi(await agendarMensagem(corpoDaProgramada(exigirCliente(id), dados))),
    listarProgramadas: async (id) => {
      const conversa = exigirCliente(id)
      const linhas = (await listarMensagensProgramadas({ clienteId: conversa.clienteId })) ?? []
      return linhas.map(programadaDaApi).filter(daConversa(conversa))
    },
    cancelarProgramada: async (programadaId) => programadaDaApi(await cancelarMensagemProgramada(programadaId)),
  }
}
