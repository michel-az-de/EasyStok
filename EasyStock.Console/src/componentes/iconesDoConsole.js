// Mapa único de ícones do Console (#2258, auditoria de UX de 05/11, A1 e G2): um ícone por módulo e
// por tela, o mesmo no Início, na Ajuda, no Menu e na Busca. Quem desenha (`IconeDoModulo`, o cartão
// de número do Início, a moldura dos módulos) lê daqui; o desenho em si mora em `Icone` (traço Lucide)
// e `MarcaIcone` (traço da marca). Mora em `componentes` porque é quem desenha que o conhece: o domínio
// não importa ícone.
//
// Regra: dentro de um módulo nenhuma tela repete o ícone de outra, e dois módulos nunca dividem o
// mesmo. A tela principal pode usar o ícone do próprio módulo. Tela sem entrada herda o do módulo.
// A chave é o id do módulo (`atendimento`) ou `modulo/tela` (`financeiro/pagar`). Cada chave está em
// UMA das duas tabelas: `MARCA_DO_CONSOLE` (traço da marca) ou `ICONE_DO_CONSOLE` (dicionário do `Icone`).

const MARCA_DO_CONSOLE = {
  atendimento: 'mensagem',
  entregas: 'entrega',
  producao: 'producao',
  financeiro: 'financeiro',
  'atendimento/balcao': 'mensagem',
  'atendimento/horarios': 'horarios',
  'cozinha/producao': 'pedido-em-preparo',
  'entregas/painel': 'entrega',
  'entregas/janelas': 'horarios',
  'cardapio/itens': 'produto',
  'cardapio/categorias': 'grade',
  'producao/estoque': 'estoque',
  'producao/producao': 'producao',
  'producao/insumos': 'ingredientes',
  'producao/receitas': 'receita',
  'producao/calculadora': 'calculadora',
  'producao/perdas': 'atencao',
  'financeiro/caixa': 'caixa',
  'campanhas/fidelidade': 'cupom',
}

const ICONE_DO_CONSOLE = {
  cadastros: 'usuarios',
  'cadastros/clientes': 'usuarios',
  'cadastros/produtos': 'package',
  'cadastros/fornecedores': 'house',
  'cadastros/bancos': 'dollar-sign',
  'cadastros/pagamentos': 'credit-card',
  'cadastros/modelos': 'modelo',
  cozinha: 'cooking-pot',
  cardapio: 'folha-cardapio',
  configuracoes: 'settings',
  campanhas: 'presente',
  'atendimento/avisos': 'bell',
  'atendimento/respostas': 'raio',
  'cozinha/fila': 'cooking-pot',
  'entregas/agenda': 'agenda',
  'cardapio/imagens': 'imagem',
  'producao/planejamento': 'planejamento',
  'financeiro/visao-geral': 'grafico-linha',
  'financeiro/pagar': 'recibo',
  'financeiro/receber': 'entrada',
  'financeiro/extrato': 'extrato',
  'financeiro/relatorios': 'grafico-barras',
  'financeiro/categorias': 'etiqueta',
  'financeiro/centros-custo': 'alvo',
  'configuracoes/canais': 'canal',
  'configuracoes/integracoes': 'plug',
  'configuracoes/impressao': 'printer',
  'configuracoes/usuarios': 'usuarios',
  'configuracoes/auditoria': 'historico',
}

export const CHAVES_DE_MARCA = Object.keys(MARCA_DO_CONSOLE)
export const CHAVES_DE_ICONE = Object.keys(ICONE_DO_CONSOLE)
export const NOMES_DE_MARCA = Object.values(MARCA_DO_CONSOLE)
export const NOMES_DE_ICONE = Object.values(ICONE_DO_CONSOLE)

// `marca:producao` ou `icone:printer`; null quando a chave não tem entrada.
const especificacaoDe = (chave) => {
  if (MARCA_DO_CONSOLE[chave]) return `marca:${MARCA_DO_CONSOLE[chave]}`
  if (ICONE_DO_CONSOLE[chave]) return `icone:${ICONE_DO_CONSOLE[chave]}`
  return null
}

export const iconeDoModulo = (moduloId) => especificacaoDe(moduloId)
export const iconeDaTela = (moduloId, telaId) => especificacaoDe(`${moduloId}/${telaId}`) ?? iconeDoModulo(moduloId)

// `marca:producao` vira { familia: 'marca', nome: 'producao' }.
export function partirIcone(especificacao) {
  if (!especificacao) return null
  const [familia, nome] = especificacao.split(':')
  return { familia, nome }
}

// Os desenhos de preparo da marca exigem 32 px no manifesto V4 para manter os detalhes.
export const tamanhoMinimoDoIcone = (especificacao) =>
  ['marca:producao', 'marca:pedido-em-preparo'].includes(especificacao) ? 32 : 24
