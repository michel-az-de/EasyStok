// Entregas no modo API (F04). Regras puras sobre o que a API devolve: pedidos
// do KDS (S19, com endereço e aprovação da S12/S14) e viagens da S44. Nada de
// rede aqui; quem busca é `infra/api/entregasApi.js`.
import { faixaDeHorarios, minutosDoDia } from './entrega'
import { dataIsoNoFuso, moeda } from './formato'
import { normalizarBusca } from './respostas'

// Status que a gaveta pede ao KDS: aprovação manual, pronto e em rota.
export const STATUS_KDS_ENTREGAS = 'aguardando_aprovacao_baba,pronto,saiu_para_entrega'

// #1440: o dia inteiro do roteiro, do pedido que acabou de chegar ao que já foi entregue.
// Rascunho e cancelado não são entrega.
export const STATUS_KDS_DIA = [
  'aguardando_pagamento', 'aguardando_aprovacao_baba', 'aprovado_baba', 'aguardando', 'preparando', 'pronto',
  'saiu_para_entrega', 'entregue',
].join(',')

const ROTULO_SITUACAO = { Montando: 'Montando', EmRota: 'Em rota', Concluida: 'Concluída', Desfeita: 'Desfeita' }
export const rotuloSituacaoViagem = (situacao) => ROTULO_SITUACAO[situacao] ?? situacao

export const TIPOS_ENTREGADOR = [
  { valor: 'Motoboy', rotulo: 'Motoboy' },
  { valor: 'Plataforma', rotulo: 'Plataforma' },
  { valor: 'Proprio', rotulo: 'Próprio' },
]

export const EMPRESAS_ENTREGADOR = [
  { valor: 'Propria', rotulo: 'Própria' },
  { valor: 'NoveNove', rotulo: '99' },
  { valor: 'Lalamove', rotulo: 'Lalamove' },
  { valor: 'Uber', rotulo: 'Uber' },
  { valor: 'Ifood', rotulo: 'iFood Entregas' },
  { valor: 'Outra', rotulo: 'Outra' },
]

export const rotuloEmpresaEntregador = (valor) =>
  (valor ? EMPRESAS_ENTREGADOR.find((e) => e.valor === valor)?.rotulo ?? valor : null)

export const DIAS_DA_SEMANA = ['Domingo', 'Segunda', 'Terça', 'Quarta', 'Quinta', 'Sexta', 'Sábado']

const MOTIVOS_APROVACAO = { fora_de_area: 'Entrega fora da área' }
// #1474 (R7): recusar cancela, estorna e avisa o cliente num toque só; a tela pergunta antes.
export const confirmacaoDaRecusa = (numero, nome) =>
  `Recusar o pedido ${numero} de ${nome}? O pedido é cancelado, o pagamento volta ao cliente e ele recebe aviso.`

export const motivoDaAprovacao = (motivo) => (motivo ? MOTIVOS_APROVACAO[motivo] ?? motivo : 'Aprovação manual')

// Recarrega no `ready` (a API não tem replay) e a cada evento de pedido do SSE (S18).
export const recarregaEntregasCom = (evento) => evento === 'ready' || evento.startsWith('pedido.')

// #1577: entregadores, janelas e bloqueios são cadastro e quase não mudam; relê-los a cada evento de
// pedido eram 3 GETs a mais por recarga. Voltam quando a leitura é inteira (montagem, `ready`, ação
// que mexe no cadastro), quando o dia do roteiro mudou e, por garantia, quando a última leitura passa
// de 5 min: o cadastro pode ter sido mexido em outro aparelho, e o `ready` só vem a cada login.
export const VALIDADE_DO_CADASTRO_MS = 5 * 60000
export const releCadastroDeEntregas = ({ soOperacao, dia, cadastro, agora }) =>
  !soOperacao || !cadastro || cadastro.dia !== dia || agora - cadastro.lidoEm >= VALIDADE_DO_CADASTRO_MS

