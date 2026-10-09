import { chamarApi } from './cliente'
import { API_BASE } from '../fonteDados'
import { urlDeExibicaoDaFoto } from '../../dominio/vitrineCardapio'

// Cardápio pelo console (#1241, F11). Dia e saldo: rotas do Operador. Item (incluir, editar, tirar
// e repor) e a lista de fora: rotas do Gerente. Tirar esconde o item, nunca apaga.
const CARDAPIO = '/api/atendimento/comanda/cardapio'
const ITEM = (id) => `${CARDAPIO}/${id}`

export const definirDisponibilidade = (id, disponivel) =>
  chamarApi(`${ITEM(id)}/disponivel`, { metodo: 'POST', corpo: { disponivel } })

export const ajustarSaldoDoItem = (id, quantidadeContada, motivo) =>
  chamarApi(`${ITEM(id)}/saldo`, { metodo: 'POST', corpo: { quantidadeContada, motivo } })

const LINHA_DA_API = { servir: 'ParaServir', casa: 'PrepararEmCasa' }

// Só o que o formulário do console edita; o resto do item fica como está no EasyStok. Campo que não
// veio em `dados` fica fora do corpo (null = não mexe). Novidade: "" tira, "aaaa-mm-dd" define.
const OPCIONAIS = ['descricao', 'ingredientes', 'alergenos', 'tempoPreparoMinutos', 'instrucaoFinalizacao']
export const corpoDoItem = (dados) => ({
  nome: dados.nome?.trim(),
  linha: LINHA_DA_API[dados.linha] ?? null,
  porcao: dados.porcao?.trim() ?? null,
  preco: dados.preco,
  ...Object.fromEntries(OPCIONAIS.filter((c) => dados[c] !== undefined).map((c) => [c, dados[c]])),
  ...('novidadeAte' in dados ? { novidadeAte: dados.novidadeAte ? dados.novidadeAte.slice(0, 10) : '' } : {}),
})

export const incluirItem = (dados) => chamarApi(CARDAPIO, { metodo: 'POST', corpo: corpoDoItem(dados) })
export const editarItem = (id, dados) => chamarApi(ITEM(id), { metodo: 'PUT', corpo: corpoDoItem(dados) })
export const definirVisivel = (id, visivel) =>
  chamarApi(`${ITEM(id)}/visivel`, { metodo: 'POST', corpo: { visivel } })

// M1.2 (#1482): tirar arquiva (≠ ocultar do site); validar libera o item novo para o agente.
export const definirArquivado = (id, arquivado) =>
  chamarApi(`${ITEM(id)}/arquivar`, { metodo: 'POST', corpo: { arquivado } })
export const validarItem = (id) => chamarApi(`${ITEM(id)}/validar`, { metodo: 'POST' })
export const obterItem = (id) => chamarApi(ITEM(id))

// Detalhe → valores do formulário (a comanda não traz a ficha: ingredientes e alérgenos).
export const detalheDaApi = (d) => ({
  descricao: d.descricao ?? '',
  ingredientes: d.ingredientes ?? '',
  alergenos: d.alergenos ?? '',
  tempoPreparoMinutos: d.tempoPreparoMinutos ?? null,
  instrucaoFinalizacao: d.instrucaoFinalizacao ?? '',
  novidadeAte: d.novidadeAte ? `${d.novidadeAte}T23:59:59-03:00` : null,
})

export const listarFora = () => chamarApi(`${CARDAPIO}/fora`)

// Item escondido → produto do console já marcado como fora do cardápio, para o "Repor".
export const itemForaDaApi = (item) => ({
  sku: item.cardapioItemId,
  nome: item.nome,
  linha: 'servir',
  porcao: item.porcao ?? '',
  preco: item.preco,
  estoque: null,
  disponivelHoje: false,
  categoria: item.categoria ?? null,
  foto: urlDeExibicaoDaFoto(item.fotoUrl, API_BASE),
  fotos: [],
  removidoEm: 'fora',
})

// Produto vendido sem saldo (S22): o mesmo cartão de alerta da Ficha, com id estável.
export const listarDesacertos = () => chamarApi('/api/estoque/desacertos')
export const alertaDaApi = (d) => ({ id: `desacerto-${d.produtoId}`, texto: d.texto, daApi: true })

// M1.1 (#1481): gestão do cardápio (Gerente). Todos os itens, inclusive ocultos e desligados.
const LINHA_DA_GESTAO = { ParaServir: 'servir', PrepararEmCasa: 'casa' }

export const listarGestao = () => chamarApi(`${CARDAPIO}/gestao`)
// O servidor troca de lugar e renumera de 1 a n (#1486).
const DIRECAO_DA_API = { subir: 'Subir', descer: 'Descer' }
export const moverItem = (id, direcao) =>
  chamarApi(`${ITEM(id)}/mover`, { metodo: 'POST', corpo: { direcao: DIRECAO_DA_API[direcao] } })

export const itemGestaoDaApi = (i) => ({
  sku: i.cardapioItemId,
  nome: i.nome,
  linha: LINHA_DA_GESTAO[i.linha] ?? 'servir',
  porcao: i.porcao ?? '',
  preco: i.preco,
  categoria: i.categoria ?? null,
  foto: urlDeExibicaoDaFoto(i.fotoUrl, API_BASE),
  noSite: i.visivel,
  hoje: i.disponivel,
  ordem: i.ordem,
  controlaSaldo: i.controlaSaldo,
  arquivado: i.arquivado === true,
  emValidacao: i.emValidacao === true,
  novidadeAte: i.novidadeAte ?? null,
})
