// Extensão .js: este arquivo é alcançado por agente.js, que o Node carrega
// direto (ferramentas/avaliar-agente.mjs), e o ESM do Node não resolve
// caminho sem extensão.
import { horaMono } from './formato.js'
import { itemPorSku } from './cardapio.js'

export const janelaPorId = (janelas, id) => janelas.find((j) => j.id === id) ?? null

export const faixaDaJanela = (janelas, id) => janelaPorId(janelas, id)?.faixa ?? null

export const janelaLotada = (janela, atual) =>
  janela.ocupadas >= janela.capacidade && janela.id !== atual

export function opcoesDeJanela(janelas, atual) {
  return janelas.map((j) => {
    const lotada = janelaLotada(j, atual)
    return {
      valor: j.id,
      desabilitada: lotada,
      rotulo: `${j.faixa} · ${lotada ? 'lotada' : `${j.capacidade - j.ocupadas} vaga(s)`}`,
    }
  })
}

// ---------------------------------------------------------------------------
// Rodada 2 · janelas com capacidade. Acrescentado no fim do arquivo.
//
// A ocupação passou a ser CONTADA dos pedidos reais, não lida de um campo. O
// `ocupadas` do catálogo era número fixo que não conversava com a massa de
// conversas, e número que mente é pior que número nenhum: escolher janela ocupa
// vaga e trocar devolve sem ninguém somar nada à mão.
//
// Erro que este arquivo evita de propósito (Baymard, teste da ALDI): esconder a
// lotação até o fim, e sumir com a janela cheia da lista. Cheia fica visível,
// desabilitada, com a contagem e a próxima livre ao lado.
// ---------------------------------------------------------------------------

// Corte: uma hora antes da faixa começar. É o tempo mínimo entre anotar e ter a
// massa embalada, e corte é coisa diferente de entrega (Loggi). O horário sai da
// própria faixa para não existir dois lugares dizendo quando a janela começa.
export const MINUTOS_DE_CORTE = 60

export function inicioDaFaixa(faixa, agora) {
  const casado = /(\d{1,2})h(\d{2})?/.exec(faixa ?? '')
  if (!casado) return null
  const dia = new Date(agora)
  dia.setHours(Number(casado[1]), Number(casado[2] ?? 0), 0, 0)
  return dia.getTime()
}

function fimDaFaixaBruta(faixa, agora) {
  const casado = /às\s*(\d{1,2})h(\d{2})?/.exec(faixa ?? '')
  if (!casado) return null
  const dia = new Date(agora)
  dia.setHours(Number(casado[1]), Number(casado[2] ?? 0), 0, 0)
  return dia.getTime()
}

// RN-06 e RN-22 (US-028, áudio 06: "tem que considerar pelo menos uns 40
// minutos"). O corte (MINUTOS_DE_CORTE) tranca a ESCOLHA da janela antes do
// pedido entrar; o respiro tranca o TEXTO depois, porque o tempo passa entre
// a confirmação e cada aviso seguinte, e repetir a janela crua num aviso
// tardio pode prometer menos que o preparo. Ponto único: todo texto que
// informa prazo ao cliente passa por aqui em vez de usar `janela.faixa` direto.
export const RESPIRO_MINUTOS = 40

// Terceiro parâmetro OPCIONAL (rodada 13, issue #42): quem já chama esta
// função sem o terceiro argumento continua com o chão do RN-22 (40 min),
// ninguém quebra. Quem tem o catálogo em mãos (reducer, agente) pode passar
// `catalogo.respiroMinutos` para refletir o que a Thati ajustou na tela de
// Janelas, sempre `>= RESPIRO_MINIMO_RN22` (mais abaixo): regra escrita não
// desce, só pode subir.
export function faixaParaCliente(faixa, agora, respiroMinutos = RESPIRO_MINUTOS) {
  const inicio = inicioDaFaixa(faixa, agora)
  const fim = fimDaFaixaBruta(faixa, agora)
  if (inicio == null || fim == null) return faixa
  const minimo = agora + respiroMinutos * 60000
  if (inicio >= minimo) return faixa
  const largura = fim > inicio ? fim - inicio : respiroMinutos * 60000
  return `${horaMono(minimo)} às ${horaMono(minimo + largura)}`
}

// "entre 12h30 às 13h30" não é português: depois de "entre" a faixa vai com
// "e" ("entre 12h30 e 13h30"), do jeito que a massa e a Thatiane escrevem.
// Todo texto ao cliente que põe a faixa depois de "entre" passa por aqui.
export const faixaDepoisDeEntre = (faixa) => String(faixa).replace(/\s+às\s+/, ' e ')