// #1434: sem pedidos e com erro, a primeira carga falhou; não fica "Carregando" para sempre.
export const situacaoDaLista = (pedidos, erro) => {
  if (pedidos !== null) return 'pronta'
  return erro ? 'falhou' : 'carregando'
}

// Divide em painéis. Pedido pronto que já está numa viagem montando não aparece
// de novo solto; viagens concluídas e desfeitas saem da tela do dia.
export function paineisDeEntregas(pedidos, viagens) {
  const lista = pedidos ?? []
  const vs = viagens ?? []
  const montando = vs.filter((v) => v.situacao === 'Montando')
  const emViagem = new Set(montando.flatMap((v) => v.paradas.map((p) => p.pedidoId)))
  return {
    aprovacao: lista.filter((p) => p.status === 'aguardando_aprovacao_baba'),
    prontos: lista.filter((p) => p.status === 'pronto' && !emViagem.has(p.id)),
    montando,
    emRota: vs.filter((v) => v.situacao === 'EmRota'),
  }
}

// RN-32: sem entregador resolvido, nenhum aviso sai. A API recusa; a tela avisa antes.
export function motivoDeSaida(viagem) {
  if (!viagem.entregadorId) return 'Escolha o entregador antes de sair.'
  if (viagem.paradas.length === 0) return 'Ponha ao menos um pedido na viagem.'
  return null
}

const hora = (h) => (h.length === 5 ? `${h}:00` : h)
const numero = (v) => Number(String(v).replace(',', '.'))
const digitos = (v) => String(v ?? '').replace(/\D/g, '')

// S45: corpos no formato dos inputs da API (`JanelaEntregaInput`, `FreteZonaInput`, `BloqueioEntregaInput`).
export const corpoJanela = (f) => ({
  diaDaSemana: Number(f.diaDaSemana),
  horaInicio: hora(f.horaInicio),
  horaFim: hora(f.horaFim),
  capacidadeMaxima: Number(f.capacidadeMaxima),
  label: f.label.trim(),
})

export function corpoZona(f) {
  const porCep = f.cobertura === 'cep'
  return {
    label: f.label.trim(),
    valor: numero(f.valor),
    tempoEstimadoMinutos: Number(f.tempoEstimadoMinutos),
    ordem: Number(f.ordem),
    cepInicio: porCep ? digitos(f.cepInicio) : null,
    cepFim: porCep ? digitos(f.cepFim) : null,
    bairros: porCep ? null : f.bairros.split(',').map((b) => b.trim()).filter(Boolean),
  }
}

export const corpoBloqueio = (f) => ({
  data: f.data,
  motivo: f.motivo.trim(),
  janelaEspecificaId: f.janelaEspecificaId || null,
})

// #1440: o formulário de janela marca vários dias de uma vez; a API guarda uma janela por dia
// da semana, então sai um corpo por dia. Sem nome, a janela leva a própria faixa.
const diasOrdenados = (dias) => [...new Set((dias ?? []).map(Number))].sort((a, b) => a - b)

// #1776 (item 21): bloqueios de hoje a um ano, o mesmo limite que a API aceita na criação (o
// bloqueio além de 60 dias entrava e sumia da lista).
export const DIAS_BLOQUEIO = 365

// #1776 (item 20): mesmo teto da API (`JanelaEntrega.CapacidadeTeto`).
export const CAPACIDADE_TETO = 500

