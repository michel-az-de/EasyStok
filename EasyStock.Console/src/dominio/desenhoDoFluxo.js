// #2855: o desenho do fluxo do Assistente como canvas. Função pura: recebe o fluxo da API, a orientação e os
// grupos abertos e devolve nós com posição (x, y) e arestas. Quem desenha (CanvasDoFluxo) só liga isto ao React Flow.
//
// O eixo PRINCIPAL é o do turno (horizontal: da esquerda para a direita; vertical: de cima para baixo). O eixo
// CRUZADO é o outro. Cada nó fica numa (coluna `m`, faixa `c`): m anda no eixo principal, c no cruzado.
//
//   faixa -4..-2   verificações abertas, em serpentina (4 por linha)
//   faixa -1       Equipe, entre Verificações e Assistente
//   faixa  0       Mensagem do cliente > Verificações > Assistente IA > Resposta ao cliente
//   faixa +1       sub-nós do Assistente: Instruções, Roteiro, Ferramentas (arestas tracejadas, como o AI Agent do n8n)
//   faixa +2...    roteiro aberto (corrente, coluna 2) e ferramentas abertas (grade, colunas 3 em diante)

export const ORIENTACOES = Object.freeze({ horizontal: 'horizontal', vertical: 'vertical' })
export const GRUPOS = Object.freeze({ portoes: 'grupo:portoes', roteiro: 'grupo:roteiro', ferramentas: 'grupo:ferramentas' })

