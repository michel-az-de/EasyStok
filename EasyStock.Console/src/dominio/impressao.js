// O que vai para o papel (rodada 11, issue #9): canhoto da comanda na bobina
// térmica de 80 mm e rota da viagem em A4. Puro, sem React: recebe o mesmo
// dado que a tela já mostra e devolve o documento pronto para `montarPdf`.
// Sempre preto no branco, independente do tema da tela.
import { dataHora, horaCurta, listaEmPortugues, plural } from './formato'
import { A4, LARGURA_BOBINA, larguraDoTexto, mm, montarPdf, quebrarLinhas } from './pdf'
import { agruparPorLinha, itensDetalhados, numeroCurto } from './pedido'
import { entregadorDaViagem, marcosDaViagem } from './viagem'
import { resumoDoRoteiro, rotuloDaData, textoDeQuemLeva } from './roteiroDoDia'

// Caneta que desce a folha: guarda o `y` atual e empilha os desenhos. A
// altura da linha é 1,3 vez o corpo, a mesma folga do texto corrido da tela.
function caneta(larguraDaPagina, margem) {
  const desenhos = []
  const estado = { y: margem }
  const largura = larguraDaPagina - margem * 2
  const escrever = (texto, { tamanho = 10, fonte = 'regular', x = margem, larguraMax = largura - (x - margem), cinza = 0 } = {}) => {
    const negrito = fonte === 'negrito'
    for (const linha of quebrarLinhas(texto, larguraMax, tamanho, negrito)) {
      estado.y += tamanho
      desenhos.push({ tipo: 'texto', x, y: estado.y, tamanho, fonte, cinza, texto: linha })
      estado.y += tamanho * 0.3
    }
  }
  const direita = (texto, { tamanho = 10, fonte = 'regular', y = estado.y, cinza = 0 } = {}) => {
    const x = larguraDaPagina - margem - larguraDoTexto(texto, tamanho, fonte === 'negrito')
    desenhos.push({ tipo: 'texto', x, y, tamanho, fonte, cinza, texto })
  }
  const traco = ({ tracejada = false, espessura = 0.5, cinza = 0 } = {}) => {
    estado.y += mm(1.5)
    desenhos.push({ tipo: 'linha', x: margem, y: estado.y, x2: larguraDaPagina - margem, y2: estado.y, tracejada, espessura, cinza })
    estado.y += mm(1.5)
  }
  const espaco = (valor) => { estado.y += valor }
  return { desenhos, estado, largura, escrever, direita, traco, espaco }
}

// Canhoto (RN-29, RN-33, US-036): mesmo conteúdo do `PapelCanhoto` da tela,
// número, cliente, janela, endereço e itens agrupados por linha com porção e
// observação (molho mora na observação). Sem preço: não é cupom fiscal. A
// largura é a da bobina, 80 mm; a altura sai do próprio conteúdo, como o
// papel que a térmica corta no fim.
export function documentoDoCanhoto({ pedido, itens, linhas, nomeCliente, endereco, faixa }) {
  const margem = mm(4)
  const c = caneta(LARGURA_BOBINA, margem)
  const recuo = mm(7)

  c.escrever('CANHOTO DE PEDIDO, NÃO É CUPOM FISCAL', { tamanho: 7.5, fonte: 'negrito' })
  c.espaco(mm(1.5))
  c.escrever('Comanda', { tamanho: 16, fonte: 'negrito', larguraMax: c.largura - mm(24) })
  c.direita(`Nº ${numeroCurto(pedido.numero)}`, { tamanho: 16, fonte: 'negrito', y: c.estado.y - 16 * 0.3 })
  c.escrever(`${nomeCliente} · Janela ${faixa ?? 'não escolhida'}`, { tamanho: 10.5, fonte: 'negrito' })
  if (pedido.cobranca?.criadaEm) c.escrever(`Enviada ${horaCurta(pedido.cobranca.criadaEm)}`, { tamanho: 9.5 })
  if (endereco) c.escrever(endereco, { tamanho: 9.5 })
  c.traco({ tracejada: true })

  const grupos = agruparPorLinha(itens, linhas)
  grupos.forEach((grupo, indice) => {
    c.escrever(grupo.rotulo.toUpperCase(), { tamanho: 8.5, fonte: 'negrito' })
    c.espaco(mm(0.8))
    for (const linha of grupo.itens) {
      c.escrever(`${linha.qtd}×`, { tamanho: 11, fonte: 'negrito' })
      c.estado.y -= 11 * 1.3
      c.escrever(linha.produto?.nome ?? linha.sku, { tamanho: 11, fonte: 'negrito', x: margem + recuo })
      if (linha.produto?.porcao) c.escrever(linha.produto.porcao, { tamanho: 9.5, x: margem + recuo })
      if (linha.obs) c.escrever(linha.obs, { tamanho: 9.5, fonte: 'italico', x: margem + recuo })
      c.espaco(mm(1.2))
    }
    if (indice < grupos.length - 1) c.traco({ tracejada: true })
  })
  if (grupos.length === 0) c.escrever('Sem itens na comanda.', { tamanho: 10 })

  const altura = c.estado.y + mm(6)
  return {
    nomeArquivo: `canhoto-${numeroCurto(pedido.numero)}.pdf`,
    titulo: `Canhoto ${numeroCurto(pedido.numero)}`,
    paginas: [{ largura: LARGURA_BOBINA, altura, desenhos: c.desenhos }],
  }
}

