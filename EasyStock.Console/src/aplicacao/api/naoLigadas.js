// Lista única do modo API (F06, #1236): toda ação do console tem de cair em uma de
// três situações, e `ferramentas/prova-f06-honestidade.mjs` falha se alguma ficar de fora.
//
//   ligada      `comApi` troca a versão local por uma que fala com o EasyStok
//               (`aplicacao/acoesApi.js` e os módulos de `aplicacao/api/`).
//   só da tela  não há nada a gravar: filtro, rascunho, abrir e fechar modal, som,
//               ou o rascunho da comanda que vai junto no "Enviar ao cliente".
//   avisa       ainda sem endpoint: no modo API ela não mexe na memória do navegador,
//               só avisa na faixa. Assim a dona nunca vê feito o que o EasyStok não tem.
//
// Ligar uma ação (F09 a F16) é tirá-la daqui e escrever a versão em `aplicacao/api/`.
import * as acao from '../acoes'

export const SO_DA_TELA = [
  'mudarFiltro', 'definirRascunho', 'abrirEncerramento', 'fecharEncerramento', 'fecharAlerta',
  'trocarModoAgente', 'limparAgente', 'alternarSom', 'ouvirAmostraDeSom', 'marcarLembretesVistos',
  'pedirNotificacaoDoNavegador',
  // Rascunho da comanda: o meio escolhido segue no corpo do pedido (F03).
  'escolherMeioPagamento',
  // Marca de canhoto já impresso nesta tela; a impressão em si não depende dela.
  'marcarCanhotoImpresso',
]

const grupo = (rotulo, nomes) => Object.fromEntries(nomes.map((nome) => [nome, rotulo]))

// Nome da ação -> rótulo que abre o aviso ("<rótulo>: ainda não ligado nesta versão.").
export const NAO_LIGADAS = {
  reabrir: 'Reabrir conversa',
  trocarJanela: 'Trocar a janela do pedido',
  salvarNota: 'Nota do cliente',
  ...grupo('Mensagens automáticas', ['alternarRegra', 'editarRegra']),
  ...grupo('Lembretes', ['criarLembrete', 'concluirLembrete']),
  ...grupo('Bloqueio do cliente', ['bloquearCliente', 'desbloquearCliente']),
  ...grupo('Cardápio do dia', ['alternarDisponibilidade', 'ajustarSaldo']),
  ...grupo('Cardápio', ['incluirItemCardapio', 'editarItemCardapio', 'alternarRemocaoItemCardapio', 'confirmarValidacaoItem']),
  mudarEnderecoDoPedido: 'Endereço do pedido',
  ...grupo('Tags do cliente', ['adicionarTag', 'removerTag', 'editarTag']),
  marcarRecebidoEntrega: 'Recebido na entrega',
  refazerCobranca: 'Refazer cobrança',
  ...grupo('Avaliação e anotação do encerramento', ['salvarAvaliacaoCliente', 'salvarAutoavaliacao', 'salvarAnotacaoFechamento']),
  ...grupo('Entregas por aqui (use a gaveta Entregas)', [
    'criarViagem', 'desfazerViagem', 'trocarModoViagem', 'porNaViagem', 'tirarDaViagem', 'reordenarParada',
    'aplicarOrdemDaViagem', 'alterarAgendamentoEntrega', 'chamarEntregador', 'cancelarChamado', 'atualizarChamado',
    'sairParaEntrega', 'marcarParadaEntregue',
  ]),
  ...grupo('Simulação', [
    'montarRoteiro', 'executarPassoSimulado', 'deslocarRelogioSimulado', 'zerarRelogioSimulado', 'limparSimulacoes',
    'reagirComoClienteSimulado', 'entregarRespostaAutomatica',
  ]),
  decidirAreaEntrega: 'Área de entrega',
  ...grupo('Galeria de fotos', ['incluirPeca', 'editarPeca', 'tirarPeca']),
  ...grupo('Respostas prontas', ['incluirRespostaPronta', 'editarRespostaPronta', 'alternarArquivamentoRespostaPronta']),
  ...grupo('Lote de papel', ['conexaoCaiu', 'conexaoVoltou', 'lancarLotePapel']),
  ...grupo('Janelas de entrega', ['criarJanela', 'editarJanela', 'pausarJanela', 'reativarJanela', 'excluirJanela', 'ajustarRespiroMinimo']),
  ...grupo('Produção', ['registrarProducao', 'ajustarContagemProducao']),
  ...grupo('Caixa', ['abrirCaixa', 'lancarMovimentoCaixa', 'estornarMovimentoCaixa', 'fecharCaixa', 'lancarVendaAvulsa']),
  ...grupo('Integrações', [
    'alternarProvedorLogistica', 'salvarCredencialProvedor', 'definirProvedorPadrao', 'cotarEntrega',
    'chamarEntregadorComProvedor', 'cancelarCorrida', 'avancarCorrida',
  ]),
  ...grupo('Fidelidade e cupons', [
    'criarCupom', 'editarCupom', 'alternarCupomAtivo', 'editarRegraFidelidade', 'criarRecompensa', 'editarRecompensa',
    'alternarRecompensaAtiva', 'criarSorteio',
  ]),
  ...grupo('Cupom e recompensa no pedido', ['aplicarCupom', 'removerCupomDoPedido', 'resgatarRecompensa']),
  ...grupo('Ocorrência', ['apurarOcorrencia', 'encerrarOcorrenciaSemEstorno']),
}

export const textoNaoLigado = (rotulo) => `${rotulo}: ainda não ligado nesta versão.`

// Variações do `enviar` (ligado só para texto livre) que a API ainda não tem (#1287): modelo
// aprovado da Meta e mensagem automática disparada pela biblioteca. Mandar como texto livre
// assumiria a conversa e cairia no 409 fora da janela, que é justamente onde o modelo serve.
// O modelo aprovado já sai pela mensagem programada (#1424, `api/mensagensProgramadas.js`).
export const ENVIOS_NAO_LIGADOS = {
  modelo: 'Modelo aprovado',
  automatica: 'Mensagem automática pela biblioteca',
}

export const envioNaoLigado = (opcoes = {}) =>
  Object.entries(ENVIOS_NAO_LIGADOS).find(([chave]) => opcoes[chave])?.[1] ?? null

// Cada não ligada vira um aviso na faixa, sem despacho local e sem chamada à API.
export function criarAvisosNaoLigadas(despachar) {
  return Object.fromEntries(Object.entries(NAO_LIGADAS).map(([nome, rotulo]) => [
    nome, () => { despachar({ tipo: acao.AVISO_API, mensagem: textoNaoLigado(rotulo) }) },
  ]))
}