// #1776 (item 20): `janelas` e `propria` (id em edição) deixam a tela dizer antes da API que o
// horário cruza com outra janela do mesmo dia. Encostadas (12h a 14h e 14h a 16h) não cruzam.
export function erroDoFormularioJanela(f, janelas = [], propria = null) {
  const inicio = minutosDoDia(f.horaInicio)
  const fim = minutosDoDia(f.horaFim)
  if (inicio == null || fim == null) return 'Informe início e fim da janela.'
  if (fim <= inicio) return 'O fim da janela tem que vir depois do início.'
  const capacidade = Number(f.capacidadeMaxima)
  if (!Number.isInteger(capacidade) || capacidade < 1) return 'Capacidade tem que ser um número inteiro de 1 ou mais.'
  if (capacidade > CAPACIDADE_TETO) return `Uma janela aceita até ${CAPACIDADE_TETO} pedidos.`
  const dias = diasOrdenados(f.dias)
  if (dias.length === 0) return 'Escolha pelo menos um dia da semana.'
  // Como na API, nome e vagas de uma janela legada não exigem corrigir a sobreposição anterior.
  const original = (janelas ?? []).find((j) => j.id === propria)
  if (original && dias.length === 1 && dias[0] === original.diaDaSemana
    && inicio === minutosDoDia(String(original.horaInicio).slice(0, 5))
    && fim === minutosDoDia(String(original.horaFim).slice(0, 5))) return null
  const cruza = (janelas ?? []).find((j) => j.id !== propria && dias.includes(j.diaDaSemana)
    && minutosDoDia(String(j.horaInicio).slice(0, 5)) < fim && inicio < minutosDoDia(String(j.horaFim).slice(0, 5)))
  if (cruza) {
    const faixa = faixaDeHorarios(String(cruza.horaInicio).slice(0, 5), String(cruza.horaFim).slice(0, 5))
    return `Já existe a janela de ${DIAS_DA_SEMANA[cruza.diaDaSemana]} ${faixa}, que cruza com este horário.`
  }
  return null
}

export const corposJanela = (f) => {
  const label = f.label?.trim() || faixaDeHorarios(f.horaInicio, f.horaFim)
  return diasOrdenados(f.dias).map((diaDaSemana) => corpoJanela({ ...f, diaDaSemana, label }))
}

// Janela da API -> campos do formulário de edição (um dia só: cada janela é de um dia).
// #1776 (item 20): nome igual à própria faixa é o nome automático; vem vazio para acompanhar o
// horário novo (antes a janela editada de 12h para 13h seguia chamada "12h00 às 14h00").
export const camposDaJanela = (j) => {
  const horaInicio = String(j.horaInicio).slice(0, 5)
  const horaFim = String(j.horaFim).slice(0, 5)
  const label = j.label ?? ''
  return {
    dias: [j.diaDaSemana], horaInicio, horaFim, capacidadeMaxima: String(j.capacidadeMaxima),
    label: label === faixaDeHorarios(horaInicio, horaFim) ? '' : label,
  }
}

// #1776 (item 22): zona da API -> campos do formulário (o mesmo da criação).
export const camposDaZona = (z) => ({
  label: z.label ?? '',
  valor: String(z.valor ?? '').replace('.', ','),
  tempoEstimadoMinutos: String(z.tempoEstimadoMinutos ?? ''),
  ordem: String(z.ordem ?? 0),
  cobertura: z.cepInicio ? 'cep' : 'bairros',
  cepInicio: z.cepInicio ?? '',
  cepFim: z.cepFim ?? '',
  bairros: (z.bairros ?? []).join(', '),
})

// #1776 (item 22): faixa de CEP ou bairro que outra zona ativa já cobre. Não bloqueia (a ordem
// decide qual vale), só avisa: "Centro" em duas zonas costuma ser engano.
export function avisoDeSobreposicao(f, zonas, propria = null) {
  const outras = (zonas ?? []).filter((z) => z.id !== propria && z.ativa)
  if (f.cobertura === 'cep') {
    const ini = Number(digitos(f.cepInicio))
    const fim = Number(digitos(f.cepFim))
    if (!ini || !fim) return null
    const z = outras.find((o) => o.cepInicio && Number(o.cepInicio) <= fim && ini <= Number(o.cepFim))
    return z ? `Esta faixa cruza com a zona ${z.label}. Vale a de menor ordem.` : null
  }
  const meus = new Set(String(f.bairros ?? '').split(',').map(normalizarBusca).filter(Boolean))
  for (const o of outras) {
    const repetido = (o.bairros ?? []).find((b) => meus.has(normalizarBusca(b)))
    if (repetido) return `${repetido} já está na zona ${o.label}. Vale a de menor ordem.`
  }
  return null
}

