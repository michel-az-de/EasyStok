// #3234 (C1): envios seguidos que falharam com o mesmo texto e o mesmo tipo de falha viram um balão só no fio.
// Na produção eram 4 balões iguais, cada um com o seu "O WhatsApp demorou a responder". Puro: recebe as linhas
// do fio (mensagens, dias, notas, divisórias, assuntos) e devolve as mesmas linhas com cada grupo numa só,
// a mais recente à vista e as de antes em `anteriores`, na ordem do fio. Qualquer linha que não seja uma
// falha igual (dia, nota, divisória, assunto, mensagem do cliente, envio que saiu) fecha o grupo.

import { TIPOS_DE_FALHA, falhaDeEnvio } from './falhaDeEnvio.js'

// Só texto puro agrupa: foto, modelo e anexo chegam com rótulo no texto ("Foto"), e duas fotos diferentes
// virariam "tentativas" da mesma coisa. A frase da falha entra inteira: recusas com motivos diferentes não se juntam.
const assinaturaDaFalha = (linha, soltas) => {
  const m = linha.tipo === 'mensagem' ? linha.mensagem : null
  if (!m || m.dir !== 'out' || m.status !== 'falhou' || soltas.has(m.id)) return null
  if (m.midia || m.modelo || m.formato || m.arte) return null
  const falha = falhaDeEnvio(m.erro)
  return `${falha.tipo}\u0000${falha.texto}\u0000${m.texto ?? ''}`
}

// `soltas`: mensagens que a atendente reenviou nesta tela. Ficam à vista onde ela clicou, com o retorno do
// reenvio, em vez de sumir dentro de um grupo fechado quando a nova falha fica igual à vizinha.
export function agruparFalhasSeguidas(linhas, { soltas = new Set() } = {}) {
  const saida = []
  let grupo = null
  for (const linha of linhas) {
    const assinatura = assinaturaDaFalha(linha, soltas)
    if (assinatura && grupo?.assinatura === assinatura) {
      grupo.mensagens.push(linha.mensagem)
      // A linha do grupo começa onde a primeira tentativa começava: a troca de voz é dela. A chave também:
      // não muda quando o grupo cresce, então o grupo aberto continua aberto com a falha nova.
      saida[saida.length - 1] = {
        ...linha, trocaDeVoz: grupo.trocaDeVoz, anteriores: grupo.mensagens.slice(0, -1), chaveDoGrupo: grupo.mensagens[0].id,
      }
      continue
    }
    grupo = assinatura ? { assinatura, trocaDeVoz: linha.trocaDeVoz, mensagens: [linha.mensagem] } : null
    saida.push(linha)
  }
  return saida
}

// O que a atendente marcou no fio (grupos `abertas`, mensagens `soltas`), por conversa: ir a outra e voltar
// não perde nem mistura. Imutável: cada marca devolve um objeto novo.
const SEM_MARCAS = Object.freeze({ abertas: new Set(), soltas: new Set() })
export const marcasDaConversa = (marcas, conversa) => marcas[conversa] ?? SEM_MARCAS
export function marcarNaConversa(marcas, conversa, campo, id, alternar) {
  const base = marcasDaConversa(marcas, conversa)
  const conjunto = new Set(base[campo])
  if (!(alternar && conjunto.delete(id))) conjunto.add(id)
  return { ...marcas, [conversa]: { ...base, [campo]: conjunto } }
}

// O aviso do grupo sai do tipo: com tempo esgotado a mensagem pode ter chegado, então não se diz "não saíram".
export function avisoDasTentativas(falha, tentativas) {
  if (tentativas <= 1) return falha.texto
  if (falha.tipo === TIPOS_DE_FALHA.TEMPO_ESGOTADO) {
    return `${tentativas} tentativas sem resposta do WhatsApp. Pode ter chegado: confira com o cliente antes de reenviar.`
  }
  if (falha.tipo === TIPOS_DE_FALHA.DESCONHECIDA) return `${tentativas} tentativas não saíram. Tente de novo.`
  return `${tentativas} tentativas não saíram. ${falha.texto}`
}
