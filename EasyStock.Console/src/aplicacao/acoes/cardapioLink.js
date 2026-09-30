// Criador de ação da Frente Cardápio por link (rodada 7, US-021). Mesma
// forma de `acoes/entregas.js`: só empacota `{ tipo, ... }`, então a janela
// do site (`useEspelhoCardapioLink.js`, com o `despachar` do espelho) e um
// eventual uso no Balcão ganham o mesmo criador sem duplicar lógica.
// Id por `crypto.randomUUID()`, não por `infra/repositorioConversas`
// (`proximoId`): a janela do site é outro documento, com o próprio contador,
// e um contador que reinicia por janela colidiria (mesmo motivo de
// `acoes/entregas.js`).
//
// A emissão da cobrança é efeito e nasce aqui fora do reducer, igual ao
// `gerarPedido` de `AtendimentoProvider.jsx`: redutor puro não inventa txid
// nem código copia e cola (`infra/provedoresDeCobranca.js`, `aplicacao` pode
// importar `infra`, `features` não pode — por isso a emissão não mora no
// componente da tela).
import * as acao from '../acoes'
import { emitirCobranca } from '../../infra/provedoresDeCobranca'
import { totalDoPedido } from '../../dominio/pedido'

const novoId = () => crypto.randomUUID()

export function criarAcoesCardapioLink(despachar) {
  return {
    criarPedidoCardapioLink: ({ id, agora, numero, itens, janela, endereco, meio, cardapio }) => {
      const emissao = {
        ...emitirCobranca(meio, { numeroPedido: numero, valor: totalDoPedido({ itens }, cardapio) }),
        meio,
      }
      despachar({
        tipo: acao.CRIAR_PEDIDO_CARDAPIO_LINK,
        id, agora, mensagemId: novoId(), numero, itens, janela, endereco, meio, emissao,
      })
      // Rodada 12 (#19): maquininha e vale andam a esteira sem pagamento, igual
      // ao `gerarPedido` do Balcão (AtendimentoProvider.jsx). Antes o site só
      // oferecia Pix e cartão, então o caso não existia aqui.
      if (!emissao.link) despachar({ tipo: acao.DESPACHAR_MESMO_ASSIM, id, agora })
      return emissao
    },
  }
}