// I2.2 (#1651): frete por distância. A tela fala km e reais; a API (`FreteRaioInput`), metros e
// centavos. O endereço da cozinha não volta da API (só a coordenada fica gravada): em branco no
// formulário quer dizer "manter a cozinha atual".
const FAIXA_VAZIA = { ateKm: '', valor: '' }
const decimalBr = (n) => String(n).replace('.', ',')
const emMetros = (km) => Math.round(numero(km) * 1000)
const emCentavos = (valor) => Math.round(numero(valor) * 100)
const preenchido = (v) => String(v ?? '').trim() !== ''
const faixasPreenchidas = (f) => (f.faixas ?? []).filter((x) => preenchido(x.ateKm) || preenchido(x.valor))

export function formularioDoFreteRaio(config) {
  if (!config?.configurado) return { cep: '', numero: '', raioKm: '', gratisKm: '0', fatorRota: '1,4', faixas: [{ ...FAIXA_VAZIA }] }
  const faixas = (config.faixas ?? []).map((x) => ({
    ateKm: decimalBr(x.ateMetros / 1000), valor: (x.valorCentavos / 100).toFixed(2).replace('.', ','),
  }))
  return {
    cep: '', numero: '',
    raioKm: decimalBr(config.raioMaxMetros / 1000),
    gratisKm: decimalBr(config.faixaGratisMetros / 1000),
    fatorRota: decimalBr(config.fatorRota),
    faixas: faixas.length ? faixas : [{ ...FAIXA_VAZIA }],
  }
}

// As mesmas regras do `ConfigurarFreteRaioUseCase`, ditas antes de chamar a API.
export function erroDoFormularioFreteRaio(f, cozinhaGravada) {
  const cep = digitos(f.cep)
  if (!cep && !preenchido(f.numero)) {
    if (!cozinhaGravada) return 'Informe o CEP e o número da cozinha.'
  } else {
    if (cep.length !== 8) return 'CEP da cozinha inválido. Informe os 8 dígitos.'
    if (!preenchido(f.numero)) return 'Informe o número do endereço da cozinha.'
  }
  const raio = emMetros(f.raioKm)
  if (!(raio > 0)) return 'O raio máximo deve ser maior que zero.'
  const gratis = emMetros(f.gratisKm)
  if (!(gratis >= 0 && gratis <= raio)) return 'A distância com frete grátis deve ficar entre zero e o raio máximo.'
  const fator = numero(f.fatorRota)
  if (!(fator >= 1 && fator <= 3)) return 'O fator de rota deve ficar entre 1 e 3.'

  let alcance = gratis
  for (const faixa of faixasPreenchidas(f)) {
    const ate = emMetros(faixa.ateKm)
    if (!(ate > 0)) return 'Informe até quantos km vai cada faixa.'
    if (!preenchido(faixa.valor) || !(emCentavos(faixa.valor) >= 0)) return 'Informe o valor de cada faixa.'
    if (ate <= gratis) return 'Cada faixa precisa começar depois da distância com frete grátis.'
    if (ate <= alcance) return 'As faixas devem estar em ordem crescente de distância, sem repetir.'
    alcance = ate
  }
  return alcance < raio ? 'As faixas precisam alcançar o raio máximo de entrega.' : null
}

export const corpoFreteRaio = (f) => ({
  cep: digitos(f.cep) || null,
  numero: String(f.numero ?? '').trim() || null,
  raioMaxMetros: emMetros(f.raioKm),
  faixaGratisMetros: emMetros(f.gratisKm),
  fatorRota: numero(f.fatorRota),
  faixas: faixasPreenchidas(f).map((x) => ({ ateMetros: emMetros(x.ateKm), valorCentavos: emCentavos(x.valor) })),
})

