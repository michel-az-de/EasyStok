import { Icone } from './Icone'
import { MarcaIcone } from './MarcaIcone'

const MODULOS = {
  cardapio: 'produto', producao: 'producao', atendimento: 'mensagem', cozinha: 'pedido-em-preparo',
  financeiro: 'financeiro', campanhas: 'cupom', configuracoes: 'configuracoes', entregas: 'entrega',
}
const TELAS = {
  itens: 'produto', categorias: 'grade', estoque: 'estoque', producao: 'producao',
  insumos: 'ingredientes', receitas: 'receita', planejamento: 'horarios', perdas: 'atencao',
  balcao: 'mensagem', horarios: 'horarios', respostas: 'mensagem', fila: 'pedido-em-preparo',
  caixa: 'caixa', fidelidade: 'cupom', canais: 'mensagem', integracoes: 'link',
  painel: 'entrega', janelas: 'horarios',
  'visao-geral': 'financeiro', pagar: 'caixa', receber: 'financeiro', extrato: 'receita',
  relatorios: 'grade', 'centros-custo': 'configuracoes',
}

// Os desenhos de preparo exigem 32 px no manifesto V4 para manter os detalhes.
export function IconeDoModulo({ modulo, telaId, tamanho = 24 }) {
  const nome = TELAS[telaId] ?? MODULOS[modulo.id]
  if (!nome) return <Icone nome={modulo.icone} tamanho={tamanho} />
  const minimo = nome === 'producao' || nome === 'pedido-em-preparo' ? 32 : 24
  return <MarcaIcone nome={nome} tamanho={Math.max(tamanho, minimo)} />
}

