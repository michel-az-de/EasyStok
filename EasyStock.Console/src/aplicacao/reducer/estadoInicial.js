import { FUNCIONAMENTO_PADRAO } from '../../dominio/funcionamento'
import { REGRA_FIDELIDADE_PADRAO } from '../../dominio/fidelidade'
import { FIDELIDADE_SEMENTE } from '../../infra/fidelidadeSemente'

export const ATENDENTE = 'Thatiane'

export function estadoInicial({
  conversas, catalogo, regras, modoAgente, som = true,
  funcionamento = FUNCIONAMENTO_PADRAO, lojaAberta = null,
}) {
  return {
    conversas,
    catalogo,
    regras,
    modoAgente,
    // Horário de funcionamento por dia e estado manual da loja (pedido do
    // dono, 24/09/2026): `lojaAberta` nasce `null` (segue o horário) até o
    // primeiro toque no controle do topo.
    funcionamento,
    lojaAberta,
    // #2135: abre sem conversa escolhida. A primeira do array não é a primeira da fila (a massa abria na
    // 34ª linha, fora da lista visível), e a seleção que a atendente já fez volta pela visita da sessão.
    selecionadaId: null,
    // Balcão (seção 1, rodada 5): duas abas, canais que ligam mais de um ao
    // mesmo tempo e uma ordem só. `MUDAR_FILTRO` é genérico (chave/valor), a
    // forma nova só muda o que mora em cada chave. #1442: abre em "Todas";
    // "Precisa de você" virou filtro com contador.
    filtros: { busca: '', aba: 'todas', canais: [], ordenacao: 'urgencia', responsavel: 'todas' },
    rascunhos: {},
    // #1577: quantas vezes cada rascunho foi escrito de fora do campo (ver rascunhoLocal.js).
    escritasDeFora: {},
    automaticoPausado: {},
    // Lista, não campo único: dois desacertos de saldo antes dela clicar
    // "Entendi" no primeiro não podem apagar um ao outro (QA2-16).
    alertasEstoque: [],
    agente: { estado: 'ocioso', sugestao: null, conversaId: null },
    ultimoAvanco: null,
    // Preferência de "Som da cozinha" (US-010): quem chama `estadoInicial`
    // decide o valor de partida; lida do localStorage em AtendimentoProvider.
    som,
    lembretes: [],
    lembretesConcluidos: {},
    // Visto do sininho, guardado por `id`+`quando` (seção 8): assim um
    // lembrete automático (id fixo) que volta adiado, em horário novo, conta
    // como novo de novo. A Frente B decide a chave e quando marcar.
    vistos: {},
    // Rodada 5 · passo zero (seção 8). `ui.encerrando` guarda o id da
    // conversa cuja modal de encerramento (seção 5) está aberta, ou `null`;
    // abre e fecha de QUALQUER lugar da tela (cabeçalho, menu do pedido,
    // barra do Pedido), então mora no estado, não num `useState` de um
    // componente só. `relogio.deslocamentoMs` é o painel de simulações
    // (seção 7, "+10 min", "+30 min", "Agora") empurrando o relógio da tela
    // sem esperar de verdade; zero não muda nada hoje.
    ui: { encerrando: null },
    relogio: { deslocamentoMs: 0 },
    // Lote de papel (rodada 8, US-042, D6, UC-04 E1): `pedidosAbertos` é a
    // foto (números de pedido) tirada no instante em que caiu, não o estado
    // atual — casos/lote.js e dominio/loteDePapel.js decidem sozinhos, este
    // arquivo só dá o valor de partida.
    conexao: { online: true, offlineDesde: null, pedidosAbertos: [] },
    // Produção (rodada 13, issue #43): lotes por sku (RN-44 a RN-47),
    // descoberto por sku (RN-48/RN-49) e o histórico de ajustes com motivo
    // (UC-09). Sku sem lote nenhum aqui continua no `estoque` plano de
    // sempre — nada muda para quem não usa a aba nova.
    producao: { lotes: [], descobertos: {}, ajustes: [] },
    // Caixa (rodada 13, issue #44): uma lista só de lançamentos MovimentoCaixa
    // (abertura/entrada/saida/fechamento), igual ao EasyStok — nada de agregado
    // "sessão de caixa" à parte. `aplicacao/casos/caixa.js` é quem lê e escreve.
    caixa: { movimentos: [] },
    // Frente Fidelidade e cupons (rodada 13, issue #45, registro 107): cupons
    // e catálogo de recompensas/sorteios cadastrados na Gestão; resgate por
    // cadastro (pontos GANHOS são derivados do histórico, não moram aqui, ver
    // dominio/fidelidade.js).
    fidelidade: {
      cupons: FIDELIDADE_SEMENTE.cupons,
      config: REGRA_FIDELIDADE_PADRAO,
      recompensas: FIDELIDADE_SEMENTE.recompensas,
      sorteios: FIDELIDADE_SEMENTE.sorteios,
      resgatesPorCadastro: {},
    },
  }
}
