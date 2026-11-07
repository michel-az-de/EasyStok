// Apresentação do cartão do Balcão (#1868). Puro e sem JSX, para a prova
// (`ferramentas/prova-1868-visual.mjs`) conferir a mesma regra que a tela usa.
//
// Medido no laboratório: "Visitante do site" era o nome em 37 de 39 cartões e a
// terceira linha repetia "Aguardando equipe" e "Chat do site" em 38 de 39. O que
// todo cartão tem não ajuda a escolher: o canal já está no selo do avatar e o
// "Precisa de você" no trilho lateral do cartão e no chip do filtro.

import { origemDaPassagem } from '../../dominio/passagem'
import { sinalVerde } from '../../dominio/cobranca'
import { faltaPagar } from '../../dominio/pagamento'
import { ehLead } from '../../dominio/conversa'
import { passoPorId } from '../../dominio/esteira'
import { moeda } from '../../dominio/formato'
import { apresentacaoDoNome, nomeDaConversa } from '../../dominio/nomeDoContato'

export const NOME_DO_VISITANTE = 'Visitante do site'

// Nome do cartão (#2258, T4 e T5). "Visitante do site" vira "Visitante" com a comanda numa cápsula
// ("#16"), a mesma do cabeçalho da conversa e da ficha; quem tem nome mantém "· Comanda N" ao lado.
// Telefone cru no lugar do nome sai com a máscara (#2232, L10) e a cápsula "Sem nome". O nome
// inteiro segue no `title`, e `curto` é a forma que a tela usa quando o espaço aperta.
export function nomeDoCartao(conversa) {
  const apresentacao = apresentacaoDoNome(nomeDaConversa(conversa), { comanda: conversa?.numeroComanda })
  const comanda = conversa?.numeroComanda ? `Comanda ${conversa.numeroComanda}` : null
  const visitante = apresentacao.completo === 'Visitante'
  const titulo = visitante || !comanda ? apresentacao.titulo : `${apresentacao.titulo} · ${comanda}`
  return {
    principal: apresentacao.completo,
    curto: apresentacao.curto,
    capsula: apresentacao.capsula,
    complemento: visitante ? null : comanda,
    titulo,
  }
}

// Motivo de precisar no cartão (#2258, T6): o rótulo cabe em 18 caracteres; a frase inteira vai no
// `title`. Mora aqui, e não no domínio, porque o texto de linha é decisão do cartão.
const MOTIVO_NO_CARTAO = {
  reclamacao: { texto: 'Reclamação', icone: 'message-square-warning' },
  passagem: { texto: 'Passou para você', icone: 'hand' },
  esperando: { texto: 'Esperando você', icone: 'relogio' },
  cobranca: { texto: 'Cobrança venceu', icone: 'dollar-sign' },
  // #1474 (R12): o cliente escreveu fora do horário; "Loja abriu" parecia aviso da loja.
  'aguardando-abertura': { texto: 'Loja fechada', icone: 'relogio', titulo: 'Escreveu com a loja fechada' },
  // Rodada 10, itens 3 e 4: pedido atrasado (ainda na cozinha) e entrega atrasada (já em rota).
  'pedido-atrasado': { texto: 'Pedido atrasado', icone: 'cooking-pot' },
  'atraso-entrega': { texto: 'Entrega atrasada', icone: 'moto' },
}

export function motivoResumido(chave, origem = null) {
  const apresentacao = chave === 'passagem' && origem === 'chat-site'
    ? { texto: 'Aguardando equipe', icone: 'relogio' }
    : MOTIVO_NO_CARTAO[chave]
  if (!apresentacao) return null
  return { texto: apresentacao.texto, icone: apresentacao.icone, titulo: apresentacao.titulo ?? apresentacao.texto }
}

// Origem da passagem no contexto do cartão. "Chat do site" repete o selo do
// canal no avatar; as outras origens dizem o porquê e ficam.
export function origemNoCartao(passagem) {
  if (!passagem || passagem.origem === 'chat-site') return null
  return origemDaPassagem(passagem)
}

