// #2855: o desenho do fluxo do Assistente como canvas. Função pura: recebe o fluxo da API, a orientação e os
// grupos abertos e devolve nós com posição (x, y) e arestas. Quem desenha (CanvasDoFluxo) só liga isto ao React Flow.
//
// O eixo PRINCIPAL é o do turno (horizontal: da esquerda para a direita; vertical: de cima para baixo). O eixo
// CRUZADO é o outro. Cada nó fica numa (coluna `m`, faixa `c`): m anda no eixo principal, c no cruzado.
//
//   faixa -2,6     verificações abertas, numa linha só no eixo principal (1..N, sem serpentina)
//   faixa -1       Equipe, entre Verificações e Assistente
//   faixa  0       Mensagem do cliente > Verificações > Assistente IA > Resposta ao cliente
//   faixa +1       sub-nós do Assistente: Instruções, Roteiro, Ferramentas (arestas tracejadas, como o AI Agent do n8n)
//   faixa +2...    roteiro aberto (corrente, coluna 2) e ferramentas abertas (grade, colunas 3 em diante)

export const ORIENTACOES = Object.freeze({ horizontal: 'horizontal', vertical: 'vertical' })
export const GRUPOS = Object.freeze({ portoes: 'grupo:portoes', roteiro: 'grupo:roteiro', ferramentas: 'grupo:ferramentas' })

// Texto de nó nunca vira reticências: título em até 2 linhas e subtítulo curto numa só, então a altura comporta as 3.
export const NO_LARGURA = 208
export const NO_ALTURA = 72
const FOLGA_PRINCIPAL = { horizontal: 52, vertical: 44 }
const FOLGA_CRUZADA = { horizontal: 40, vertical: 28 }
const PORTOES_POR_LINHA = 4
const FERRAMENTAS_POR_LINHA = { horizontal: 4, vertical: 6 }

// Lados físicos do nó por orientação: onde o eixo principal entra e sai e onde o cruzado começa e termina.
const LADOS = Object.freeze({
  horizontal: { entra: 'left', sai: 'right', cruzaIni: 'top', cruzaFim: 'bottom' },
  vertical: { entra: 'top', sai: 'bottom', cruzaIni: 'left', cruzaFim: 'right' },
})

/** Quantas ferramentas ligadas e quantas existem. */
export function contagemDasFerramentas(ferramentas = []) {
  return { ligadas: ferramentas.filter((f) => f.ligada).length, total: ferramentas.length }
}

/** Id do nó a partir do grupo aberto ou fechado: 'portoes' vira 'grupo:portoes'. */
export function idDoGrupo(chave) {
  return GRUPOS[chave] ?? null
}

/** "consultar_pedido" vira "Consultar pedido": o nome técnico não aparece na tela. */
export function rotuloDaFerramenta(nome = '') {
  const texto = String(nome).replaceAll('_', ' ').trim()
  return texto ? texto[0].toLocaleUpperCase('pt-BR') + texto.slice(1) : ''
}

