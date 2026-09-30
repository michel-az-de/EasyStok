// Tradução do formato da EasyStock.Api (`ConversaResumoResult`, `MensagemAtendimentoResult`)
// para o formato de conversa que as telas do console já conhecem (ver conversasSemente.js).
// O que a API ainda não entrega (pedido, notas, tags, endereço) nasce vazio: cada módulo
// ganha o dado de verdade quando for ligado (matriz 10-console.md).

const NOME_DO_CANAL = {
  WhatsApp: 'WhatsApp', Instagram: 'Instagram', Messenger: 'Messenger',
  ChatSite: 'Chat do site', Email: 'E-mail', Sms: 'SMS',
}

const ESTADO_DA_SITUACAO = { Automatica: 'Aberto', Assumida: 'Em atendimento', Encerrada: 'Encerrado' }

const STATUS_DA_MENSAGEM = {
  Pendente: 'enviando', Enviada: 'enviada', Entregue: 'lida', Lida: 'lida', Falhou: 'falhou',
}

const ROTULO_DO_CONTEUDO = {
  Imagem: 'Foto', Audio: 'Áudio', Documento: 'Arquivo', Localizacao: 'Localização', Outro: 'Mensagem',
}

const HORAS_DA_JANELA = 24
const MS_POR_HORA = 3600000

// DateTime sem fuso vem da API em UTC; sem o Z o navegador leria como hora local.
const instante = (valor) => {
  if (!valor) return null
  return /[zZ]|[+-]\d{2}:\d{2}$/.test(valor) ? valor : `${valor}Z`
}

export function mensagemDaApi(m) {
  const saida = m.direcao === 'Saida'
  return {
    id: m.id,
    dir: saida ? 'out' : 'in',
    texto: m.texto || ROTULO_DO_CONTEUDO[m.tipoConteudo] || '',
    em: instante(m.enviadaEm),
    ...(saida ? { status: STATUS_DA_MENSAGEM[m.status] ?? 'enviada' } : {}),
    ...(saida && m.autor === 'Agente' ? { automatica: true } : {}),
    ...(m.erro ? { erro: m.erro } : {}),
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

export function conversaDaApi(resumo, mensagensDaApi, usuario) {
  const mensagens = (mensagensDaApi ?? []).map(mensagemDaApi).sort(cronologica)
  const minha = resumo.assumidaPorUsuarioId && resumo.assumidaPorUsuarioId === usuario?.id
  return {
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
    janelaExpiraEm: janelaExpiraEm(resumo, mensagens),
    ultimaEm: instante(resumo.ultimaMensagemEm),
    naoLidas: resumo.naoLidas,
    atrasada: false,
    cliente: { desde: null, endereco: null, enderecoCapturado: null, pedidos: 0, tags: [], notas: [] },
    pedido: null,
    mensagens,
  }
}