export function preencherFaixa(texto, faixa) {
  return texto
    .replace(/(entre) \{faixa\}/gi, (_, entre) => `${entre} ${faixaDepoisDeEntre(faixa)}`)
    .replace(/\{faixa\}/g, () => faixa)
}

// Pedido entregue não ocupa mais nada: a vaga é de quem ainda vai receber.
const pedidosDaJanela = (conversas, id) => conversas.filter(
  (c) => c.pedido && c.pedido.janela === id && c.pedido.estado !== 'entregue',
)

// Início da janela do pedido ainda ativo (seção 1 do Balcão, rodada 5): o
// cartão de triagem mostra "12h30" com o ícone da moto só quando falta
// entregar. Pedido sem janela escolhida ou já entregue não tem hora para
// mostrar.
export function inicioDoPedidoAtivo(conversa, janelas, agora) {
  const pedido = conversa?.pedido
  if (!pedido || pedido.estado === 'entregue' || !pedido.janela) return null
  const faixa = faixaDaJanela(janelas, pedido.janela)
  return faixa ? inicioDaFaixa(faixa, agora) : null
}

const porcoesDe = (pedido) => pedido.itens.reduce((soma, linha) => soma + linha.qtd, 0)

// Q2 em aberto (issue #42): "como calcular capacidade quando o pedido
// mistura as duas linhas". PROPOSTA implementada como opção ajustável,
// desligada por padrão (`janela.capacidadePorLinha` nulo mantém a conta de
// sempre, um pedido = uma vaga). Ligada (duas capacidades na mesma janela,
// uma por linha de RN-17), o pedido que mistura servir e preparar em casa
// ocupa vaga NAS DUAS linhas que ele toca: é o lado mais restritivo, porque
// "impedir pedido encavalado" (a pergunta da Tatiana) significa nunca deixar
// uma linha passar do que ela mede dar conta, mesmo que a outra linha do
// MESMO pedido caiba à vontade. `cardapio` é opcional: sem ele (chamador
// antigo, ou tela que ainda não tem o catálogo em mãos) a conta cai na de
// sempre, por capacidade total.
function linhasDoPedido(pedido, cardapio) {
  const linhas = new Set()
  for (const item of pedido?.itens ?? []) {
    const produto = itemPorSku(cardapio, item.sku)
    if (produto?.linha) linhas.add(produto.linha)
  }
  return linhas
}

function ocupacaoPorLinha(janela, pedidos, cardapio) {
  if (!janela.capacidadePorLinha || !cardapio) return null
  const contagem = { servir: 0, casa: 0 }
  for (const { pedido } of pedidos) {
    const linhas = linhasDoPedido(pedido, cardapio)
    if (linhas.has('servir')) contagem.servir += 1
    if (linhas.has('casa')) contagem.casa += 1
  }
  return ['servir', 'casa'].reduce((acc, chave) => {
    const capacidade = janela.capacidadePorLinha[chave] ?? 0
    const ocupadas = contagem[chave]
    acc[chave] = { capacidade, ocupadas, vagas: Math.max(capacidade - ocupadas, 0) }
    return acc
  }, {})
}

// Uma janela com tudo que a tela precisa dizer sobre ela. Cinco estados, em
// texto: aberta, poucas vagas, cheia, em corte e confirmada.
export function ocupacaoDaJanela(janela, conversas, agora, atual = null, cardapio = null) {
  const pedidos = pedidosDaJanela(conversas, janela.id)
  const ocupadas = pedidos.length
  // Corte por janela (rodada 13, issue #42): cada faixa pode precisar de um
  // tempo de preparo diferente. Sem o campo (semente antiga, ou janela criada
  // sem mexer no valor sugerido), cai no chão de sempre, `MINUTOS_DE_CORTE`.
  const corteMinutos = janela.corteMinutos ?? MINUTOS_DE_CORTE
  const vagasTotais = Math.max(janela.capacidade - ocupadas, 0)
  const porLinha = ocupacaoPorLinha(janela, pedidos, cardapio)
  // Vaga é o MENOR dos limites que valem para esta janela: total e, se a
  // proposta Q2 estiver ligada, cada linha. Nunca oferece mais do que o lado
  // mais apertado permite.
  const vagas = porLinha ? Math.min(vagasTotais, porLinha.servir.vagas, porLinha.casa.vagas) : vagasTotais
  const comeca = inicioDaFaixa(janela.faixa, agora)
  const emCorte = comeca != null && agora > comeca - corteMinutos * 60000
  const confirmada = janela.id === atual
  const estourou = ocupadas > janela.capacidade
    || (porLinha != null && (porLinha.servir.ocupadas > porLinha.servir.capacidade
      || porLinha.casa.ocupadas > porLinha.casa.capacidade))

  let chave = 'aberta'
  if (confirmada) chave = 'confirmada'
  else if (estourou) chave = 'estourada'
  else if (vagas === 0) chave = 'cheia'
  else if (emCorte) chave = 'em-corte'
  else if (vagas <= 1) chave = 'poucas-vagas'

  return {
    id: janela.id,
    faixa: janela.faixa,
    capacidade: janela.capacidade,
    corteMinutos,
    ocupadas,
    vagas,
    porLinha,
    estourou,
    emCorte,
    confirmada,
    chave,
    pedidos,
    porcoes: pedidos.reduce((soma, c) => soma + porcoesDe(c.pedido), 0),
    // Percentual serve à barra. Passa de 100 quando ela força encaixe, e a barra
    // precisa saber disso para mostrar que estourou em vez de fingir que encheu.
    percentual: Math.round((ocupadas / janela.capacidade) * 100),
  }
}

