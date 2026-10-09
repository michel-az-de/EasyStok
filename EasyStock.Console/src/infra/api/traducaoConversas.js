// Tradução do formato da EasyStock.Api (`ConversaResumoResult`, `MensagemAtendimentoResult`)
// para o formato de conversa que as telas do console já conhecem (ver conversasSemente.js).
// O que a API ainda não entrega (pedido, notas, tags, endereço) nasce vazio: cada módulo
// ganha o dado de verdade quando for ligado (matriz 10-console.md).

import { pausaDaSituacao, respostaAtrasada } from '../../dominio/automatico'
import { ACOES } from '../../dominio/agente'

const NOME_DO_CANAL = {
  WhatsApp: 'WhatsApp', Instagram: 'Instagram', Messenger: 'Messenger',
  ChatSite: 'Chat do site', Email: 'E-mail', Sms: 'SMS',
}

// Caminho de volta (#1424): o nome do canal na tela vira o enum da API (`CanalConversa`, string).
const CANAL_DO_NOME = Object.fromEntries(Object.entries(NOME_DO_CANAL).map(([api, nome]) => [nome, api]))
export const canalParaApi = (nome) => CANAL_DO_NOME[nome] ?? nome

const ESTADO_DA_SITUACAO = { Automatica: 'Aberto', Assumida: 'Em atendimento', Encerrada: 'Encerrado' }

const STATUS_DA_MENSAGEM = {
  Pendente: 'enviando', Enviada: 'enviada', Entregue: 'entregue', Lida: 'lida', Falhou: 'falhou',
}

const ROTULO_DO_CONTEUDO = {
  Imagem: 'Foto', Audio: 'Áudio', Documento: 'Arquivo', Localizacao: 'Localização', Outro: 'Mensagem',
}

const HORAS_DA_JANELA = 24
const MS_POR_HORA = 3600000

// DateTime sem fuso vem da API em UTC; sem o Z o navegador leria como hora local.
export const instante = (valor) => {
  if (!valor) return null
  return /[zZ]|[+-]\d{2}:\d{2}$/.test(valor) ? valor : `${valor}Z`
}

// `conversaId` vem da listagem: com ele, a mensagem com arquivo leva o endereço para o balão
// buscar a mídia no endpoint autenticado (#1287). Sem ele (confirmação de envio), só o rótulo.
export function mensagemDaApi(m, conversaId = null) {
  const saida = m.direcao === 'Saida'
  // #1474: nota interna do sistema (ex.: escalada para a dona) nunca vai ao canal: sem id externo
  // e Pendente para sempre. Vira evento na conversa, não balão "automática · enviando".
  if (saida && m.autor === 'Sistema' && !m.externoId && m.status === 'Pendente') {
    return { id: m.id, dir: 'sistema', texto: m.texto || '', em: instante(m.enviadaEm) }
  }
  return {
    id: m.id,
    dir: saida ? 'out' : 'in',
    texto: m.texto || ROTULO_DO_CONTEUDO[m.tipoConteudo] || '',
    em: instante(m.enviadaEm),
    ...(saida ? { status: STATUS_DA_MENSAGEM[m.status] ?? 'enviada' } : {}),
    ...(saida && m.autor === 'Agente' ? { automatica: true, origemAutomatica: 'agente' } : {}),
    // #1441: automáticas por gatilho (S42) e avisos do sistema também saem sem a dona.
    ...(saida && m.autor === 'Sistema' ? { automatica: true, origemAutomatica: 'sistema' } : {}),
    // #1424: saiu pelo disparo da mensagem programada; o balão mostra o selo.
    ...(saida && m.programada ? { programada: true } : {}),
    ...(m.erro ? { erro: m.erro } : {}),
    // S58/S60 (#1391): espera o cliente responder ao modelo de retomada; ou saiu pela reserva por SMS.
    ...(m.aguardaClienteDesde ? { aguardaCliente: true } : {}),
    ...(m.reservaSmsEm ? { porSms: true } : {}),
    // #1398: texto do áudio transcrito, mostrado abaixo do player.
    ...(m.transcricao ? { transcricao: m.transcricao } : {}),
    ...(m.midiaChave && conversaId ? { midia: { conversaId, mensagemId: m.id, mime: m.midiaMime ?? null } } : {}),
    // #1397: o EasyStok desistiu de baixar o anexo depois das tentativas; o balão avisa em vez de ficar parado.
    ...(!m.midiaChave && m.midiaFalhou ? { midia: { falhou: true, erro: m.erroMidia ?? null } } : {}),
  }
}

const cronologica = (a, b) => new Date(a.em) - new Date(b.em)

// Fim da janela de 24 h: última mensagem do cliente + 24 h. Sem mensagens carregadas,
// só o `dentroDaJanela` da API; aí a última mensagem da conversa é a melhor aproximação.
function janelaExpiraEm(resumo, mensagens) {
  const ultimaDoCliente = mensagens.filter((m) => m.dir === 'in').at(-1)
  if (ultimaDoCliente) return new Date(new Date(ultimaDoCliente.em).getTime() + HORAS_DA_JANELA * MS_POR_HORA).toISOString()
  if (mensagens.length > 0) return null
  const ultima = new Date(instante(resumo.ultimaMensagemEm)).getTime()
  return resumo.dentroDaJanela ? new Date(ultima + HORAS_DA_JANELA * MS_POR_HORA).toISOString() : new Date(ultima).toISOString()
}