// Dados de cada nó. `subtitulo` é curto (cabe numa linha do nó); a frase inteira vai em `descricao`, que o nó mostra
// no `title` e a lista para leitor de tela (ordemDoFluxo) usa.
const DADOS_DA_SAIDA = { tipo: 'ponta', icone: 'circle-check', titulo: 'Resposta ao cliente', subtitulo: 'WhatsApp', descricao: 'Resposta ao cliente: sai pelo WhatsApp' }
const DADOS_DA_EQUIPE = {
  tipo: 'equipe', icone: 'usuarios', titulo: 'Equipe', subtitulo: 'Assume a conversa',
  descricao: 'Equipe: recebe a conversa quando uma verificação barra ou o Assistente pede ajuda',
}
const dadosDaEntrada = () => ({ tipo: 'ponta', icone: 'inbox', titulo: 'Mensagem do cliente', subtitulo: 'WhatsApp', descricao: 'Mensagem do cliente: chega pelo WhatsApp' })
const dadosDeVerificacoes = (portoes, aberto) => ({
  tipo: 'grupo', icone: 'lock', titulo: 'Verificações', subtitulo: `${portoes.length} etapas`, grupo: 'portoes', aberto: aberto.has('portoes'),
  descricao: `Verificações: ${portoes.length} etapas fixas, sempre na mesma ordem`,
})
const dadosDoAssistente = (fluxo) => ({
  tipo: 'assistente', icone: 'sparkles', titulo: 'Assistente IA', subtitulo: fluxo?.ativo ? 'Ligado' : 'Desligado',
  descricao: `Assistente IA: ${fluxo?.ativo ? 'ligado' : 'desligado'} na loja`,
})
const dadosDasInstrucoes = (fluxo) => ({
  tipo: 'prompt', icone: 'settings', titulo: 'Instruções da loja',
  descricao: `Instruções da loja: ${(fluxo?.prompt ?? '').length.toLocaleString('pt-BR')} caracteres`,
})
const dadosDoRoteiro = (roteiro, aberto) => {
  const ativas = roteiro.filter((e) => e.ativo).length
  return {
    tipo: 'grupo', icone: 'extrato', titulo: 'Roteiro', subtitulo: `${ativas} de ${roteiro.length} etapas`, grupo: 'roteiro', aberto: aberto.has('roteiro'),
    descricao: `Roteiro: ${ativas} de ${roteiro.length} etapas ligadas`,
  }
}
const dadosDasFerramentas = (ligadas, total, aberto) => ({
  tipo: 'grupo', icone: 'plug', titulo: 'Ferramentas', subtitulo: `${ligadas} de ${total} ligadas`, grupo: 'ferramentas', aberto: aberto.has('ferramentas'),
  descricao: `Ferramentas: ${ligadas} de ${total} ligadas`,
})
const dadosDoPortao = (p, i) => ({
  tipo: 'portao', icone: 'lock', titulo: p.titulo, subtitulo: p.escala ? 'Passa à equipe' : 'Só o robô para', escala: Boolean(p.escala), ordem: i + 1,
  descricao: `${i + 1}. ${p.titulo}: ${p.escala ? 'quando barra, passa a conversa para a equipe' : 'quando barra, só o robô deixa de responder'}`,
})
const dadosDaEtapa = (etapa, i) => {
  const nome = etapa.nome || 'Sem nome'
  return {
    tipo: 'roteiro', icone: 'extrato', titulo: `${i + 1}. ${nome}`, nome, ativo: etapa.ativo, desligado: !etapa.ativo, indice: i,
    descricao: `${i + 1}. ${nome}${etapa.instrucao ? `: ${etapa.instrucao}` : ''}`,
  }
}
const dadosDaFerramenta = (f) => {
  const titulo = rotuloDaFerramenta(f.nome)
  return {
    tipo: 'ferramenta', icone: f.travada ? 'lock' : 'plug', nome: f.nome, titulo, ligada: Boolean(f.ligada), travada: Boolean(f.travada), desligado: !f.ligada,
    descricao: f.descricao ? `${titulo}: ${f.descricao}` : titulo,
  }
}

/** A ordem do turno como lista de { id, texto }, para o leitor de tela: o desenho em si não diz a ordem. */
export function ordemDoFluxo(desenho) {
  const dados = new Map(desenho.nos.map((n) => [n.id, n.dados]))
  const comPrefixo = (prefixo) => desenho.nos.filter((n) => n.id.startsWith(prefixo)).map((n) => n.id)
  const ids = ['entrada', GRUPOS.portoes, ...comPrefixo('portao:'), 'assistente', 'prompt', GRUPOS.roteiro, ...comPrefixo('roteiro:'),
    GRUPOS.ferramentas, ...comPrefixo('ferramenta:'), 'saida', 'equipe']
  return ids.filter((id) => dados.has(id)).map((id) => ({ id, texto: dados.get(id).descricao ?? dados.get(id).titulo }))
}

/**
 * @param {{ fluxo: object, roteiro?: Array, orientacao?: string, abertos?: Iterable<string> }} entrada
 * `abertos` traz as chaves dos grupos abertos: 'portoes', 'roteiro', 'ferramentas'.
 * @returns {{ nos: Array, arestas: Array, orientacao: string }}
 */
