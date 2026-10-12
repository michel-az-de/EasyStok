// Janelas que o Balcão abre à parte (#1616): a Cozinha e as Entregas, cada uma com o nome
// dado no `window.open`. A mesma constante serve para abrir e para a janela saber que é
// avulsa. Puro: quem lê `window` é a tela.

import { ROTA_PRINCIPAL, rotaDaHash } from './rota'

export const JANELA_COZINHA = 'cdb-cozinha'
export const JANELA_ENTREGAS = 'cdb-entregas'
const NOMES = [JANELA_COZINHA, JANELA_ENTREGAS]

// Avulsa: tem um dos nomes e a janela que a abriu segue aberta. Nela, "Módulos" e "Balcão"
// carregariam um segundo Balcão aqui dentro (outra sincronização, outro som, comandas
// separadas); a volta é para a janela de origem. Sem origem (tablet da cozinha, hall, origem
// já fechada) é uma janela comum.
export const ehJanelaAvulsa = ({ nome, origemAberta }) => origemAberta === true && NOMES.includes(nome)

// A janela de origem saiu do Balcão (foi a outra tela)? Então "Voltar ao Balcão" a leva de volta.
export const origemForaDoBalcao = (hashDaOrigem, { fonteApi = false } = {}) =>
  rotaDaHash(hashDaOrigem, { fonteApi }).tipo !== ROTA_PRINCIPAL
