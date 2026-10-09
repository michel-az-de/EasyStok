// Rodada 12, issue #17 (feedback da Thatiane, áudio de 26/09/2026): quem leva
// o pedido e qual número ele confere. Puro, sem React.
//
// O entregador deixa de ser só um nome. Vira registro com nome, veículo, placa
// e empresa (própria, 99, Lalamove, iFood Entregas), preenchido à mão no
// despacho. Nenhuma dessas plataformas está ligada ao sistema: o formato é o
// mesmo que a integração devolveria (motorista, veículo, placa), e quando ela
// existir preenche estes campos sem mudar a tela. `tipo` continua o que a
// esteira já conhece (`dominio/esteira.js: nomeParaMensagem`): 'propria' é a
// dona levando, 'motoboy' é entregador da casa, 'plataforma' é de aplicativo.
import { numeroCurto } from './pedido'
import { entregadorResolvido } from './viagem'

export const EMPRESAS_DE_ENTREGA = [
  { valor: 'propria', rotulo: 'Própria' },
  { valor: '99', rotulo: '99' },
  { valor: 'lalamove', rotulo: 'Lalamove' },
  { valor: 'ifood', rotulo: 'iFood Entregas' },
  { valor: 'outra', rotulo: 'Outra' },
]

export const VEICULOS = [
  { valor: 'moto', rotulo: 'Moto', temPlaca: true },
  { valor: 'carro', rotulo: 'Carro', temPlaca: true },
  { valor: 'bicicleta', rotulo: 'Bicicleta', temPlaca: false },
]

export const AVISO_SEM_INTEGRACAO =
  '99, Lalamove e iFood Entregas ainda não estão ligados ao sistema: preencha o que o app do entregador mostrar.'

// Pedido da Thatiane: o filtro "por janela" não dizia o que é janela.
export const EXPLICACAO_JANELA =
  'Janela de entrega é a faixa de horário que o cliente escolheu para receber, por exemplo 12h30 às 13h30. '
  + 'Aqui as entregas ficam juntas por faixa, com quantas vagas cada uma ainda tem.'

const rotuloDe = (lista, valor) => lista.find((x) => x.valor === valor)?.rotulo ?? null

// Placa antiga (ABC1234) e Mercosul (ABC1D23), sem hífen nem espaço.
export const normalizarPlaca = (texto) => String(texto ?? '').toUpperCase().replace(/[^A-Z0-9]/g, '').slice(0, 7)
const PLACA = /^[A-Z]{3}\d[A-Z0-9]\d{2}$/

const veiculoTemPlaca = (veiculo) => VEICULOS.find((v) => v.valor === veiculo)?.temPlaca ?? true

// Motivo por escrito de por que o registro ainda não fecha; `null` quando
// fecha. Placa é obrigatória em moto e carro: é ela que separa três "José" no
// mesmo dia quando um problema aparece depois.
export function motivoDoEntregador({ nome, veiculo = 'moto', placa = '' } = {}) {
  if (!String(nome ?? '').trim()) return 'Falta o nome de quem vai levar.'
  const limpa = normalizarPlaca(placa)
  if (!limpa) return veiculoTemPlaca(veiculo) ? 'Falta a placa. É ela que separa entregadores com o mesmo nome.' : null
  if (!PLACA.test(limpa)) return 'Placa com 7 letras e números, como ABC1D23 ou ABC1234.'
  return null
}

export function montarEntregador({ nome, veiculo = 'moto', placa = '', empresa = 'propria' }) {
  const limpo = String(nome ?? '').trim()
  if (!limpo) return null
  return {
    tipo: empresa === 'propria' ? 'motoboy' : 'plataforma',
    nome: limpo,
    veiculo,
    placa: normalizarPlaca(placa),
    empresa,
  }
}

// Partes de leitura de qualquer forma que o pedido já guardou: registro novo,
// "eu mesma levo", entregador de chamado antigo (só nome) e o texto fixo das
// sementes de antes ("motoboy da casa"), que não tem registro nenhum.
export function partesDoEntregador(entregador) {
  if (!entregador) return null
  if (typeof entregador === 'string') {
    return { nome: entregador, detalhes: ['sem placa registrada'] }
  }
  if (entregador.tipo === 'propria') return { nome: 'Eu mesma (Thatiane)', detalhes: [] }
  const veiculo = rotuloDe(VEICULOS, entregador.veiculo)
  const comPlaca = [veiculo, entregador.placa].filter(Boolean).join(' ')
  return {
    nome: entregador.nome ?? 'Entregador sem nome',
    detalhes: [comPlaca || null, rotuloDe(EMPRESAS_DE_ENTREGA, entregador.empresa)].filter(Boolean),
  }
}