// Rodada 13 (issue #42): só entra na oferta do dia quem está ATIVA e cai no
// dia da semana marcado. A janela que já é o `atual` de um pedido continua
// aparecendo mesmo pausada ou fora do dia de hoje, porque pausar não some
// com o pedido que já marcou ali (Aceite: "janela com pedidos não se apaga").
export const ocupacaoDeHoje = (janelas, conversas, agora, atual = null, cardapio = null) =>
  janelas
    .filter((janela) => janelaVisivelHoje(janela, agora) || janela.id === atual)
    .map((janela) => ocupacaoDaJanela(janela, conversas, agora, atual, cardapio))

// Rótulo do estado, em texto. Cor nunca informa sozinha.
// Uma contagem só em toda a tela: ocupadas de capacidade. Misturar "3 vagas" com
// "2 de 5" no mesmo painel obriga a pessoa a converter de cabeça, e ela está com
// a mão na massa.
const ROTULOS = {
  confirmada: (o) => `escolhida, ${o.ocupadas} de ${o.capacidade}`,
  estourada: (o) => `capacidade estourada, ${o.ocupadas} de ${o.capacidade}`,
  cheia: (o) => `cheia, ${o.ocupadas} de ${o.capacidade}`,
  'em-corte': (o) => `passou do corte, ${o.ocupadas} de ${o.capacidade}`,
  'poucas-vagas': (o) => `${o.ocupadas} de ${o.capacidade}, última vaga`,
  aberta: (o) => `${o.ocupadas} de ${o.capacidade}, ${o.vagas} vagas`,
}

export const rotuloDaOcupacao = (ocupacao) => ROTULOS[ocupacao.chave](ocupacao)

// Motivo por escrito de por que não dá para escolher. Sem isto sobra um botão
// morto, e botão morto faz a pessoa clicar de novo achando que a tela travou.
export function motivoDoBloqueio(ocupacao) {
  if (ocupacao.confirmada) return null
  if (ocupacao.vagas === 0) {
    // Proposta Q2: quando é a linha (não o total da janela) que zerou a
    // vaga, o motivo aponta a linha certa, para a Thati não procurar vaga
    // que só existe na outra linha.
    if (ocupacao.porLinha) {
      const { servir, casa } = ocupacao.porLinha
      if (servir.vagas === 0 && casa.vagas === 0) {
        return `Linhas "para servir" e "preparar em casa" cheias: ${servir.ocupadas} de ${servir.capacidade} `
          + `e ${casa.ocupadas} de ${casa.capacidade}.`
      }
      if (servir.vagas === 0) return `Linha "para servir" cheia: ${servir.ocupadas} de ${servir.capacidade}.`
      if (casa.vagas === 0) return `Linha "preparar em casa" cheia: ${casa.ocupadas} de ${casa.capacidade}.`
    }
    return `Cheia: ${ocupacao.ocupadas} de ${ocupacao.capacidade} pedidos já marcados.`
  }
  if (ocupacao.emCorte) {
    const corte = ocupacao.corteMinutos ?? MINUTOS_DE_CORTE
    return `Passou do corte, ${corte} min antes da faixa. Não dá tempo de preparar e embalar.`
  }
  return null
}

export const janelaDisponivel = (ocupacao) => motivoDoBloqueio(ocupacao) == null

