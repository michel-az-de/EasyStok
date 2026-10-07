// Ações que o "Pergunte ao assistente" propõe (#1445, homologação de 07/10: "se eu falar
// para ele mandar o cardápio ou anotar alguma coisa aqui, ele não está fazendo").
// O EasyStok devolve as propostas (tool use do modelo); aqui elas viram cartão com um
// botão, e só o clique da atendente executa, pelas mesmas ações que a tela já usa.
// Nada sai ao cliente sem esse clique. Puro: sem React e sem infra; quem faz o efeito
// chega injetado em `executarAcaoDoAssistente`.
import { primeiroNome } from './mensagem.js'
import { textoConviteCardapio } from './cardapioLink.js'

export const ACOES_DO_ASSISTENTE = {
  ENVIAR_CARDAPIO: 'enviar_cardapio',
  NOTA_INTERNA: 'nota_interna',
  RASCUNHO: 'rascunho',
  ABRIR_TELA: 'abrir_tela',
}

const TELAS = ['cardapio', 'comanda']
const COM_TEXTO = [ACOES_DO_ASSISTENTE.NOTA_INTERNA, ACOES_DO_ASSISTENTE.RASCUNHO]

const textoLimpo = (valor) => (typeof valor === 'string' ? valor.trim() : '')

function acaoValida(bruta) {
  const tipo = bruta?.tipo
  if (!Object.values(ACOES_DO_ASSISTENTE).includes(tipo)) return null
  const texto = textoLimpo(bruta.texto)
  if (COM_TEXTO.includes(tipo) && !texto) return null
  if (tipo === ACOES_DO_ASSISTENTE.ABRIR_TELA && !TELAS.includes(bruta.tela)) return null
  const tela = tipo === ACOES_DO_ASSISTENTE.ABRIR_TELA ? bruta.tela : null
  return {
    chave: [tipo, tela ?? '', texto].join('|'),
    tipo,
    texto: COM_TEXTO.includes(tipo) ? texto : null,
    tela,
    estado: 'proposta',
    erro: null,
  }
}

// O modo local (servidor do agente) ainda responde só texto; o modo API traz `acoes`.
// Tipo desconhecido, texto vazio e tela fora da lista caem fora; repetida vale uma vez.
export function respostaDoAssistente(resposta) {
  if (typeof resposta === 'string') return { texto: resposta, acoes: [] }
  const acoes = []
  for (const bruta of Array.isArray(resposta?.acoes) ? resposta.acoes : []) {
    const acao = acaoValida(bruta)
    if (acao && !acoes.some((a) => a.chave === acao.chave)) acoes.push(acao)
  }
  return { texto: textoLimpo(resposta?.texto), acoes }
}

// Rótulos do cartão. `saiParaCliente` marca o único botão que manda algo ao cliente.
export function descreverAcaoDoAssistente(acao, nomeCliente) {
  const nome = nomeCliente ? primeiroNome(nomeCliente) : 'o cliente'
  switch (acao.tipo) {
    case ACOES_DO_ASSISTENTE.ENVIAR_CARDAPIO:
      return { titulo: 'Mandar o cardápio', detalhe: `Link do cardápio para ${nome}.`, botao: 'Enviar ao cliente', feito: 'Cardápio enviado', saiParaCliente: true }
    case ACOES_DO_ASSISTENTE.NOTA_INTERNA:
      return { titulo: 'Nota interna', detalhe: acao.texto, botao: 'Salvar nota', feito: 'Nota salva', saiParaCliente: false }
    case ACOES_DO_ASSISTENTE.RASCUNHO:
      return { titulo: 'Resposta sugerida', detalhe: acao.texto, botao: 'Usar no rascunho', feito: 'No rascunho', saiParaCliente: false }
    case ACOES_DO_ASSISTENTE.ABRIR_TELA:
      return acao.tela === 'comanda'
        ? { titulo: 'Comanda', detalhe: null, botao: 'Ver comanda', feito: 'Aberta', saiParaCliente: false }
        : { titulo: 'Cardápio', detalhe: null, botao: 'Abrir cardápio', feito: 'Aberto', saiParaCliente: false }
    default:
      return null
  }
}

// Executa a ação confirmada pela atendente. `portas` são as ações da tela:
// { obterLinkCardapio, enviar, salvarNota, definirRascunho, abrirTela }.
// O cardápio sai com o mesmo convite e o mesmo link do botão do compositor (#1353).
export async function executarAcaoDoAssistente(acao, conversa, portas) {
  switch (acao.tipo) {
    case ACOES_DO_ASSISTENTE.ENVIAR_CARDAPIO: {
      const link = await portas.obterLinkCardapio(conversa.id)
      if (!link?.url) throw new Error('Não consegui gerar o link do cardápio.')
      await portas.enviar(conversa.id, textoConviteCardapio(conversa.nome, link.url))
      return
    }
    case ACOES_DO_ASSISTENTE.NOTA_INTERNA:
      await portas.salvarNota(conversa.id, acao.texto)
      return
    case ACOES_DO_ASSISTENTE.RASCUNHO:
      portas.definirRascunho(conversa.id, acao.texto)
      return
    case ACOES_DO_ASSISTENTE.ABRIR_TELA:
      portas.abrirTela(acao.tela)
      return
    default:
      throw new Error('Ação desconhecida.')
  }
}
