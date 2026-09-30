// Domínio da Frente 6 · Entregas de hoje (rodada 5, seção 6). Puro, sem
// React: viagem, parada e os marcos calculados de trás para frente. A
// viagem em si não ganhou uma coleção nova no estado (o Provider é de
// outra frente, e a F6 nunca o edita): ela mora denormalizada em
// `pedido.viagem = { id, modo, ordem, chamado }` dentro de cada conversa
// que é uma parada, e este arquivo é quem sabe juntar isso de volta.
import { indiceDoPasso } from './esteira'
import { inicioDaFaixa, janelaPorId } from './entrega'
import { partesDoEndereco } from './formato'

// Quem leva, a partir do que a viagem já capturou (US-040). Viagem "eu
// levo" (`modo: 'propria'`) não precisa de nome; viagem "entregador" só
// resolve depois que o chamado simulado (`chamado.entregador`,
// `aplicacao/casos/entregas.js: ATUALIZAR_CHAMADO`) achou alguém.
export function entregadorDaViagem(viagem) {
  if (!viagem) return null
  if (viagem.modo === 'propria') return { tipo: 'propria' }
  const achado = viagem.chamado?.entregador
  // Rodada 12 (issue #17): o chamado passa a trazer o registro inteiro (nome,
  // veículo, placa, empresa, `dominio/despacho.js`); chamado antigo trazia
  // só o nome em texto e continua valendo.
  if (achado && typeof achado === 'object') return { ...achado, tipo: 'plataforma' }
  if (achado) return { tipo: 'plataforma', nome: achado }
  return null
}

const TIPOS_DE_ENTREGADOR = new Set(['propria', 'motoboy', 'plataforma'])

// Ponto único de leitura de "quem entrega este pedido" (US-040): o que a
// dona já escolheu direto no pedido (`aplicacao/reducer.js: AVANCAR_ESTEIRA`,
// motoboy nomeado ou "eu mesma levo" sem viagem) tem prioridade; sem isso,
// cai para o que a viagem já capturou. `null` quer dizer "ainda não dá pra
// saber quem leva", e quem despacha decide então, nunca inventa.
//
// `pedido.entregador` só conta quando tem a forma nova (`{ tipo, ... }`): o
// texto fixo antigo ("motoboy da casa", semente de antes do US-040) é uma
// string solta, sem `tipo`, e cairia como "resolvido" pelo `??` de baixo só
// por não ser `null`. Tratar como não resolvido é o que faz o despacho
// perguntar em vez de repetir o texto fixo em silêncio.
export const entregadorResolvido = (pedido) => {
  const doPedido = pedido?.entregador
  if (doPedido != null && TIPOS_DE_ENTREGADOR.has(doPedido.tipo)) return doPedido
  return entregadorDaViagem(pedido?.viagem)
}

// Fim da faixa "11h30 às 12h30": mesmo parse de `inicioDaFaixa`, na segunda
// hora da string.
export function fimDaFaixa(faixa, agora) {
  const casado = /às\s*(\d{1,2})h(\d{2})?/.exec(faixa ?? '')
  if (!casado) return null
  const dia = new Date(agora)
  dia.setHours(Number(casado[1]), Number(casado[2] ?? 0), 0, 0)
  return dia.getTime()
}

// Marcos de uma viagem (seção 6, "Supervisão: os marcos calculados de trás
// para frente"), a partir do início da janela de cada parada. `paradas`
// chega na ordem da viagem; entrega sem viagem entra como array de 1 item
// (ela conta como viagem de uma parada só).
export function marcosDaViagem(paradas, agora, constantes) {
  const { minutosTrecho, minutosChegadaEntregador, minutosConferir } = constantes
  const porParada = paradas.map((parada, indice) => {
    const chegar = inicioDaFaixa(parada.faixa, agora)
    const entregarAte = fimDaFaixa(parada.faixa, agora)
    const acumulado = minutosTrecho * (indice + 1) * 60000
    return {
      id: parada.id, chegar, entregarAte,
      sairCandidato: chegar == null ? null : chegar - acumulado,
    }
  })
  const candidatos = porParada.map((p) => p.sairCandidato).filter((v) => v != null)
  const sair = candidatos.length ? Math.min(...candidatos) : null
  const chamar = sair == null ? null : sair - minutosChegadaEntregador * 60000
  const conferir = sair == null ? null : sair - minutosConferir * 60000
  // Chegada real na parada: partindo de `sair` (o horário único da viagem
  // inteira), não do início da própria janela dela. É essa conta que pode
  // estourar o fim da janela numa viagem com muita parada (seção 6, "chega
  // 13h40, depois da janela").
  const comChegadaReal = porParada.map((parada, indice) => {
    const chegadaReal = sair == null ? null : sair + minutosTrecho * (indice + 1) * 60000
    const atrasada = chegadaReal != null && parada.entregarAte != null && chegadaReal > parada.entregarAte
    return { ...parada, chegadaReal, atrasada }
  })
  return { sair, chamar, conferir, porParada: comChegadaReal }
}