// Próxima livre, para andar ao lado da janela cheia. Quem ouve "não dá" precisa
// ouvir "dá às 18h30" na mesma linha.
export function proximaLivre(ocupacoes, id) {
  const indice = ocupacoes.findIndex((o) => o.id === id)
  return ocupacoes.slice(indice + 1).find(janelaDisponivel)
    ?? ocupacoes.find((o) => o.id !== id && janelaDisponivel(o))
    ?? null
}

// Resumo do topo: "Janela 12h30 · 2/4". Diz que é janela, não hora solta. Só
// janela que ainda vai sair, porque o topo é para o resto do dia, não para o
// histórico.
export const resumoDeHoje = (ocupacoes) => ocupacoes
  .filter((o) => !o.emCorte || o.ocupadas > 0)
  .map((o) => ({
    id: o.id,
    texto: `Janela ${o.faixa.split(' ')[0]} · ${o.ocupadas}/${o.capacidade}`,
    chave: o.chave,
    estourou: o.estourou,
  }))

export const totalDePorcoes = (ocupacoes) => ocupacoes.reduce((soma, o) => soma + o.porcoes, 0)

// ---------------------------------------------------------------------------
// Rodada 13 · configuração das janelas pela Thati (issue #42, D9, RN-21,
// RN-22, Q2). Até aqui a lista de janelas era só semente do catálogo
// (`infra/catalogo.js`, `JANELAS_ENTREGA`), sem tela para mexer: criar,
// editar, pausar/reativar, dias da semana, corte por janela e a proposta da
// Q2 (capacidade por linha) nascem aqui, puro, do mesmo jeito que o resto
// deste arquivo. A tela (`features/gestao/janelas/AbaJanelas.jsx`) só chama.
//
// Achado ao medir o código para a issue: o texto dela cita "dominio/janela.js"
// como onde a janela mora, mas aquele arquivo é a janela de RESPOSTA da
// mensageria (WhatsApp/Instagram, `canal.horasJanela`), duas regras de
// domínio homônimas e sem relação. Capacidade, corte e respiro de entrega
// (RN-21/RN-22) sempre estiveram aqui, em `dominio/entrega.js`. Registrado
// também no arquivo de decisão 104.
// ---------------------------------------------------------------------------

// pt-BR, na mesma ordem de `Date#getDay()`: 0 domingo ... 6 sábado. A tela de
// configuração usa esta lista para os toggles de dia, a agenda do dia usa
// `TODOS_OS_DIAS` como valor padrão de janela nova (entrega todo dia, o caso
// comum da casa hoje).
export const DIAS_DA_SEMANA = [
  { valor: 0, rotulo: 'dom' },
  { valor: 1, rotulo: 'seg' },
  { valor: 2, rotulo: 'ter' },
  { valor: 3, rotulo: 'qua' },
  { valor: 4, rotulo: 'qui' },
  { valor: 5, rotulo: 'sex' },
  { valor: 6, rotulo: 'sáb' },
]
export const TODOS_OS_DIAS = DIAS_DA_SEMANA.map((d) => d.valor)

// Chão do RN-22 ("respiro de pelo menos 40 minutos"): regra ESCRITA, não
// decisão da Thati. O campo de respiro da tela pode subir dele, nunca descer;
// `ajustarRespiroMinimo` é o único lugar que aplica esse piso.
export const RESPIRO_MINIMO_RN22 = 40

export const diaDaSemanaDe = (agora) => new Date(agora).getDay()

// "11:30" (formato do <input type="time">) -> 690 minutos desde 0h. Parser
// único: quem lê hora de formulário passa por aqui, ninguém escreve regex
// solta de novo.
export function minutosDoDia(hhmm) {
  const casado = /^(\d{1,2}):(\d{2})$/.exec(String(hhmm ?? '').trim())
  if (!casado) return null
  const horas = Number(casado[1])
  const minutos = Number(casado[2])
  if (horas > 23 || minutos > 59) return null
  return horas * 60 + minutos
}

// 690 -> "11h30", o MESMO formato que `inicioDaFaixa`/`fimDaFaixaBruta` já lêem
// no topo deste arquivo. Ponto único: quem monta uma faixa nova passa por
// aqui, em vez de escrever "Xh" à mão e desalinhar do parser.
function horaComH(minutosDesdeZero) {
  return `${Math.floor(minutosDesdeZero / 60)}h${String(minutosDesdeZero % 60).padStart(2, '0')}`
}

