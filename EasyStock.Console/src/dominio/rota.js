// Rota lida por hash. Função pura, recebe a string do hash por parâmetro (a
// mesma regra de dominio/canal.js: domínio não lê `window`).
//
// Issue #40 (rodada 13, achado da varredura): `app/App.jsx` decidia a tela
// só uma vez, ao montar. Quando a hash mudava com o app já aberto (janela
// nova bloqueada, ou o navegador reaproveitando a mesma aba no lugar de abrir
// uma nova, comum em tablet), a tela nunca trocava e o link do cardápio
// aberto na mesma aba chegava a mostrar o Balcão da dona. Agora quem lê o
// hash de verdade (`hooks/useHash.js`) chama esta função a cada troca.
//
// #1447 (homologação de 07/10): sem hash o console abre no hall de módulos, e
// `#/m/<modulo>/<tela>` abre a tela do módulo (spec M0.2, ADR-0046 D1: o módulo
// vem da rota). As rotas de antes continuam como apelido: `#/cozinha`,
// `#/entregas` e `#/cardapio-link/<id>` abrem a mesma tela de sempre.

import { PREFIXO_ROTA as PREFIXO_CARDAPIO_LINK } from './cardapioLink'
import { MODULO_DOS_PRIMEIROS_PASSOS, TELA_PRIMEIROS_PASSOS, ehPassoConhecido } from './configuracaoInicial'
import { moduloCongelado } from './congelados'
import { TELA_OPERACAO, hashDoModulo, moduloPorId, telasDoMenu } from './modulos'

export const ROTA_HALL = 'hall'
export const ROTA_MODULO = 'modulo'
export const ROTA_PRINCIPAL = 'principal'
export const ROTA_ENTREGAS = 'entregas'
export const ROTA_COZINHA = 'cozinha'
export const ROTA_CARDAPIO_LINK = 'cardapio-link'

export const HASH_HALL = '#/'
export const HASH_ENTREGAS = '#/entregas'
export const HASH_COZINHA = '#/cozinha'
// #1474: a barra lateral abre a tela de operação do módulo, com a barra "← Módulos" para
// voltar. Os apelidos de cima seguem para a janela avulsa do tablet.
export const HASH_MODULO_COZINHA = '#/m/cozinha'
export const HASH_MODULO_ENTREGAS = '#/m/entregas'

const PREFIXO_MODULO = '#/m/'

// Tela de operação de cada módulo: a mesma tela que abria antes do hall.
const ROTA_DA_OPERACAO = {
  'atendimento/balcao': ROTA_PRINCIPAL,
  'cozinha/fila': ROTA_COZINHA,
  'entregas/painel': ROTA_ENTREGAS,
  'entregas/agenda': ROTA_ENTREGAS,
}

// #2070: Primeiros passos guarda o passo no terceiro segmento (o Voltar do navegador volta um passo). Passo fora do
// mapa vira `null` e a tela retoma do primeiro em aberto.
const ehPrimeirosPassos = (moduloId, telaId) => moduloId === MODULO_DOS_PRIMEIROS_PASSOS && telaId === TELA_PRIMEIROS_PASSOS
export const hashDoPasso = (passo) => `${hashDoModulo(MODULO_DOS_PRIMEIROS_PASSOS, TELA_PRIMEIROS_PASSOS)}/${passo}`

function rotaDoModulo(caminho, fonteApi) {
  const [moduloId, telaId, passoId] = caminho.split('/')
  const modulo = moduloPorId(moduloId)
  // Módulo congelado (#1891) é tratado como inexistente: cai no hall, sem moldura vazia.
  if (!modulo || moduloCongelado(modulo.id)) return { tipo: ROTA_HALL }
  const telas = telasDoMenu(modulo, { fonteApi })
  // Módulo "em breve" digitado à mão: a moldura do módulo diz que ainda não há tela.
  if (telas.length === 0) return { tipo: ROTA_MODULO, modulo: modulo.id, tela: null, aba: null }
  const tela = telas.find((t) => t.id === telaId) ?? telas[0]
  if (tela.tipo === TELA_OPERACAO) {
    return { tipo: ROTA_DA_OPERACAO[`${modulo.id}/${tela.id}`], modulo: modulo.id, tela: tela.id }
  }
  const rota = { tipo: ROTA_MODULO, modulo: modulo.id, tela: tela.id, aba: tela.aba }
  return ehPrimeirosPassos(modulo.id, tela.id) ? { ...rota, passo: ehPassoConhecido(passoId) ? passoId : null } : rota
}

// `hash` é `window.location.hash` (string vazia ou ausente na carga sem hash
// nenhuma). Cardápio por link carrega o id da conversa junto, porque a tela
// precisa dele (mesmo prefixo de dominio/cardapioLink.js). Hash desconhecida
// cai no hall: nunca trava nem finge outra tela.
export function rotaDaHash(hash, { fonteApi = false } = {}) {
  const valor = hash ?? ''
  if (valor === HASH_ENTREGAS) return { tipo: ROTA_ENTREGAS }
  if (valor === HASH_COZINHA) return { tipo: ROTA_COZINHA }
  if (valor.startsWith(PREFIXO_CARDAPIO_LINK)) {
    return { tipo: ROTA_CARDAPIO_LINK, conversaId: valor.slice(PREFIXO_CARDAPIO_LINK.length) }
  }
  if (valor.startsWith(PREFIXO_MODULO)) return rotaDoModulo(valor.slice(PREFIXO_MODULO.length), fonteApi)
  return { tipo: ROTA_HALL }
}