function textoDoEntregador(viagem) {
  const quem = entregadorDaViagem(viagem)
  if (!quem) return 'Entregador a definir'
  if (quem.tipo === 'propria') return 'Eu mesma levo'
  return `Entregador ${quem.nome}`
}

// Rota da viagem em A4 (seção 6, "Gerar rota"): a mesma ordem das paradas e a
// mesma chegada prevista da modal, mais o que quem leva precisa na rua e na
// conferência das sacolas: telefone, janela, número do pedido e itens. Parada
// nunca parte entre duas páginas; cada página leva o próprio rodapé.
export function documentoDaRota({ viagem, janelas, agora, constantes, enderecoDaCasa, cardapio }) {
  const margem = mm(18)
  const paradas = viagem.paradas
  const comFaixa = paradas.map((c) => ({ id: c.id, faixa: janelas.find((j) => j.id === c.pedido.janela)?.faixa }))
  const marcos = marcosDaViagem(comFaixa, agora, constantes)
  const hora = (ms) => (ms == null ? 'a definir' : horaCurta(new Date(ms).toISOString()))

  const paginas = []
  let c = null
  const novaPagina = () => {
    c = caneta(A4.largura, margem)
    paginas.push({ largura: A4.largura, altura: A4.altura, desenhos: c.desenhos })
    return c
  }
  novaPagina()
  const fimUtil = A4.altura - margem - mm(8)

  c.escrever('Rota da viagem', { tamanho: 20, fonte: 'negrito' })
  c.direita('Casa da Baba', { tamanho: 11, fonte: 'negrito', y: c.estado.y - 20 * 0.3 })
  c.espaco(mm(1))
  c.escrever(
    [
      `${paradas.length} ${paradas.length === 1 ? 'parada' : 'paradas'}`,
      `Saída prevista ${hora(marcos.sair)}`,
      textoDoEntregador(viagem),
    ].join(' · '),
    { tamanho: 11 },
  )
  if (enderecoDaCasa) c.escrever(`Sai de ${enderecoDaCasa}`, { tamanho: 10, cinza: 0.2 })
  c.traco({ espessura: 1 })

  const recuo = mm(12)
  const larguraMiolo = c.largura - recuo
  paradas.forEach((conversa, indice) => {
    const parada = marcos.porParada.find((p) => p.id === conversa.id)
    const faixa = comFaixa[indice].faixa
    const itens = itensDetalhados(conversa.pedido, cardapio)
      .map((l) => `${l.qtd}× ${l.produto?.nome ?? l.sku}${l.obs ? ` (${l.obs})` : ''}`).join(', ')
    const dados = [
      `Janela ${faixa ?? 'não escolhida'}`,
      `Pedido ${numeroCurto(conversa.pedido.numero)}`,
      conversa.cliente?.telefone ? `Tel. ${conversa.cliente.telefone}` : null,
    ].filter(Boolean).join(' · ')
    const endereco = conversa.cliente?.endereco ?? 'Endereço não informado'
    const chegada = `Chegada ${hora(parada?.chegadaReal)}${parada?.atrasada ? ', depois da janela' : ''}`

    // Altura do bloco medida antes de desenhar, para decidir a página.
    const alturaDe = (texto, tamanho, negrito = false) =>
      quebrarLinhas(texto, larguraMiolo, tamanho, negrito).length * tamanho * 1.3
    const alturaDoBloco = alturaDe(conversa.nome, 13, true) + alturaDe(endereco, 11.5)
      + alturaDe(dados, 10) + (itens ? alturaDe(itens, 10) : 0) + mm(6)
    if (c.estado.y + alturaDoBloco > fimUtil && indice > 0) novaPagina()

    const topo = c.estado.y
    c.escrever(`${indice + 1}`, { tamanho: 20, fonte: 'negrito', larguraMax: recuo })
    c.estado.y = topo
    const larguraDoNome = larguraMiolo - larguraDoTexto(chegada, 11, true) - mm(4)
    c.escrever(conversa.nome, { tamanho: 13, fonte: 'negrito', x: margem + recuo, larguraMax: larguraDoNome })
    c.direita(chegada, { tamanho: 11, fonte: 'negrito', y: topo + 13 })
    c.escrever(endereco, { tamanho: 11.5, x: margem + recuo })
    c.escrever(dados, { tamanho: 10, x: margem + recuo, cinza: 0.2 })
    if (itens) c.escrever(itens, { tamanho: 10, x: margem + recuo, cinza: 0.2 })
    c.espaco(mm(1))
    if (indice < paradas.length - 1) c.traco({ cinza: 0.55 })
  })
  if (paradas.length === 0) c.escrever('Viagem sem paradas.', { tamanho: 11 })

  const geradaEm = `Gerada em ${dataHora(agora)}`
  paginas.forEach((pagina, indice) => {
    const y = A4.altura - margem + mm(4)
    pagina.desenhos.push({ tipo: 'texto', x: margem, y, tamanho: 8.5, cinza: 0.25, texto: geradaEm })
    const rodape = `Página ${indice + 1} de ${paginas.length}`
    pagina.desenhos.push({
      tipo: 'texto', x: A4.largura - margem - larguraDoTexto(rodape, 8.5), y, tamanho: 8.5, cinza: 0.25, texto: rodape,
    })
  })

  // Dia local (não UTC): rota das 21h ainda é do mesmo dia no nome do arquivo.
  const data = new Date(agora)
  const dia = [data.getFullYear(), data.getMonth() + 1, data.getDate()].map((n) => String(n).padStart(2, '0')).join('-')
  // Id da viagem é UUID no app: os 8 primeiros bastam para separar duas
  // rotas do mesmo dia sem um nome de arquivo de 50 letras.
  const id = String(viagem.id).replace(/[^\w-]/g, '').slice(0, 8)
  return { nomeArquivo: `rota-${dia}-${id}.pdf`, titulo: 'Rota da viagem', paginas }
}