// Junta as conversas por `pedido.viagem.id`, ordenadas pela `ordem` de cada
// parada dentro da viagem. Uma conversa sem `pedido.viagem` não entra em
// grupo nenhum (fica "solta", ver `entregasSoltas`).
export function viagensDeHoje(conversas) {
  const grupos = new Map()
  for (const conversa of conversas) {
    const viagem = conversa.pedido?.viagem
    if (!viagem) continue
    if (!grupos.has(viagem.id)) {
      grupos.set(viagem.id, { id: viagem.id, modo: viagem.modo, chamado: viagem.chamado, paradas: [] })
    }
    grupos.get(viagem.id).paradas.push(conversa)
  }
  return [...grupos.values()].map((grupo) => ({
    ...grupo,
    paradas: grupo.paradas.slice().sort((a, b) => a.pedido.viagem.ordem - b.pedido.viagem.ordem),
  }))
}

// "Gerar rota" (seção 6): ordena por início de janela e, dentro da mesma
// janela, junta o mesmo bairro. Integração da rodada 5: a ordem sugerida
// passa a ser APLICADA na viagem (com desfazer na tela), não só mostrada.
export function ordemSugeridaDaViagem(paradas, janelas, agora) {
  return paradas.slice().sort((a, b) => {
    const faixaA = janelas.find((j) => j.id === a.pedido.janela)?.faixa
    const faixaB = janelas.find((j) => j.id === b.pedido.janela)?.faixa
    const inicioA = inicioDaFaixa(faixaA, agora) ?? 0
    const inicioB = inicioDaFaixa(faixaB, agora) ?? 0
    if (inicioA !== inicioB) return inicioA - inicioB
    const bairroA = partesDoEndereco(a.cliente?.endereco).bairro
    const bairroB = partesDoEndereco(b.cliente?.endereco).bairro
    return bairroA.localeCompare(bairroB, 'pt-BR')
  })
}

export const viagemPorId = (conversas, id) => viagensDeHoje(conversas).find((v) => v.id === id) ?? null

// Toda conversa com pedido é "entrega de hoje" (a janela default de um
// pedido novo já é de hoje, `dominio/pedido.js:novoPedido`). Cancelada e
// entregue continuam na lista: os chips do topo (seção 6) que escondem ou
// mostram, a função aqui devolve todo mundo.
export const entregasDeHoje = (conversas) => conversas.filter((c) => c.pedido)

export const entregasSoltas = (conversas) => entregasDeHoje(conversas).filter((c) => !c.pedido.viagem)

// Rótulo, ícone e tom por estado da esteira (seção 6, tabela "Estados").
const ESTADOS_ENTREGA = {
  aguardando: { chave: 'preparar', rotulo: 'A preparar', icone: 'cooking-pot', tom: 'neutro' },
  pago: { chave: 'preparar', rotulo: 'A preparar', icone: 'cooking-pot', tom: 'neutro' },
  preparo: { chave: 'preparar', rotulo: 'A preparar', icone: 'cooking-pot', tom: 'neutro' },
  embalado: { chave: 'pronta', rotulo: 'Pronta', icone: 'package', tom: 'ok' },
  entrega: { chave: 'com-entregador', rotulo: 'Com o entregador', icone: 'moto', tom: 'atencao' },
  entregue: { chave: 'entregue', rotulo: 'Entregue', icone: 'house', tom: 'ok' },
  cancelado: { chave: 'cancelada', rotulo: 'Cancelada', icone: 'circle-x', tom: 'neutro' },
}
export const estadoDaEntrega = (pedido) => ESTADOS_ENTREGA[pedido?.estado] ?? ESTADOS_ENTREGA.aguardando