export function textoDoEntregador(entregador) {
  const partes = partesDoEntregador(entregador)
  if (!partes) return null
  return [partes.nome, ...partes.detalhes].join(' · ')
}

// Número que o entregador confere na retirada, como no iFood. A comanda
// ("0186") é interna da cozinha e do canhoto; misturar os dois fazia o
// entregador pedir um número e a cozinha procurar outro. Com integração, o
// número vem da plataforma (`pedido.numeroEntrega`) e tem precedência. Sem
// ela, sai da comanda por uma conta que não repete: multiplicar por 7919
// (primo, não divide 9000) é uma troca um a um dentro dos 9000 números de
// quatro dígitos.
//
// #1474 (R6): pedido do EasyStok já tem o número que a Cozinha, as Entregas e o cliente leem
// (8 letras do id); inventar outro fazia o entregador conferir um número que não existe.
export function numeroParaEntregador(pedido) {
  if (pedido?.numeroEntrega) return '#' + pedido.numeroEntrega
  if (pedido?.pedidoId) return pedido.numero
  const curto = numeroCurto(pedido?.numero ?? '')
  let base = Number.parseInt(curto, 10)
  if (Number.isNaN(base)) base = [...String(curto)].reduce((soma, letra) => (soma * 31 + letra.charCodeAt(0)) % 9000, 7)
  return '#' + String((((base % 9000) * 7919) + 1237) % 9000 + 1000)
}

const ultimaDaRegra = (mensagens, regra) => [...(mensagens ?? [])].reverse().find((m) => m.regra === regra)?.em ?? null

// Quem levou este pedido, quando saiu e quando chegou. O registro é o próprio
// `pedido.entregador`, gravado no despacho (`aplicacao/reducer.js:
// AVANCAR_ESTEIRA` e `casos/entregas.js: SAIR_PARA_ENTREGA`); as horas vêm
// dos avisos automáticos que o despacho e a entrega já mandam ao cliente, sem
// campo novo no estado.
export function registroDaEntrega(conversa) {
  const pedido = conversa?.pedido
  if (!pedido || (pedido.estado !== 'entrega' && pedido.estado !== 'entregue')) return null
  const entregador = pedido.entregador ?? entregadorResolvido(pedido)
  if (!entregador) return null
  return {
    entregador,
    numero: numeroParaEntregador(pedido),
    saiuEm: ultimaDaRegra(conversa.mensagens, 'esteira-entrega'),
    entregueEm: pedido.estado === 'entregue' ? ultimaDaRegra(conversa.mensagens, 'esteira-entregue') : null,
  }
}

const chaveDoEntregador = (e) => `${e.nome.trim().toLowerCase()}|${e.placa ?? ''}`

const registroCompleto = (e) => e && typeof e === 'object' && e.tipo !== 'propria' && e.nome

// Quem já dá para escolher num toque: os cadastrados da casa e quem foi
// digitado hoje em outro pedido. Rótulo com a placa, para "José" nunca ficar
// igual a "José".
export function entregadoresConhecidos(conversas, cadastrados = []) {
  const vistos = new Map()
  const doDia = (conversas ?? []).map((c) => c.pedido?.entregador).filter(registroCompleto)
  for (const entregador of [...cadastrados, ...doDia]) {
    const chave = chaveDoEntregador(entregador)
    if (vistos.has(chave)) continue
    const partes = partesDoEntregador(entregador)
    vistos.set(chave, { chave, rotulo: [partes.nome, ...partes.detalhes].join(' · '), entregador })
  }
  return [...vistos.values()]
}

// Tudo que `componentes/EscolhaDeEntregador.jsx` precisa, numa chamada só: o
// componente não pode importar `dominio` (`ferramentas/verificar-camadas.mjs`),
// então cada tela que despacha (ficha, cozinha, entregas) passa isto adiante.
export const opcoesDoDespacho = (conversas, cadastrados) => ({
  empresas: EMPRESAS_DE_ENTREGA,
  veiculos: VEICULOS,
  conhecidos: entregadoresConhecidos(conversas, cadastrados),
  validar: motivoDoEntregador,
  montar: montarEntregador,
  aviso: AVISO_SEM_INTEGRACAO,
})
