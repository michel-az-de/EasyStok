import { progressoDasReceitas } from './receitas'

// #2362: a configuração da Produção segue a ordem da dependência (Insumos → Receitas), e não a do
// menu. Enquanto faltar algo, o módulo mostra os passos no topo; com tudo pronto, o checklist some.
// O mínimo do prato fica de fora: ainda não há tela no console que o edite (revisão de UX, item 8).
// Separado do componente para a prova rodar sem React.

function passoInsumos(insumos) {
  const semCusto = insumos.filter((i) => i.custo == null).length
  const n = insumos.length
  const detalhe = n === 0 ? 'Nenhum insumo cadastrado'
    : semCusto > 0 ? `${n} ${n === 1 ? 'insumo' : 'insumos'}, ${semCusto} sem custo`
      : `${n} ${n === 1 ? 'insumo' : 'insumos'} com custo`
  return { id: 'insumos', rotulo: 'Cadastrar insumos e custos', tela: 'insumos', feito: n > 0 && semCusto === 0, detalhe }
}

function passoReceitas(receitas) {
  const { comReceita, total } = progressoDasReceitas(receitas)
  const detalhe = total === 0 ? 'Nenhum prato no cardápio' : `${comReceita} de ${total} ${total === 1 ? 'prato' : 'pratos'} com receita`
  return { id: 'receitas', rotulo: 'Montar receitas', tela: 'receitas', feito: total > 0 && comReceita === total, detalhe }
}

export function configuracaoDaProducao({ insumos = [], receitas = [] }) {
  const passos = [passoInsumos(insumos), passoReceitas(receitas)]
  const ativo = passos.find((p) => !p.feito)?.id ?? null
  return { passos, ativo, visivel: ativo !== null }
}