export function faixaDeHorarios(horaInicio, horaFim) {
  const inicio = minutosDoDia(horaInicio)
  const fim = minutosDoDia(horaFim)
  if (inicio == null || fim == null) return ''
  return `${horaComH(inicio)} às ${horaComH(fim)}`
}

// Validação do formulário de criar/editar (um lugar só: a tela nunca decide
// sozinha se pode salvar). Devolve o motivo em texto, ou `null` quando está
// tudo certo. Capacidade por linha é OPCIONAL (proposta Q2): só valida as duas
// quando `capacidadePorLinha` vier preenchido.
export function erroDaJanela(campos) {
  const inicio = minutosDoDia(campos.horaInicio)
  const fim = minutosDoDia(campos.horaFim)
  if (inicio == null || fim == null) return 'Informe início e fim da faixa, no formato hh:mm.'
  if (fim <= inicio) return 'O fim da faixa tem que vir depois do início.'
  if (!Number.isInteger(campos.capacidade) || campos.capacidade < 1) {
    return 'Capacidade tem que ser um número inteiro de 1 ou mais.'
  }
  if (!Number.isInteger(campos.corteMinutos) || campos.corteMinutos < 0) {
    return 'Corte tem que ser um número de minutos, 0 ou mais.'
  }
  if (!campos.diasSemana || campos.diasSemana.length === 0) {
    return 'Escolha pelo menos um dia da semana.'
  }
  if (campos.capacidadePorLinha) {
    const { servir, casa } = campos.capacidadePorLinha
    if (!Number.isInteger(servir) || servir < 0 || !Number.isInteger(casa) || casa < 0) {
      return 'Capacidade por linha tem que ser um número inteiro, 0 ou mais, nas duas linhas.'
    }
  }
  return null
}

// Fábrica pura: a tela nunca monta o objeto de janela à mão, para nenhum
// campo novo nascer desalinhado (ex.: `faixa` sem bater com `horaInicio`).
// Chamador confere `erroDaJanela(campos) == null` antes.
export function criarJanela(id, campos) {
  return {
    id,
    horaInicio: campos.horaInicio,
    horaFim: campos.horaFim,
    faixa: faixaDeHorarios(campos.horaInicio, campos.horaFim),
    capacidade: campos.capacidade,
    capacidadePorLinha: campos.capacidadePorLinha ?? null,
    corteMinutos: campos.corteMinutos,
    diasSemana: [...campos.diasSemana].sort((a, b) => a - b),
    ativa: true,
  }
}

export function editarJanela(janela, campos) {
  return {
    ...janela,
    horaInicio: campos.horaInicio,
    horaFim: campos.horaFim,
    faixa: faixaDeHorarios(campos.horaInicio, campos.horaFim),
    capacidade: campos.capacidade,
    capacidadePorLinha: campos.capacidadePorLinha ?? null,
    corteMinutos: campos.corteMinutos,
    diasSemana: [...campos.diasSemana].sort((a, b) => a - b),
  }
}

// Pausar não é apagar (Aceite da issue #42): pedido que já marcou a janela
// continua enxergando ela (ver `ocupacaoDeHoje`), só sai da oferta para quem
// ainda não marcou nada.
export const pausarJanela = (janela) => ({ ...janela, ativa: false })
export const reativarJanela = (janela) => ({ ...janela, ativa: true })

// "Janela com pedidos não pode ser apagada, só pausada" (Aceite): conta os
// pedidos de HOJE em diante do mesmo jeito que `pedidosDaJanela` já faz
// acima (pedido entregue não prende mais a janela), para a dona conseguir
// apagar a janela de teste que nunca recebeu ninguém.
export function janelaTemPedidos(janela, conversas) {
  return pedidosDaJanela(conversas ?? [], janela.id).length > 0
}

export const podeExcluirJanela = (janela, conversas) => !janelaTemPedidos(janela, conversas)

// Visível hoje = ativa E o dia da semana de `agora` está entre os marcados.
// Semente sem `diasSemana` (dado antigo) continua valendo todo dia, para não
// sumir janela de quem não editou nada ainda.
export function janelaVisivelHoje(janela, agora) {
  if (janela.ativa === false) return false
  if (!janela.diasSemana) return true
  return janela.diasSemana.includes(diaDaSemanaDe(agora))
}

// Piso do RN-22: quem tenta baixar o respiro da tela para menos de 40 min sai
// travado em 40, nunca em erro silencioso.
export const ajustarRespiroMinimo = (minutos) => Math.max(RESPIRO_MINIMO_RN22, Math.round(minutos) || RESPIRO_MINIMO_RN22)
