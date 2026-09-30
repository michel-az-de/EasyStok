// Rota das janelas espelho lidas por hash: Entregas, Cozinha e Cardápio por
// link (US-021, US-037, US-038). Função pura, recebe a string do hash por
// parâmetro (a mesma regra de dominio/canal.js: domínio não lê `window`).
//
// Issue #40 (rodada 13, achado da varredura): `app/App.jsx` decidia a tela
// só uma vez, ao montar. Quando a hash mudava com o app já aberto (janela
// nova bloqueada, ou o navegador reaproveitando a mesma aba no lugar de abrir
// uma nova, comum em tablet), a tela nunca trocava e o link do cardápio
// aberto na mesma aba chegava a mostrar o Balcão da dona. Agora quem lê o
// hash de verdade (`hooks/useHash.js`) chama esta função a cada troca.

import { PREFIXO_ROTA as PREFIXO_CARDAPIO_LINK } from './cardapioLink'

export const ROTA_PRINCIPAL = 'principal'
export const ROTA_ENTREGAS = 'entregas'
export const ROTA_COZINHA = 'cozinha'
export const ROTA_CARDAPIO_LINK = 'cardapio-link'

export const HASH_ENTREGAS = '#/entregas'
export const HASH_COZINHA = '#/cozinha'

// `hash` é `window.location.hash` (string vazia ou ausente na carga sem
// hash nenhuma). Cardápio por link carrega o id da conversa junto, porque a
// tela precisa dele (mesmo prefixo de dominio/cardapioLink.js).
export function rotaDaHash(hash) {
  const valor = hash ?? ''
  if (valor === HASH_ENTREGAS) return { tipo: ROTA_ENTREGAS }
  if (valor === HASH_COZINHA) return { tipo: ROTA_COZINHA }
  if (valor.startsWith(PREFIXO_CARDAPIO_LINK)) {
    return { tipo: ROTA_CARDAPIO_LINK, conversaId: valor.slice(PREFIXO_CARDAPIO_LINK.length) }
  }
  return { tipo: ROTA_PRINCIPAL }
}
