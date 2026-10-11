// Título da aba do navegador (#1582). Puro: recebe a rota já decidida por `rotaDaHash`.
//
// Balcão, Cozinha e Entregas ficam abertos em janelas separadas e todas se chamavam
// "Casa da Baba"; na barra de tarefas não dava para saber qual era qual. O nome da tela
// vem do catálogo de módulos, o mesmo do hall e do menu. A contagem de "Precisa de você"
// só existe no Balcão e vai na frente, para caber em aba estreita.

import { moduloPorId } from './modulos'
import { ROTA_CARDAPIO_LINK, ROTA_COZINHA, ROTA_ENTREGAS, ROTA_PRINCIPAL } from './rota'

export const TITULO_BASE = 'Casa da Baba'

// Janela avulsa aberta pelo apelido (`#/cozinha`, `#/entregas`): não tem módulo na rota.
const NOME_DO_APELIDO = {
  [ROTA_PRINCIPAL]: 'Balcão',
  [ROTA_COZINHA]: 'Cozinha',
  [ROTA_ENTREGAS]: 'Entregas',
  [ROTA_CARDAPIO_LINK]: 'Cardápio',
}

export function nomeDaTela(rota) {
  const modulo = moduloPorId(rota?.modulo)
  if (!modulo) return NOME_DO_APELIDO[rota?.tipo] ?? null
  return modulo.telas.find((tela) => tela.id === rota.tela)?.rotulo ?? modulo.nome
}

export function tituloDaAba(rota, precisaDeVoce = 0) {
  const nome = nomeDaTela(rota)
  return `${precisaDeVoce > 0 ? `(${precisaDeVoce}) ` : ''}${nome ? `${nome} · ` : ''}${TITULO_BASE}`
}
