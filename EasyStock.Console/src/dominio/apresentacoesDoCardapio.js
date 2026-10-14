// Mesmo catálogo autorado de src/ui/product-art.js do site Casa da Baba.
// Agrupamento só de apresentação: cada objeto e SKU continua sendo o original.
const RECEITAS = new Set([
  'Lasanha Bolonhesa Clássica', 'Lasanha Verde com Bolonhesa',
  'Talharim', 'Fettuccine', 'Espaguete', 'Linguini',
  'Ravioli de Limão Siciliano e Mussarela', 'Ravioli de Ricota e Espinafre',
  'Ravioli de Abóbora e Alho-Poró', 'Ravioli Tricolore de Peito de Peru Defumado',
  'Ravioli Tricolore de Alho-Poró Cremoso', 'Tortellini de Costela',
  'Sorrentino de Ricota e Espinafre', 'Capeletti de Carne', 'Capeletti de Frango',
  'Nhoque Artesanal', 'Panqueca de Carne', 'Panqueca de Frango',
  'Molho Pomodoro', 'Molho Bolonhesa', 'Molho Bechamel',
  'Pesto de Manjericão', 'Manteiga de Sálvia', 'Fonduta de Parmesão',
])

export function agruparApresentacoes(itens) {
  const grupos = new Map()
  for (const item of itens) {
    const nome = String(item.nome || '').trim()
    const peso = String(item.porcao || '').trim()
    const receita = peso && nome.endsWith(' ' + peso) ? nome.slice(0, -(peso.length + 1)).trim() : nome
    // Porções nativas já têm identidade própria prato~porção e mantêm a UI existente.
    const conhecida = peso && RECEITAS.has(receita) && !item.porcoes?.length
    const chave = conhecida ? `${item.categoria}:${receita}` : item.sku
    if (!grupos.has(chave)) grupos.set(chave, { nome: conhecida ? receita : item.nome, itens: [] })
    grupos.get(chave).itens.push(item)
  }
  return [...grupos.values()]
}