export function desenharFluxo({ fluxo, roteiro = fluxo?.roteiro ?? [], orientacao = ORIENTACOES.horizontal, abertos = [], compacto = false }) {
  const o = orientacao === ORIENTACOES.vertical ? ORIENTACOES.vertical : ORIENTACOES.horizontal
  if (compacto && o === ORIENTACOES.vertical) return desenharEmColuna({ fluxo, roteiro, abertos })
  const aberto = new Set(abertos)
  const lado = LADOS[o]
  const horizontal = o === ORIENTACOES.horizontal
  const passoPrincipal = (horizontal ? NO_LARGURA : NO_ALTURA) + FOLGA_PRINCIPAL[o]
  const passoCruzado = (horizontal ? NO_ALTURA : NO_LARGURA) + FOLGA_CRUZADA[o]
  const ponto = (m, c) => (horizontal ? { x: m * passoPrincipal, y: c * passoCruzado } : { x: c * passoCruzado, y: m * passoPrincipal })

  const nos = []
  const arestas = []
  const no = (id, m, c, dados) => nos.push({ id, ...ponto(m, c), dados: { id, ...dados } })
  const aresta = (origem, origemLado, destino, destinoLado, extra = {}) => arestas.push({
    id: `${origem}>${destino}`, origem, origemLado, destino, destinoLado, ...extra,
  })
  const principal = (origem, destino, extra) => aresta(origem, lado.sai, destino, lado.entra, extra)
  const filho = (origem, destino, extra) => aresta(origem, lado.cruzaFim, destino, lado.cruzaIni, { tracejada: true, ...extra })

  const portoes = fluxo?.portoes ?? []
  const ferramentas = fluxo?.ferramentas ?? []
  const { ligadas, total } = contagemDasFerramentas(ferramentas)

  no('entrada', 0, 0, dadosDaEntrada())
  no(GRUPOS.portoes, 1, 0, dadosDeVerificacoes(portoes, aberto))
  no('assistente', 2, 0, dadosDoAssistente(fluxo))
  no('saida', 3, 0, DADOS_DA_SAIDA)
  no('equipe', 1.5, -1.5, DADOS_DA_EQUIPE)

  principal('entrada', GRUPOS.portoes)
  principal(GRUPOS.portoes, 'assistente')
  principal('assistente', 'saida')
  aresta(GRUPOS.portoes, lado.cruzaIni, 'equipe', lado.entra, { rotulo: 'barra', tom: 'equipe' })
  aresta('assistente', lado.cruzaIni, 'equipe', lado.sai, { rotulo: 'pede ajuda', tom: 'equipe' })

  // Sub-nós do Assistente.
  no('prompt', 1, 1, dadosDasInstrucoes(fluxo))
  no(GRUPOS.roteiro, 2, 1, dadosDoRoteiro(roteiro, aberto))
  no(GRUPOS.ferramentas, 3, 1, dadosDasFerramentas(ligadas, total, aberto))
  filho('assistente', 'prompt')
  filho('assistente', GRUPOS.roteiro)
  filho('assistente', GRUPOS.ferramentas)

  // Verificações abertas: serpentina de 4 por linha, acima da Equipe (faixas -2,6, -3,6, -4,6). Uma linha só de 11
  // não cabe no quadro sem cortar o grupo seguinte, que o próximo toque precisa alcançar.
  if (aberto.has('portoes')) {
    portoes.forEach((p, i) => {
      const linha = Math.floor(i / PORTOES_POR_LINHA)
      const posicao = i % PORTOES_POR_LINHA
      const volta = linha % 2 === 1
      const id = `portao:${p.codigo}`
      no(id, 1 + (volta ? PORTOES_POR_LINHA - 1 - posicao : posicao), -2.6 - linha, dadosDoPortao(p, i))
      if (i === 0) {
        aresta(GRUPOS.portoes, lado.cruzaIni, id, lado.cruzaFim, { tracejada: true })
        return
      }
      const antes = `portao:${portoes[i - 1].codigo}`
      if (posicao === 0) aresta(antes, lado.cruzaIni, id, lado.cruzaFim)
      else if (volta) aresta(antes, lado.entra, id, lado.sai)
      else principal(antes, id)
    })
  }

  // Roteiro aberto: corrente no eixo cruzado, coluna 2.
  if (aberto.has('roteiro')) {
    roteiro.forEach((etapa, i) => {
      const id = `roteiro:${i}`
      no(id, 2, 2 + i, dadosDaEtapa(etapa, i))
      if (i === 0) filho(GRUPOS.roteiro, id)
      else aresta(`roteiro:${i - 1}`, lado.cruzaFim, id, lado.cruzaIni)
    })
  }

  // Ferramentas abertas: caixa de ferramentas em grade a partir da coluna 3. Não têm ordem entre si, então só a
  // primeira linha pendura no grupo e não há seta de uma ferramenta para outra.
  if (aberto.has('ferramentas')) {
    const porLinha = FERRAMENTAS_POR_LINHA[o]
    ferramentas.forEach((f, i) => {
      const id = `ferramenta:${f.nome}`
      no(id, 3 + (i % porLinha), 2 + Math.floor(i / porLinha), dadosDaFerramenta(f))
      if (i < porLinha) filho(GRUPOS.ferramentas, id)
    })
  }

  return { nos, arestas, orientacao: o }
}