// A etiqueta do estado sai quando não ajuda a escolher o cartão: na passagem ("Aguardando
// equipe", "Passou para você") o cartão ganha o trilho lateral de atenção; em "Automático" e
// "Com você" (#1899: eram 14 de 16 cartões) quem conduz já está na pílula da BarraModo. Nos
// três casos o texto vai para o leitor de tela. Bloqueado, Encerrada, Outro atendente e os
// motivos (reclamação, cobrança venceu, pedido atrasado) continuam escritos.
const ESTADOS_SEM_ETIQUETA = ['passagem', 'automatico', 'voce']
export function mostraEtiquetaDoEstado(estado) {
  return !ESTADOS_SEM_ETIQUETA.includes(estado?.chave)
}

// #2232 (banca UX, X1, WCAG 4.1.2): nome acessível do item da lista numa frase só, na ordem em
// que se decide: quem, quanto espera, por quê, quando. Antes o leitor de tela lia o botão inteiro,
// prévia incluída.
export function rotuloDoCartao({ nome, estado = null, pendentes = 0, motivo = null, contexto = [], atrasada = false, pagamentoNaoVisto = false, hora, selecionada = false }) {
  return [
    nome,
    estado,
    pendentes > 0 ? (pendentes === 1 ? '1 mensagem sem resposta' : `${pendentes} mensagens sem resposta`) : null,
    motivo,
    ...contexto,
    atrasada ? 'prazo de resposta estourado' : null,
    pagamentoNaoVisto ? 'pagamento novo' : null,
    hora,
    selecionada ? 'conversa aberta' : null,
  ].filter(Boolean).join(', ')
}

// A terceira linha só existe se sobrar o que dizer nela.
export function temLinhaDeContexto({ etiqueta, contexto }) {
  return Boolean(etiqueta) || (contexto?.length ?? 0) > 0
}

const ICONE_DO_PASSO = {
  aguardando: 'dollar-sign', pago: 'circle-check', preparo: 'cooking-pot',
  embalado: 'package', entrega: 'moto', entregue: 'house',
}

export const MARCA_EM_MONTAGEM = { texto: 'Em montagem', icone: 'shopping-cart', tom: 'neutro' }

// Passo do pedido no contexto da linha (com o sinal verde do pagamento, RN-25,
// dez minutos só) ou "Lead" quando ainda não há pedido nem cadastro.
// #1940 (item 2): no modo API a comanda em montagem ainda não tem frete (só o servidor calcula, ao
// gerar a cobrança): "Falta R$ 38,00" no cartão contra R$ 50,00 na Ficha. Sem pedido criado, o
// cartão diz que está em montagem, sem valor.
export function marcaDoPedido(conversa, cardapio, { fonteApi = false } = {}) {
  const passo = conversa.pedido ? passoPorId(conversa.pedido.estado) : null
  if (passo) {
    if (fonteApi && !conversa.pedido.pedidoId) return MARCA_EM_MONTAGEM
    const saldo = faltaPagar(conversa.pedido, cardapio)
    if (saldo > 0 && conversa.pedido.sinalNaReserva && conversa.pedido.cobranca?.pagaEm
      && Math.round(conversa.pedido.cobranca.valor * 100) === Math.round((conversa.pedido.totalApi - saldo) * 100)) {
      return { texto: 'Sinal pago', icone: 'circle-check', tom: 'ok' }
    }
    if (saldo > 0) return { texto: `Falta ${moeda(saldo)}`, icone: 'dollar-sign', tom: 'neutro' }
    if (sinalVerde(conversa.pedido)) return { texto: 'Pago', icone: 'circle-check', tom: 'ok' }
    return { texto: passo.rotulo, icone: ICONE_DO_PASSO[passo.id], tom: 'neutro' }
  }
  if (ehLead(conversa)) return { texto: 'Contato', icone: 'user-plus', tom: 'marca' }
  return null
}