export const NO_LARGURA = 188
export const NO_ALTURA = 64
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

  no('entrada', 0, 0, { tipo: 'ponta', icone: 'inbox', titulo: 'Mensagem do cliente', subtitulo: 'WhatsApp' })
  no(GRUPOS.portoes, 1, 0, {
    tipo: 'grupo', icone: 'lock', titulo: 'Verificações', subtitulo: `${portoes.length} etapas fixas`, grupo: 'portoes', aberto: aberto.has('portoes'),
  })
  no('assistente', 2, 0, { tipo: 'assistente', icone: 'sparkles', titulo: 'Assistente IA', subtitulo: fluxo?.ativo ? 'Ligado na loja' : 'Desligado na loja' })
  no('saida', 3, 0, { tipo: 'ponta', icone: 'circle-check', titulo: 'Resposta ao cliente', subtitulo: 'Sai pelo WhatsApp' })
  no('equipe', 1.5, -1.5, { tipo: 'equipe', icone: 'usuarios', titulo: 'Equipe', subtitulo: 'A conversa fica com você' })

  principal('entrada', GRUPOS.portoes)
  principal(GRUPOS.portoes, 'assistente')
  principal('assistente', 'saida')
  aresta(GRUPOS.portoes, lado.cruzaIni, 'equipe', lado.entra, { rotulo: 'barra', tom: 'equipe' })
  aresta('assistente', lado.cruzaIni, 'equipe', lado.sai, { rotulo: 'pede ajuda', tom: 'equipe' })

  // Sub-nós do Assistente.
  no('prompt', 1, 1, { tipo: 'prompt', icone: 'settings', titulo: 'Instruções da loja', subtitulo: `${(fluxo?.prompt ?? '').length.toLocaleString('pt-BR')} caracteres` })
  no(GRUPOS.roteiro, 2, 1, {
    tipo: 'grupo', icone: 'extrato', titulo: 'Roteiro', subtitulo: `${roteiro.filter((e) => e.ativo).length} de ${roteiro.length} etapas`, grupo: 'roteiro', aberto: aberto.has('roteiro'),
  })
  no(GRUPOS.ferramentas, 3, 1, {
    tipo: 'grupo', icone: 'plug', titulo: 'Ferramentas', subtitulo: `${ligadas} de ${total} ligadas`, grupo: 'ferramentas', aberto: aberto.has('ferramentas'),
  })
  filho('assistente', 'prompt')
  filho('assistente', GRUPOS.roteiro)
  filho('assistente', GRUPOS.ferramentas)

  // Verificações abertas: serpentina de 4 por linha, acima da Equipe (faixas -2,6, -3,6, -4,6).
  if (aberto.has('portoes')) {
    portoes.forEach((p, i) => {
      const linha = Math.floor(i / PORTOES_POR_LINHA)
      const posicao = i % PORTOES_POR_LINHA
      const volta = linha % 2 === 1
      no(`portao:${p.codigo}`, 1 + (volta ? PORTOES_POR_LINHA - 1 - posicao : posicao), -2.6 - linha, {
        tipo: 'portao', icone: 'lock', titulo: p.titulo, subtitulo: p.escala ? 'Barra e passa à equipe' : 'Barra e o robô só não responde',
        escala: Boolean(p.escala), ordem: i + 1,
      })
      const id = `portao:${p.codigo}`
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
      no(id, 2, 2 + i, { tipo: 'roteiro', icone: 'extrato', titulo: `${i + 1}. ${etapa.nome || 'Sem nome'}`, subtitulo: etapa.instrucao, ativo: etapa.ativo, desligado: !etapa.ativo, indice: i })
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
      no(id, 3 + (i % porLinha), 2 + Math.floor(i / porLinha), {
        tipo: 'ferramenta', icone: f.travada ? 'lock' : 'plug', titulo: f.nome, ligada: Boolean(f.ligada), travada: Boolean(f.travada), desligado: !f.ligada,
      })
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

  no('entrada', 0, { tipo: 'ponta', icone: 'inbox', titulo: 'Mensagem do cliente', subtitulo: 'WhatsApp' })
  no(GRUPOS.portoes, 0, {
    tipo: 'grupo', icone: 'lock', titulo: 'Verificações', subtitulo: `${portoes.length} etapas fixas`, grupo: 'portoes', aberto: aberto.has('portoes'),
  })
  trilho('entrada', GRUPOS.portoes)
  if (aberto.has('portoes')) {
    portoes.forEach((p, i) => {
      const id = `portao:${p.codigo}`
      no(id, 1, {
        tipo: 'portao', icone: 'lock', titulo: p.titulo, subtitulo: p.escala ? 'Barra e passa à equipe' : 'Barra e o robô só não responde',
        escala: Boolean(p.escala), ordem: i + 1,
      })
      tronco(GRUPOS.portoes, id)
    })
  }

  no('assistente', 0, { tipo: 'assistente', icone: 'sparkles', titulo: 'Assistente IA', subtitulo: fluxo?.ativo ? 'Ligado na loja' : 'Desligado na loja' })
  trilho(GRUPOS.portoes, 'assistente')
  no('prompt', 1, { tipo: 'prompt', icone: 'settings', titulo: 'Instruções da loja', subtitulo: `${(fluxo?.prompt ?? '').length.toLocaleString('pt-BR')} caracteres` })
  tronco('assistente', 'prompt')
  no(GRUPOS.roteiro, 1, {
    tipo: 'grupo', icone: 'extrato', titulo: 'Roteiro', subtitulo: `${roteiro.filter((e) => e.ativo).length} de ${roteiro.length} etapas`, grupo: 'roteiro', aberto: aberto.has('roteiro'),
  })
  tronco('assistente', GRUPOS.roteiro)
  if (aberto.has('roteiro')) {
    roteiro.forEach((etapa, i) => {
      const id = `roteiro:${i}`
      no(id, 2, { tipo: 'roteiro', icone: 'extrato', titulo: `${i + 1}. ${etapa.nome || 'Sem nome'}`, subtitulo: etapa.instrucao, ativo: etapa.ativo, desligado: !etapa.ativo, indice: i })
      tronco(GRUPOS.roteiro, id)
    })
  }
  no(GRUPOS.ferramentas, 1, {
    tipo: 'grupo', icone: 'plug', titulo: 'Ferramentas', subtitulo: `${ligadas} de ${total} ligadas`, grupo: 'ferramentas', aberto: aberto.has('ferramentas'),
  })
  tronco('assistente', GRUPOS.ferramentas)
  if (aberto.has('ferramentas')) {
    ferramentas.forEach((f) => {
      const id = `ferramenta:${f.nome}`
      no(id, 2, { tipo: 'ferramenta', icone: f.travada ? 'lock' : 'plug', titulo: f.nome, ligada: Boolean(f.ligada), travada: Boolean(f.travada), desligado: !f.ligada })
      tronco(GRUPOS.ferramentas, id)
    })
  }

  no('saida', 0, { tipo: 'ponta', icone: 'circle-check', titulo: 'Resposta ao cliente', subtitulo: 'Sai pelo WhatsApp' })
  trilho('assistente', 'saida')
  // A Equipe fica no fim, alinhada ao recuo maior: o corredor da direita passa por fora de todos os nós.
  no('equipe', 2, { tipo: 'equipe', icone: 'usuarios', titulo: 'Equipe', subtitulo: 'Quando barra ou pede ajuda' })
  aresta(GRUPOS.portoes, 'right', 'equipe', 'right', { tom: 'equipe' })
  aresta('assistente', 'right', 'equipe', 'right', { tom: 'equipe' })

  return { nos, arestas, orientacao: ORIENTACOES.vertical, compacto: true }
}

/** Ids a enquadrar quando um grupo abre: o grupo, o Assistente que o contém e os nós novos. */
export function idsDoGrupo(desenho, chave) {
  const prefixo = { portoes: 'portao:', roteiro: 'roteiro:', ferramentas: 'ferramenta:' }[chave]
  if (!prefixo) return []
  const raiz = GRUPOS[chave]
  return [raiz, ...desenho.nos.filter((n) => n.id.startsWith(prefixo)).map((n) => n.id)]
}
