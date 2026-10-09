// Módulos do ERP da Casa da Baba (ADR-0056, plano docs/plan/erp-casa-da-baba/), na
// ordem e com a numeração do plano. O hall (#1447, homologação de 07/10) mostra
// os oito; cada módulo tem o próprio menu (as `telas`) e o módulo ativo vem da
// rota, nunca de estado guardado (ADR-0046 D1).
//
// Tela de `operacao` é a tela de trabalho de hoje (Balcão, fila da Cozinha,
// painel de Entregas) e abre sem moldura, como sempre abriu. Tela de `ajuste`
// abre dentro da moldura do módulo e monta uma aba da antiga Gestão (`aba`).
// `soApi`: só existe no modo API (no modo demonstração não há o que gravar).
//
// Módulo sem tela no console aparece "Em breve" e não navega. Quem decide se o
// perfil pode abrir o módulo é a API (M0.3, ainda não entregue): esta lista não
// esconde nada por nível.

import { partesNoFuso } from './formato'

export const TELA_OPERACAO = 'operacao'
export const TELA_AJUSTE = 'ajuste'

export const MODULOS = [
  {
    numero: 1, id: 'cardapio', nome: 'Cardápio', icone: 'cardapio',
    resumo: 'Pratos, preços, combos e fotos',
    // M1.1 (#1481): a gestão do cardápio lê e grava no EasyStok; no modo demonstração o
    // cardápio continua no Gerir do balcão.
    telas: [
      { id: 'itens', rotulo: 'Itens do cardápio', tipo: TELA_AJUSTE, aba: 'cardapio', soApi: true },
      // M1.3 (#1483): as categorias (seções) que o cliente vê.
      { id: 'categorias', rotulo: 'Categorias', tipo: TELA_AJUSTE, aba: 'categorias', soApi: true },
    ],
  },
  {
    numero: 2, id: 'producao', nome: 'Produção', icone: 'flask-conical',
    resumo: 'Insumos, fichas técnicas e perdas',
    // M2.1 (#1490): o estoque do dia lê do EasyStok; na demonstração segue a aba da Cozinha.
    telas: [
      { id: 'estoque', rotulo: 'Estoque do dia', tipo: TELA_AJUSTE, aba: 'estoque-do-dia', soApi: true },
      // M2.2 (#1491): lançar a produção do dia por prato, com as etiquetas.
      { id: 'producao', rotulo: 'Produção do dia', tipo: TELA_AJUSTE, aba: 'producao-do-dia', soApi: true },
      // M2.3 (#1496): insumos (intermediário e embalagem), com o que comprar.
      { id: 'insumos', rotulo: 'Insumos', tipo: TELA_AJUSTE, aba: 'insumos', soApi: true },
      // M2.4a (#1498): a receita de cada prato, com o custo por porção.
      { id: 'receitas', rotulo: 'Receitas', tipo: TELA_AJUSTE, aba: 'receitas', soApi: true },
    ],
  },
  {
    numero: 3, id: 'atendimento', nome: 'Atendimento', icone: 'conversa',
    resumo: 'Conversas, pedidos e clientes',
    telas: [
      { id: 'balcao', rotulo: 'Balcão', tipo: TELA_OPERACAO },
      { id: 'horarios', rotulo: 'Horários e mensagens', tipo: TELA_AJUSTE, aba: 'atendimento', soApi: true },
      // #1441: criar, editar e arquivar respostas prontas; ligar e escrever as automáticas.
      { id: 'respostas', rotulo: 'Respostas e automáticas', tipo: TELA_AJUSTE, aba: 'respostas' },
    ],
  },
  {
    numero: 4, id: 'cozinha', nome: 'Cozinha', icone: 'cooking-pot',
    resumo: 'Fila de preparo e produção do dia',
    telas: [
      { id: 'fila', rotulo: 'Fila de preparo', tipo: TELA_OPERACAO },
      { id: 'producao', rotulo: 'Produção e cardápio do dia', tipo: TELA_AJUSTE, aba: 'producao' },
    ],
  },
  {
    numero: 5, id: 'financeiro', nome: 'Financeiro', icone: 'dollar-sign',
    resumo: 'Caixa do dia, vendas e fechamento',
    telas: [
      { id: 'caixa', rotulo: 'Caixa do dia', tipo: TELA_AJUSTE, aba: 'caixa' },
    ],
  },
  {
    numero: 6, id: 'campanhas', nome: 'Campanhas', icone: 'presente',
    resumo: 'Fidelidade, cupons e promoções',
    telas: [
      { id: 'fidelidade', rotulo: 'Fidelidade e cupons', tipo: TELA_AJUSTE, aba: 'fidelidade' },
    ],
  },
  {
    numero: 7, id: 'configuracoes', nome: 'Configurações', icone: 'automacao',
    resumo: 'Canais, integrações e usuários',
    telas: [
      { id: 'canais', rotulo: 'Canais', tipo: TELA_AJUSTE, aba: 'canais', soApi: true },
      { id: 'integracoes', rotulo: 'Integrações de entrega', tipo: TELA_AJUSTE, aba: 'integracoes' },
    ],
  },
  {
    numero: 8, id: 'entregas', nome: 'Entregas', icone: 'moto',
    resumo: 'Janelas, entregadores e viagens',
    telas: [
      { id: 'painel', rotulo: 'Painel de entregas', tipo: TELA_OPERACAO },
      { id: 'janelas', rotulo: 'Janelas de entrega', tipo: TELA_AJUSTE, aba: 'janelas' },
    ],
  },
]

export const moduloPorId = (id) => MODULOS.find((m) => m.id === id) ?? null

export const hashDoModulo = (moduloId, telaId) => `#/m/${moduloId}${telaId ? '/' + telaId : ''}`

// Menu do módulo no modo atual: tela só da API some no modo demonstração.
export const telasDoMenu = (modulo, { fonteApi = false } = {}) =>
  (modulo?.telas ?? []).filter((t) => fonteApi || !t.soApi)

// Cartões do hall: disponível = tem pelo menos uma tela no modo atual.
export function modulosDoHall({ fonteApi = false } = {}) {
  return MODULOS.map((modulo) => {
    const telas = telasDoMenu(modulo, { fonteApi })
    const disponivel = telas.length > 0
    return {
      ...modulo,
      telas,
      disponivel,
      href: disponivel ? hashDoModulo(modulo.id) : null,
    }
  })
}

// Busca somente no catálogo de navegação, sem consultar dados de clientes.
const textoDeBusca = (valor) => valor.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLocaleLowerCase('pt-BR').trim()
export function filtrarModulos(modulos, busca) {
  const termos = textoDeBusca(busca).split(/\s+/).filter(Boolean)
  return modulos.filter((modulo) => {
    const texto = textoDeBusca([modulo.nome, modulo.resumo, ...modulo.telas.map((t) => t.rotulo)].join(' '))
    return termos.every((termo) => texto.includes(termo))
  })
}

// Saudação do topo do hall, pela hora da loja (não a do navegador nem a UTC).
export function saudacaoDoHall(agora, nome) {
  const { horas } = partesNoFuso(agora)
  const periodo = horas < 12 ? 'Bom dia' : horas < 18 ? 'Boa tarde' : 'Boa noite'
  const primeiro = (nome ?? '').trim().split(/\s+/)[0]
  return primeiro ? `${periodo}, ${primeiro}` : periodo
}