// #2857: no celular o vertical vira uma coluna só, como um sumário: cada grupo aberto cresce PARA BAIXO, recuado
// sob quem o contém, e nada cresce para o lado. A largura total cabe numa tela de 375 px sem diminuir o texto.
//   trilho da esquerda   liga o turno: Mensagem > Verificações > Assistente IA > Resposta
//   tronco recuado       liga cada grupo aos seus itens (sai da alça `tronco`, embaixo e à esquerda do nó)
//   corredor da direita  leva Verificações e Assistente à Equipe, que fica no fim, alinhada ao recuo maior
export const RECUO_DA_COLUNA = 32
const FOLGA_DA_COLUNA = 24

function desenharEmColuna({ fluxo, roteiro, abertos }) {
  const aberto = new Set(abertos)
  const nos = []
  const arestas = []
  let linha = 0
  const no = (id, nivel, dados) => {
    nos.push({ id, x: nivel * RECUO_DA_COLUNA, y: linha * (NO_ALTURA + FOLGA_DA_COLUNA), dados: { id, ...dados } })
    linha += 1
  }
  const aresta = (origem, origemLado, destino, destinoLado, extra = {}) => arestas.push({
    id: `${origem}>${destino}`, origem, origemLado, destino, destinoLado, ...extra,
  })
  const trilho = (origem, destino) => aresta(origem, 'left', destino, 'left')
  const tronco = (origem, destino) => aresta(origem, 'tronco', destino, 'left', { tracejada: true, semSeta: true })

  const portoes = fluxo?.portoes ?? []
  const ferramentas = fluxo?.ferramentas ?? []
  const { ligadas, total } = contagemDasFerramentas(ferramentas)

  no('entrada', 0, dadosDaEntrada())
  no(GRUPOS.portoes, 0, dadosDeVerificacoes(portoes, aberto))
  trilho('entrada', GRUPOS.portoes)
  if (aberto.has('portoes')) {
    portoes.forEach((p, i) => {
      const id = `portao:${p.codigo}`
      no(id, 1, dadosDoPortao(p, i))
      tronco(GRUPOS.portoes, id)
    })
  }

  no('assistente', 0, dadosDoAssistente(fluxo))
  trilho(GRUPOS.portoes, 'assistente')
  no('prompt', 1, dadosDasInstrucoes(fluxo))
  tronco('assistente', 'prompt')
  no(GRUPOS.roteiro, 1, dadosDoRoteiro(roteiro, aberto))
  tronco('assistente', GRUPOS.roteiro)
  if (aberto.has('roteiro')) {
    roteiro.forEach((etapa, i) => {
      const id = `roteiro:${i}`
      no(id, 2, dadosDaEtapa(etapa, i))
      tronco(GRUPOS.roteiro, id)
    })
  }
  no(GRUPOS.ferramentas, 1, dadosDasFerramentas(ligadas, total, aberto))
  tronco('assistente', GRUPOS.ferramentas)
  if (aberto.has('ferramentas')) {
    ferramentas.forEach((f) => {
      const id = `ferramenta:${f.nome}`
      no(id, 2, dadosDaFerramenta(f))
      tronco(GRUPOS.ferramentas, id)
    })
  }

  no('saida', 0, DADOS_DA_SAIDA)
  trilho('assistente', 'saida')
  // A Equipe fica no fim, alinhada ao recuo maior: o corredor da direita passa por fora de todos os nós.
  no('equipe', 2, DADOS_DA_EQUIPE)
  aresta(GRUPOS.portoes, 'right', 'equipe', 'right', { tom: 'equipe' })
  aresta('assistente', 'right', 'equipe', 'right', { tom: 'equipe' })

  return { nos, arestas, orientacao: ORIENTACOES.vertical, compacto: true }
}

/** Ids a enquadrar quando um grupo abre: os três grupos (para o clique no seguinte continuar a um toque) e os nós novos. */
export function idsDoGrupo(desenho, chave) {
  const prefixo = { portoes: 'portao:', roteiro: 'roteiro:', ferramentas: 'ferramenta:' }[chave]
  if (!prefixo) return []
  const raiz = GRUPOS[chave]
  const grupos = Object.values(GRUPOS).filter((id) => id !== raiz && desenho.nos.some((n) => n.id === id))
  return [raiz, ...grupos, ...desenho.nos.filter((n) => n.id.startsWith(prefixo)).map((n) => n.id)]
}
