// Massa de exemplo da frente Fidelidade e cupons (rodada 13, issue #45,
// registro 107): cupons e catálogo de recompensas coerentes com o cardápio e
// os clientes que já existem na massa (infra/catalogo.js, item de menor
// preço, `EXT-PAR`, vira a recompensa mais barata).
import { novaRecompensa, novoCupom, novoSorteio } from '../dominio/fidelidade'

export const FIDELIDADE_SEMENTE = {
  cupons: [
    novoCupom('cupom-bemvinda', {
      codigo: 'BEMVINDA10', tipoDesconto: 'percentual', valor: 10, minimoPedido: 50,
    }),
    novoCupom('cupom-fixo', {
      codigo: 'BABA15', tipoDesconto: 'valor', valor: 15, validade: '2026-12-31', limiteUsos: 100, minimoPedido: 60,
    }),
  ],
  recompensas: [
    novaRecompensa('recompensa-parmesao', {
      tipo: 'produto', rotulo: 'Parmesão ralado (extra)', custoPontos: 20, skuProduto: 'EXT-PAR',
    }),
    novaRecompensa('recompensa-frete', {
      tipo: 'frete-gratis', rotulo: 'Frete grátis no próximo pedido', custoPontos: 30,
    }),
    novaRecompensa('recompensa-sorteio', {
      tipo: 'sorteio', rotulo: 'Número da sorte · Sorteio de outubro', custoPontos: 10, sorteioId: 'sorteio-outubro',
    }),
  ],
  sorteios: [
    novoSorteio('sorteio-outubro', { nome: 'Sorteio de outubro', dataSorteio: '2026-10-31' }),
  ],
}