export const corpoTesteDeFrete = (f) => ({ cep: digitos(f.cep), numero: String(f.numero ?? '').trim() })

const emKm = (metros) =>
  `${(metros / 1000).toLocaleString('pt-BR', { minimumFractionDigits: 1, maximumFractionDigits: 1 })} km`

// Resultado do "Testar um endereço" (`FreteRaioTesteResult`) em texto de balcão.
export function resumoDoTesteDeFrete(r) {
  if (r.modo !== 'raio') return { tom: 'atencao', titulo: 'Vale a tabela de zonas', detalhe: r.motivo ?? '' }
  const rota = `${emKm(r.distanciaRotaMetros)} de rota${r.distanciaEstimada ? ' (estimada, sem rota medida)' : ''}`
  if (r.foraDeCobertura) return { tom: 'atencao', titulo: 'Fora do raio de entrega', detalhe: rota }
  return { tom: 'ok', titulo: r.gratis ? 'Frete grátis' : `Frete de ${moeda(r.valorCentavos / 100)}`, detalhe: rota }
}
// I9.2 (#1691): corrida pedida por fora (app da 99, da Lalamove, da Uber), anotada na viagem com o
// custo (`CorridaExternaBody` da API). Entregador próprio não é corrida externa.
export const EMPRESAS_DE_CORRIDA = EMPRESAS_ENTREGADOR.filter((e) => e.valor !== 'Propria')
export const CORRIDA_CUSTO_MAXIMO_CENTAVOS = 500000

export const empresaPadraoDaCorrida = (viagem, entregadores) => {
  const empresa = (entregadores ?? []).find((e) => e.id === viagem?.entregadorId)?.empresa
  return EMPRESAS_DE_CORRIDA.some((e) => e.valor === empresa) ? empresa : 'NoveNove'
}

// "18,90", "18.9" e "R$ 18,90" valem o mesmo; o que não é número vira NaN e o formulário avisa.
const custoEmCentavos = (texto) => {
  const limpo = String(texto ?? '').replace(/R\$/i, '').trim()
  return limpo === '' ? NaN : Math.round(numero(limpo) * 100)
}

export function erroDoFormularioCorrida(f) {
  const custo = custoEmCentavos(f.custo)
  if (Number.isNaN(custo)) return 'Informe o custo da corrida.'
  if (custo < 0) return 'O custo da corrida não pode ser negativo.'
  if (custo > CORRIDA_CUSTO_MAXIMO_CENTAVOS) return 'Custo acima de R$ 5.000,00. Confira o valor.'
  return null
}

export const corpoCorrida = (f) => ({
  provedor: f.empresa,
  custoCentavos: custoEmCentavos(f.custo),
  motorista: String(f.motorista ?? '').trim() || null,
})

export const formularioDaCorrida = (corrida) => ({
  empresa: corrida.provedor,
  custo: (corrida.custoCentavos / 100).toFixed(2).replace('.', ','),
  motorista: corrida.motorista ?? '',
})

// Viagem concluída sai do painel, mas a 99 muitas vezes só fecha o preço no fim: as concluídas do dia
// escolhido continuam à mão para anotar ou corrigir o custo.
export const viagensConcluidasNoDia = (viagens, dia) => (viagens ?? [])
  .filter((v) => v.situacao === 'Concluida' && v.concluidaEm && dataIsoNoFuso(Date.parse(v.concluidaEm)) === dia)

export const custoDasCorridas = (corridas) => (corridas ?? []).reduce((soma, c) => soma + (c.custoCentavos ?? 0), 0)

export const resumoDaCorrida = (corrida) => [
  rotuloEmpresaEntregador(corrida.provedor), moeda(corrida.custoCentavos / 100), corrida.motorista, corrida.corrigidoEm ? 'corrigida' : null,
].filter(Boolean).join(' · ')