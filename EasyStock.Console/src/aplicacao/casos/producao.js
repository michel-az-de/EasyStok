// Casos de reducer da frente Produção e cardápio (rodada 13, issue #43,
// UC-08, UC-09). Mesmo molde de `casos/cardapio.js`: mexe só em
// `estado.producao` (novo) e no `estoque` do item de `estado.catalogo.cardapio`
// (RN-46: saldo do cardápio nasce e vive em porções, a partir dos lotes).
import * as acao from '../acoes'
import { itemPorSku } from '../../dominio/cardapio'
import {
  ajustarContagemLotes, criarLoteProducao, fecharDescoberto, saldoEmPorcoes,
} from '../../dominio/producao'

const comEstoqueDoSku = (cardapio, sku, estoque) =>
  cardapio.map((item) => (item.sku === sku ? { ...item, estoque } : item))

export const casosProducao = {
  // UC-08: registra o lote (identificador, data, validade, RN-44) já em
  // porções de venda (RN-46). Insumo intermediário (alt A: molho, massa
  // laminada) entra na lista de lotes mas nunca aparece no saldo do
  // cardápio — fica disponível só para a própria produção usar depois.
  [acao.PRODUCAO_REGISTRAR_LOTE]: (estado, {
    loteId, sku, identificador, pesoRealG, porcoes, validadeDias, insumo, agora,
  }) => {
    const lote = criarLoteProducao({
      id: loteId,
      sku,
      identificador,
      pesoRealG,
      porcoes,
      validadeDias,
      produzidoEmIso: new Date(agora).toISOString(),
      insumo,
    })
    const lotes = [...estado.producao.lotes, lote]
    return {
      ...estado,
      producao: { ...estado.producao, lotes },
      catalogo: insumo ? estado.catalogo : {
        ...estado.catalogo,
        cardapio: comEstoqueDoSku(estado.catalogo.cardapio, sku, saldoEmPorcoes(lotes, sku)),
      },
    }
  },

  // UC-09 passos 6-7: ela conta o físico e lança o ajuste. Motivo obrigatório
  // (mínimo 3 caracteres, mesmo padrão do estorno de estoque do EasyStok) e
  // o ajuste fica registrado com data e motivo, nunca por delete. Fecha o
  // alerta persistente do sku (aceite da issue), mesmo quando o saldo
  // corrigido continua baixo.
  [acao.PRODUCAO_AJUSTAR_CONTAGEM]: (estado, {
    ajusteId, sku, loteId, novoSaldoTotal, motivo, agora,
  }) => {
    const motivoLimpo = (motivo ?? '').trim()
    if (motivoLimpo.length < 3) return estado
    const item = itemPorSku(estado.catalogo.cardapio, sku)
    if (!item) return estado
    const saldoAntes = item.estoque
    const { lotes, aplicadoEmLote } = ajustarContagemLotes(estado.producao.lotes, sku, novoSaldoTotal)
    // Sem lote nenhum do sku (ela vendeu descoberto sem nunca ter lançado
    // produção), não há onde encostar o ajuste: o número contado vale direto
    // no cardápio, registrado como limite de protótipo.
    const saldoDepois = aplicadoEmLote ? saldoEmPorcoes(lotes, sku) : novoSaldoTotal
    return {
      ...estado,
      producao: {
        ...estado.producao,
        lotes,
        descobertos: fecharDescoberto(estado.producao.descobertos, sku),
        ajustes: [...estado.producao.ajustes, {
          id: ajusteId, sku, loteId: loteId ?? null, motivo: motivoLimpo,
          saldoAntes, saldoDepois, agora: new Date(agora).toISOString(),
        }],
      },
      catalogo: {
        ...estado.catalogo,
        cardapio: comEstoqueDoSku(estado.catalogo.cardapio, sku, saldoDepois),
      },
    }
  },
}