// Atalho dos dois: documento pronto em bytes, que é o que o botão baixa ou
// manda imprimir.
export const pdfDoCanhoto = (dados) => {
  const documento = documentoDoCanhoto(dados)
  return { ...documento, bytes: montarPdf(documento) }
}
export const pdfDaRota = (dados) => {
  const documento = documentoDaRota(dados)
  return { ...documento, bytes: montarPdf(documento) }
}

// Roteiro de entregas do dia (issue #1440): por janela, quantas entregas, cada pedido com
// endereço, situação e quem leva. A4 (paginado, pedido nunca parte entre páginas) ou bobina
// de 80 mm (uma tira só, a altura sai do conteúdo). Só preto: a térmica não tem cinza.
export function documentoDoRoteiro({ roteiro, papel = 'a4', agora }) {
  const bobina = papel === 'bobina'
  const largura = bobina ? LARGURA_BOBINA : A4.largura
  const margem = bobina ? mm(4) : mm(18)
  const t = bobina
    ? { titulo: 14, janela: 11, nome: 10, corpo: 9, apoio: 8.5 }
    : { titulo: 20, janela: 13, nome: 11.5, corpo: 10.5, apoio: 9.5 }
  const paginas = []
  let c = null
  const novaPagina = () => {
    c = caneta(largura, margem)
    paginas.push({ largura, altura: A4.altura, desenhos: c.desenhos })
  }
  novaPagina()
  const fimUtil = A4.altura - margem - mm(8)
  const alturaDe = (texto, tamanho, negrito = false) => quebrarLinhas(texto, c.largura, tamanho, negrito).length * tamanho * 1.3
  // A4: o bloco que não cabe vai inteiro para a página seguinte. Na bobina não há página.
  const caber = (altura) => { if (!bobina && c.estado.y + altura > fimUtil) novaPagina() }

  c.escrever('Roteiro de entregas', { tamanho: t.titulo, fonte: 'negrito', larguraMax: bobina ? c.largura : c.largura - mm(40) })
  if (!bobina) c.direita('Casa da Baba', { tamanho: 11, fonte: 'negrito', y: c.estado.y - t.titulo * 0.3 })
  c.escrever(rotuloDaData(roteiro.data), { tamanho: t.corpo + 1, fonte: 'negrito' })
  c.escrever(`${resumoDoRoteiro(roteiro)}.`, { tamanho: t.corpo })
  if (roteiro.bloqueioDoDia) c.escrever(`Dia bloqueado: ${roteiro.bloqueioDoDia}`, { tamanho: t.corpo, fonte: 'negrito' })
  c.traco({ espessura: 1 })

  roteiro.grupos.forEach((grupo) => {
    const titulo = grupo.chave === 'sem-janela' ? 'Sem janela' : `${grupo.faixa} · ${grupo.label}`
    const quantas = plural(grupo.pedidos.length, 'entrega', 'entregas')
      + (grupo.capacidade ? ` de ${grupo.capacidade} vagas` : '')
    const quem = `Quem leva: ${grupo.quemLeva.length ? listaEmPortugues(grupo.quemLeva) : 'a definir'}`
    caber(alturaDe(titulo, t.janela, true) + alturaDe(quantas, t.corpo) * 2 + mm(14))
    c.espaco(mm(2))
    c.escrever(titulo, { tamanho: t.janela, fonte: 'negrito' })
    c.escrever(quantas, { tamanho: t.corpo, fonte: 'negrito' })
    if (grupo.bloqueio) c.escrever(`Bloqueada: ${grupo.bloqueio}`, { tamanho: t.corpo, fonte: 'negrito' })
    c.escrever(quem, { tamanho: t.corpo })
    if (grupo.pedidos.length === 0) c.escrever('Nenhum pedido nesta janela.', { tamanho: t.corpo })
    c.traco({ tracejada: true })

    grupo.pedidos.forEach((p, indice) => {
      const nome = `${indice + 1}. Nº ${p.numero} · ${p.cliente}${p.apto ? ` · apto ${p.apto}` : ''}`
      const endereco = p.endereco ?? 'Sem endereço no cadastro'
      const situacao = [
        p.pago ? 'Pago' : 'Não pago', p.rotulo,
        p.entregador ? `Leva ${textoDeQuemLeva(p.entregador)}` : null, p.falta,
      ].filter(Boolean).join(' · ')
      caber(alturaDe(nome, t.nome, true) + alturaDe(endereco, t.corpo) + alturaDe(situacao, t.apoio) + mm(4))
      c.escrever(nome, { tamanho: t.nome, fonte: 'negrito' })
      c.escrever(endereco, { tamanho: t.corpo })
      c.escrever(situacao, { tamanho: t.apoio })
      c.espaco(mm(2))
    })
  })

  const geradoEm = `Gerado em ${dataHora(agora)}`
  if (bobina) {
    c.traco({ tracejada: true })
    c.escrever(geradoEm, { tamanho: t.apoio })
    paginas[0].altura = c.estado.y + mm(6)
  } else {
    paginas.forEach((pagina, indice) => {
      const y = A4.altura - margem + mm(4)
      pagina.desenhos.push({ tipo: 'texto', x: margem, y, tamanho: 8.5, texto: geradoEm })
      const rodape = `Página ${indice + 1} de ${paginas.length}`
      pagina.desenhos.push({ tipo: 'texto', x: A4.largura - margem - larguraDoTexto(rodape, 8.5), y, tamanho: 8.5, texto: rodape })
    })
  }
  return {
    nomeArquivo: `roteiro-${roteiro.data}${bobina ? '-80mm' : ''}.pdf`,
    titulo: `Roteiro de entregas ${roteiro.data}`,
    paginas,
  }
}

export const pdfDoRoteiro = (dados) => {
  const documento = documentoDoRoteiro(dados)
  return { ...documento, bytes: montarPdf(documento) }
}
