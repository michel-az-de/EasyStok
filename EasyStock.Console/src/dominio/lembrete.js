// Lembrete: o que não pode escapar enquanto ela cozinha. É o canhoto de papel
// no alfinete do áudio 06, em tela. Tarefa da dona, com hora, que ninguém
// manda para fora.
//
// Puro. O relógio chega por parâmetro e nada aqui olha Date.now(), senão o
// relógio da tela e o do lembrete andariam separados.

import { mensagensSemRespostaReal, primeiroNome } from './mensagem'
import { origemDaPassagem } from './passagem'

export const TIPOS = { DONA: 'dona' }

export const ORIGENS = { MANUAL: 'manual', PASSAGEM: 'passagem', PAGAMENTO: 'pagamento' }

// As notificações InApp ficam no sino transversal. Aqui só entram lembretes.
export const FONTES = { LEMBRETE: 'lembrete' }

export const MINUTOS_SEM_RESPOSTA = 10
export const MINUTOS_SEM_PAGAMENTO = 15

export const novoLembrete = ({
  id = null, tipo = TIPOS.DONA, titulo, detalhe = null,
  quando, conversaId = null, origem = ORIGENS.MANUAL,
}) => ({ id, tipo, titulo, detalhe, quando, conversaId, origem })

export const vencido = (lembrete, agora) => lembrete.quando <= agora

export const contarVencidos = (lembretes, agora) =>
  (lembretes ?? []).filter((l) => vencido(l, agora)).length

export const porHora = (lembretes) => [...lembretes].sort((a, b) => a.quando - b.quando)

export function descreverPrazo(quando, agora) {
  const minutos = Math.round((quando - agora) / 60000)
  if (minutos < 0) {
    const atraso = -minutos
    if (atraso < 60) return `venceu há ${atraso} min`
    const horas = Math.round(atraso / 60)
    return horas < 24 ? `venceu há ${horas} h` : `venceu há ${Math.round(horas / 24)} d`
  }
  if (minutos === 0) return 'agora'
  if (minutos < 60) return `em ${minutos} min`
  const horas = Math.round(minutos / 60)
  if (horas < 24) return `em ${horas} h`
  return new Date(quando).toLocaleString('pt-BR', {
    day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit',
  })
}

// Igual ao ramo "venceu há" de descreverPrazo, mas sem o verbo: o sininho
// (seção 8) já diz "vencido" pela seção onde o item está, o texto só precisa
// do tempo. Só chamado com lembrete vencido, então nunca dá tempo negativo.
export function descreverAtraso(quando, agora) {
  const atraso = Math.max(0, Math.round((agora - quando) / 60000))
  if (atraso < 60) return `há ${atraso} min`
  const horas = Math.round(atraso / 60)
  return horas < 24 ? `há ${horas} h` : `há ${Math.round(horas / 24)} d`
}

// Chave de "visto" (seção 8): id + quando, não só id. Um lembrete automático
// (id fixo) que a sincronização atualiza não ressuscita como visto por
// engano, e um lembrete adiado volta como novo no horário novo.
export const chaveLembrete = (lembrete) => `${lembrete.id}:${lembrete.quando}`

// Número curto do pedido (seção 2 do glossário: quatro dígitos em toda a
// tela). Não fica em dominio/formato.js de propósito: é só para este detalhe,
// e um nome igual lá colidiria com a Frente A mexendo na mesma tela.
const numeroCurto = (numero) => String(numero).split('-').at(-1)

// Passou para a dona e o cliente continua falando por último: o prazo é dez
// minutos depois da última fala dele. O lembrete nasce junto com a passagem e
// só fica em destaque quando o prazo vence, pelo relógio da tela.
//
// `mensagensSemRespostaReal` (rodada 13, issue #41) em vez da última mensagem
// crua: aviso de esteira que sai sozinho enquanto a passagem espera (RN-05)
// virava a última mensagem da lista e apagava o lembrete de "Responder Fulano"
// mesmo com a pergunta dele ainda sem resposta.
function prazoDaPassagem(conversa) {
  if (!conversa.passagem) return null
  const fim = mensagensSemRespostaReal(conversa).at(-1)
  if (!fim) return null
  return new Date(fim.em).getTime() + MINUTOS_SEM_RESPOSTA * 60000
}

// Quando a cobrança saiu. Sem a marca da regra, vale a última mensagem da casa,
// que é onde a comanda e o link apareceram. Conversa sem nada disso usa agora.
function instanteDaCobranca(conversa, agora) {
  const mensagens = conversa.mensagens ?? []
  const daCasa = [...mensagens].reverse()
  const marcada = daCasa.find((m) => m.regra === 'cobranca' || m.regra === 'resumo')
  // Com cobrança emitida, o relógio é o dela; sem cobrança nem comanda
  // enviada, não há prazo nenhum correndo (integração: comanda recém-aberta
  // aparecia como "Cobrança venceu" contando da última mensagem da casa).
  if (!conversa.pedido?.cobranca && !marcada) return null
  const referencia = marcada ?? daCasa.find((m) => m.dir === 'out')
  return referencia ? new Date(referencia.em).getTime() : agora
}

