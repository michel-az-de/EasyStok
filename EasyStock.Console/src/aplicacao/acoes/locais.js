import * as acao from '../acoes'

// Ações que só mexem na tela e que o modo API também usa (filtro, modal, rascunho da comanda, troco, canhoto,
// conexão). `api/naoLigadas.js` as lista em `SO_DA_TELA`; ficam fora de `src/demonstracao/` por isso.
export function criarAcoesDaTela(despachar) {
  return {
    // Chip de meio marcado (ponta a da integração): fica no pedido.
    escolherMeioPagamento: (id, meio) => despachar({ tipo: acao.ESCOLHER_MEIO_PAGAMENTO, id, meio }),

    // J2 (#1990): troco do dinheiro na entrega, em reais (null = não informado).
    definirTroco: (id, trocoPara) => despachar({ tipo: acao.DEFINIR_TROCO, id, trocoPara }),

    marcarCanhotoImpresso: (numero) => despachar({ tipo: acao.MARCAR_CANHOTO_IMPRESSO, numero }),

    // #1443: o gesto da loja só é pedido no modo API; fechar o modal é só da tela.
    fecharGestoLoja: () => despachar({ tipo: acao.FECHAR_GESTO_LOJA }),

    conexaoCaiu: (agora) => despachar({ tipo: acao.CONEXAO_CAIU, agora }),

    conexaoVoltou: (agora) => despachar({ tipo: acao.CONEXAO_VOLTOU, agora }),
  }
}