// `Assumida` sem responsável: o automático passou a conversa para a fila humana. Vira a
// mesma `passagem` do protótipo (selo "Precisa de você" com o motivo). `motivoEscalada` é
// opcional (F08); sem ele, o motivo padrão. A API não dá a hora da escalada.
function passagemDaApi(resumo) {
  if (resumo.situacao !== 'Assumida' || resumo.assumidaPorUsuarioId) return null
  return { motivo: resumo.motivoEscalada || null, em: null, assumida: false }
}

// `agora` só pesa em `atrasada` (#1427): retrato do SLA na sincronização (a
// cada 5 s), no relógio inteiro. O cartão recalcula a cada tique com o
// expediente da loja (`respostaAtrasada`, dominio/automatico.js).
export function conversaDaApi(resumo, mensagensDaApi, usuario, agora = Date.now()) {
  const mensagens = (mensagensDaApi ?? []).map((m) => mensagemDaApi(m, resumo.id)).sort(cronologica)
  const minha = resumo.assumidaPorUsuarioId && resumo.assumidaPorUsuarioId === usuario?.id
  const conversa = {
    id: resumo.id,
    conta: resumo.clienteId ? 'cliente' : 'lead',
    // Chave do cadastro na API: consentimento (S38) e o que mais for por cliente.
    clienteId: resumo.clienteId ?? null,
    nome: resumo.contatoNome || resumo.contatoIdExterno,
    contato: resumo.contatoIdExterno,
    canal: NOME_DO_CANAL[resumo.canal] ?? resumo.canal,
    estado: ESTADO_DA_SITUACAO[resumo.situacao] ?? 'Aberto',
    situacaoApi: resumo.situacao,
    responsavel: resumo.assumidaPorUsuarioId ? (minha ? usuario.nome : 'Outro atendente') : null,
    // F07, item 3: quem pausou o automático (você, outro atendente ou a escalada) e a passagem.
    pausaApi: pausaDaSituacao(resumo, usuario?.id ?? null),
    passagem: passagemDaApi(resumo),
    janelaExpiraEm: janelaExpiraEm(resumo, mensagens),
    ultimaEm: instante(resumo.ultimaMensagemEm),
    // Prévia do cartão quando as mensagens não foram carregadas (encerrada, #1287).
    ultimaMensagemTexto: resumo.ultimaMensagemTexto ?? null,
    naoLidas: resumo.naoLidas,
    // #1427: SLA de primeira resposta da loja e de onde ele conta.
    slaMinutos: resumo.slaRespostaMinutos ?? null,
    aguardaResposta: resumo.aguardaResposta ?? false,
    ultimaEntradaEm: resumo.ultimaMensagemEntradaEm ? instante(resumo.ultimaMensagemEntradaEm) : null,
    cliente: { desde: null, endereco: null, enderecoCapturado: null, pedidos: 0, tags: [], notas: [] },
    pedido: null,
    mensagens,
  }
  return { ...conversa, atrasada: respostaAtrasada(conversa, agora, Boolean(conversa.pausaApi)) }
}

// Linha do painel "Não entregues" (S59): quem devia receber e a mensagem no formato do balão.
export const naoEntregueDaApi = (l) => ({
  conversaId: l.conversaId,
  contato: l.contatoNome || l.contatoIdExterno,
  canal: l.canal,
  aberta: l.conversaAberta,
  mensagem: mensagemDaApi(l.mensagem, l.conversaId),
})

// Mensagem programada (#1424, `MensagemProgramadaResult`) no formato da lista do console.
// `situacao` segue o enum da API (Agendada, Enviando, Enviada, Cancelada, Falhou).
export const programadaDaApi = (r) => ({
  id: r.id,
  clienteId: r.clienteId,
  conversaId: r.conversaId ?? null,
  canal: NOME_DO_CANAL[r.canal] ?? r.canal,
  finalidade: r.finalidade,
  texto: r.texto ?? null,
  modelo: r.modelo ?? null,
  agendadaPara: instante(r.agendadaPara),
  situacao: r.situacao,
  tentativas: r.tentativas ?? 0,
  erro: r.erro ?? null,
  enviadaEm: instante(r.enviadaEm),
})

// Sugestão do agente (#1420) no formato que o painel do agente lê. O agente do EasyStok escreve a
// mensagem; quem decide se passa para a dona é ele no automático, então aqui a ação é sempre propor.
export const sugestaoDaApi = (r, recebidoEm = Date.now()) => ({
  modo: 'api',
  modelo: 'agente do EasyStok',
  intencao: { chave: 'sugestao', rotulo: 'Sugestão do agente' },
  confianca: null,
  acao: ACOES.PROPOR,
  texto: r?.texto ?? '',
  estruturada: true,
  prompt: '',
  recebidoEm,
  latenciaMs: r?.latenciaMs ?? null,
  tokensEstimados: null,
  tokensMedidos: r?.tokens ?? null,
  custoUsd: null,
  transporte: 'easystok',
})