// Exportado porque "cobranca vencida" no balcao e a MESMA data do lembrete de
// pagamento. Duas contas para o mesmo prazo divergem no dia em que alguem mexe
// so numa delas.
export function prazoDoPagamento(conversa, agora) {
  const pedido = conversa.pedido
  if (!pedido || pedido.estado !== 'aguardando') return null
  if ((pedido.itens?.length ?? 0) === 0) return null
  const instante = instanteDaCobranca(conversa, agora)
  return instante == null ? null : instante + MINUTOS_SEM_PAGAMENTO * 60000
}

// Os lembretes que DEVEM existir agora, olhando só o estado das conversas. O id
// é determinístico, então sincronizar duas vezes não duplica nada.
export function lembretesAutomaticos(conversas, agora) {
  const lista = []
  for (const conversa of conversas ?? []) {
    const nome = primeiroNome(conversa.nome)

    const passagem = prazoDaPassagem(conversa)
    if (passagem != null) {
      lista.push(novoLembrete({
        id: `auto-passagem-${conversa.id}`,
        titulo: `Responder ${nome}`,
        // Rodada 12 (issue #16): o detalhe diz a origem (automático, hora e
        // motivo), a mesma frase do cartão do Balcão.
        detalhe: origemDaPassagem(conversa.passagem).curto,
        quando: passagem,
        conversaId: conversa.id,
        origem: ORIGENS.PASSAGEM,
      }))
    }

    const pagamento = prazoDoPagamento(conversa, agora)
    if (pagamento != null) {
      lista.push(novoLembrete({
        id: `auto-pagamento-${conversa.id}`,
        titulo: `Confirmar pagamento de ${nome}`,
        detalhe: `Pedido ${numeroCurto(conversa.pedido.numero)} sem pagamento`,
        quando: pagamento,
        conversaId: conversa.id,
        origem: ORIGENS.PAGAMENTO,
      }))
    }
  }
  return lista
}

// A lista que o sininho mostra: só a tarefa dela.
export const listaDeLembretes = ({ lembretes }) => porHora(lembretes ?? [])

// Sugestões de tarefa a partir do que a conversa tem agora. São as três falas
// da entrevista: confirmar pagamento, responder alguém, chamar o motoboy.
export function sugestoesParaDona(conversa, faixa) {
  if (!conversa) return []
  const nome = primeiroNome(conversa.nome)
  const lista = []
  if (conversa.pedido?.estado === 'aguardando') lista.push(`Confirmar pagamento de ${nome}`)
  lista.push(`Responder ${nome}`)
  if (faixa) lista.push(`Chamar o motoboy da janela ${faixa}`)
  return lista
}

// Pílulas de "Quando" da modal Programar lembrete (seção 8). Cada uma já
// mostra a hora que dá, calculada a partir do `agora` da tela: nada aqui olha
// Date.now() (mesma regra do topo do arquivo).
export const OPCOES_QUANDO = [
  { id: '10min', prefixo: 'Em 10 min', minutos: 10 },
  { id: '30min', prefixo: 'Em 30 min', minutos: 30 },
  { id: '1h', prefixo: 'Em 1 h', minutos: 60 },
  { id: 'amanha', prefixo: 'Amanhã', hora: 9 },
]

export const OPCAO_OUTRO_HORARIO = 'outro'

const horaComH = (ms) => {
  const data = new Date(ms)
  return `${data.getHours()}h${String(data.getMinutes()).padStart(2, '0')}`
}

export function quandoDaOpcao(opcaoId, agora) {
  const opcao = OPCOES_QUANDO.find((o) => o.id === opcaoId)
  if (!opcao) return agora
  if (opcao.minutos != null) return agora + opcao.minutos * 60000
  const alvo = new Date(agora)
  alvo.setDate(alvo.getDate() + 1)
  alvo.setHours(opcao.hora, 0, 0, 0)
  return alvo.getTime()
}

export const rotuloDaOpcao = (opcaoId, agora) => {
  const opcao = OPCOES_QUANDO.find((o) => o.id === opcaoId)
  return opcao ? `${opcao.prefixo} · ${horaComH(quandoDaOpcao(opcaoId, agora))}` : ''
}

// "Outro horário": ela escolhe a hora do relógio. Se já passou hoje, vale
// amanhã, porque um horário no passado não é lembrete, é registro.
export function quandoDoHorario(horario, agora) {
  const [h, m] = horario.split(':').map(Number)
  const alvo = new Date(agora)
  alvo.setHours(h, m, 0, 0)
  if (alvo.getTime() <= agora) alvo.setDate(alvo.getDate() + 1)
  return alvo.getTime()
}

export function rotuloDoHorario(horario, agora) {
  const quando = quandoDoHorario(horario, agora)
  const amanha = new Date(quando).getDate() !== new Date(agora).getDate()
  return `${amanha ? 'Amanhã' : 'Hoje'} · ${horaComH(quando)}`
}