const faixaDoPedido = (conversa, janelas) => janelaPorId(janelas, conversa.pedido.janela)?.faixa ?? null

// Próximo passo desta entrega, com o marco (hora) e o botão que a leva ao
// passo seguinte da esteira (seção 6, "Cartão de entrega", linha 3: "só o
// próximo marco aparece"). Entrega já agrupada numa viagem que ainda não
// despachou não tem botão próprio: quem despacha é a viagem (Chamar
// entregador / Sair agora); uma vez em rota, cada parada ganha de volta o
// botão "Entregue" para marcar antes da hora (seção 6, "Em rota").
export function proximoPassoDeEntrega(conversa, grupoDaViagem, janelas, agora, constantes) {
  const pedido = conversa.pedido
  if (!pedido || pedido.estado === 'entregue' || pedido.estado === 'cancelado') return null

  const paradas = grupoDaViagem
    ? grupoDaViagem.paradas.map((c) => ({ id: c.id, faixa: faixaDoPedido(c, janelas) }))
    : [{ id: conversa.id, faixa: faixaDoPedido(conversa, janelas) }]
  const marcos = marcosDaViagem(paradas, agora, constantes)
  const minhaParada = marcos.porParada.find((p) => p.id === conversa.id)

  const emRotaOuAlem = indiceDoPasso(pedido.estado) >= indiceDoPasso('entrega')

  if (pedido.viagem && !emRotaOuAlem) {
    return { chave: 'aguardando-viagem', quandoMs: marcos.sair, semAcao: true }
  }
  if (pedido.estado === 'embalado') {
    return { chave: 'sair', quandoMs: marcos.sair, rotuloBotao: 'Saiu', proximoEstado: 'entrega' }
  }
  if (pedido.estado === 'entrega') {
    return { chave: 'entregar', quandoMs: minhaParada?.entregarAte, rotuloBotao: 'Entregue', proximoEstado: 'entregue' }
  }
  // aguardando, pago ou preparo: ainda falta conferir.
  return { chave: 'conferir', quandoMs: marcos.conferir, rotuloBotao: 'Conferi', proximoEstado: 'embalado' }
}

// Ordenação por clique (seção 6): nunca escrita, só clique. `viagensPorId`
// é um `Map` de `viagensDeHoje` (o chamador monta uma vez só por render).
export function ordenarEntregas(entregas, modo, { janelas, agora, constantes, viagensPorId }) {
  const comChave = entregas.map((conversa) => {
    const grupo = conversa.pedido.viagem ? viagensPorId.get(conversa.pedido.viagem.id) : null
    const passo = proximoPassoDeEntrega(conversa, grupo, janelas, agora, constantes)
    const minutos = passo ? Math.round((passo.quandoMs - agora) / 60000) : Number.POSITIVE_INFINITY
    const bairro = partesDoEndereco(conversa.cliente?.endereco).bairro || 'Sem bairro'
    return { conversa, minutos, bairro }
  })
  if (modo === 'longe') return comChave.sort((a, b) => b.minutos - a.minutos).map((x) => x.conversa)
  if (modo === 'bairro') {
    return comChave.sort((a, b) => a.bairro.localeCompare(b.bairro, 'pt-BR')).map((x) => x.conversa)
  }
  return comChave.sort((a, b) => a.minutos - b.minutos).map((x) => x.conversa)
}

// Link do Google Maps (seção 6, "Gerar rota"; estudo 18, F25): origem fixa
// na casa, até 9 paradas por link. Mais que isso vira um segundo link, cada
// um partindo da casa de novo (a direção não detalha o encadeamento de um
// terceiro link em diante, e nesta rodada não passamos disso).
const PARADAS_POR_LINK = 9

export function urlsDeRota(enderecoDaCasa, enderecosDasParadas) {
  const blocos = []
  for (let i = 0; i < enderecosDasParadas.length; i += PARADAS_POR_LINK) {
    blocos.push(enderecosDasParadas.slice(i, i + PARADAS_POR_LINK))
  }
  return blocos.map((bloco) => {
    const destino = bloco.at(-1)
    const waypoints = bloco.slice(0, -1)
    const params = new URLSearchParams({
      api: '1', origin: enderecoDaCasa, destination: destino,
    })
    if (waypoints.length) params.set('waypoints', waypoints.join('|'))
    return `https://www.google.com/maps/dir/?${params.toString()}`
  })
}
