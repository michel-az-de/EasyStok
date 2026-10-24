import { podeAlterarMeio } from '../dominio/cobranca'
import { totalDoPedido } from '../dominio/pedido'
import { proximoId, proximoNumeroPedido } from '../infra/numeracao'
import { emitirCobranca } from '../infra/provedoresDeCobranca'
import { tocarAmostra } from '../infra/som'
import * as acao from './acoes'
import { demonstracao } from '../infra/demonstracao'
import { criarAcoesDaTela } from './acoes/locais'

// As ações do lado do navegador. As que o modo API também usa (filtro, rascunho, comanda em rascunho, som,
// modal de encerramento...) ficam aqui; as que só a demonstração usa (esteira, Pix, entregas simuladas...)
// vêm de `src/demonstracao/`, que o build de produção não carrega. Ver `infra/demonstracao.js`.
export function criarAcoesLocais({ despachar, agoraRef, estadoRef, pedirNotificacaoDoNavegador, guardarRascunhoJa }) {
  return {
    ajustarQuantidade: (id, sku, delta) => despachar({
      tipo: acao.AJUSTAR_QUANTIDADE, id, sku, delta, alertaId: proximoId('alerta'),
      agora: agoraRef.current,
    }),
    ajustarObservacao: (id, sku, texto) => despachar({ tipo: acao.AJUSTAR_OBSERVACAO, id, sku, texto }),
    mudarFiltro: (chave, valor) => despachar({ tipo: acao.MUDAR_FILTRO, chave, valor }),
    definirRascunho: (id, texto) => despachar({ tipo: acao.DEFINIR_RASCUNHO, id, texto }),
    // #1577: texto do campo de escrever, que chega na pausa da digitação. `ja` é a tela saindo (401, Hall,
    // aba oculta): o despacho pode não render mais, então o Provider também grava direto.
    descarregarRascunho: (despacho, { ja = false, desmontando = false } = {}) => {
      despachar({ tipo: acao.DEFINIR_RASCUNHO, ...despacho, doCampo: true, adotar: desmontando })
      if (ja) guardarRascunhoJa?.(despacho)
    },
    removerItem: (id, sku) => despachar({ tipo: acao.REMOVER_ITEM, id, sku }),
    fecharAlerta: (alertaId) => despachar({ tipo: acao.FECHAR_ALERTA, alertaId }),
    trocarModoAgente: (modo) => despachar({ tipo: acao.TROCAR_MODO_AGENTE, modo }),
    limparAgente: () => despachar({ tipo: acao.AGENTE_LIMPOU }),
    // numeroPedido vem de uma série própria (infra/repositorioConversas.js),
    // separada do contador de mensagem/nota/alerta: só entra no pedido
    // quando a conversa ainda não tem um (o reducer ignora o valor se já
    // existe `c.pedido`), então continua a série real da massa em vez de
    // nascer com o próximo id solto de qualquer coisa (achado do gerente,
    // 24/09: "Nº 1" no canhoto).
    // #1686 (item 3): segunda compra na mesma conversa, depois do pedido entregue ou cancelado.
    novaComanda: (id) => despachar({ tipo: acao.NOVA_COMANDA, id, numeroPedido: proximoNumeroPedido() }),
    adicionarItem: (id, item) => despachar({
      tipo: acao.ADICIONAR_ITEM, id, item, numeroPedido: proximoNumeroPedido(), alertaId: proximoId('alerta'),
      agora: agoraRef.current,
    }),
    // Rodada 12 (issue #13): "Alterar forma de pagamento". Emite a cobrança
    // nova aqui fora (efeito, como gerarCobranca) e despacha uma ação só,
    // que cancela a pendente e manda a nova. Sem link, a esteira anda como
    // em qualquer cobrança de maquininha ou vale. A guarda é a mesma do
    // reducer: sem ela, DESPACHAR_MESMO_ASSIM andaria um pedido pago.
    alterarMeioPagamento: (id, meio) => {
      const pedido = estadoRef.current.conversas.find((c) => c.id === id)?.pedido
      if (!podeAlterarMeio(pedido) || !meio || meio === pedido.cobranca.meio) return
      const emissao = {
        ...emitirCobranca(meio, {
          numeroPedido: pedido.numero,
          valor: totalDoPedido(pedido, estadoRef.current.catalogo.cardapio),
        }),
        meio,
      }
      despachar({
        tipo: acao.ALTERAR_MEIO_PAGAMENTO, id, agora: agoraRef.current, emissao,
        mensagemId: proximoId('msg'), sistemaId: proximoId('sis'),
      })
      if (!emissao.link) {
        despachar({ tipo: acao.DESPACHAR_MESMO_ASSIM, id, agora: agoraRef.current })
      }
    },
    // Rodada 2 · janelas de entrega com capacidade.
    escolherJanela: (id, janela) => despachar({ tipo: acao.ESCOLHER_JANELA, id, janela }),
    forcarEncaixe: (id, janela, faixa, motivo) => despachar({
      tipo: acao.FORCAR_ENCAIXE, id, janela, faixa, motivo,
      agora: agoraRef.current, mensagemId: proximoId('sis'),
    }),
    alternarSom: (ligado) => despachar({ tipo: acao.ALTERNAR_SOM, ligado }),
    // Amostra sob demanda (US-010): não muda estado, só toca. Passa pela
    // camada de ações porque features não importa infra direto.
    ouvirAmostraDeSom: (evento) => tocarAmostra(evento),
    // Rodada 5 · passo zero da direção visual (seção 8). `ui.encerrando`
    // abre a modal de encerramento (seção 5) de qualquer lugar: cabeçalho
    // da conversa, menu do pedido e barra do Pedido.
    abrirEncerramento: (id, numero = null) => despachar({ tipo: acao.ABRIR_ENCERRAMENTO, id, numero }),
    fecharEncerramento: () => despachar({ tipo: acao.FECHAR_ENCERRAMENTO }),
    pedirNotificacaoDoNavegador,
    ...criarAcoesDaTela(despachar),
  }
}

// Todas as ações do navegador, fora do React (F06, #1236): o Provider chama isto dentro de um `useMemo`.
// `pendentes` guarda os timers da entrega simulada, para o Provider limpar ao desmontar.
export function criarAcoes(argumentos) {
  return { ...criarAcoesLocais(argumentos), ...(demonstracao ? demonstracao.criarAcoes(argumentos) : null) }
}
