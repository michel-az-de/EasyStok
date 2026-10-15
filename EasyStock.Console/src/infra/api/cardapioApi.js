import { chamarApi } from './cliente'
import { API_BASE } from '../fonteDados'
import { urlDeExibicaoDaFoto } from '../../dominio/vitrineCardapio'

// Cardápio pelo console (#1241, F11). Dia e saldo: rotas do Operador. Item (incluir, editar, tirar
// e repor) e a lista de fora: rotas do Gerente. Tirar esconde o item, nunca apaga.
const CARDAPIO = '/api/atendimento/comanda/cardapio'
const ITEM = (id) => `${CARDAPIO}/${id}`

export const definirDisponibilidade = (id, disponivel) =>
  chamarApi(`${ITEM(id)}/disponivel`, { metodo: 'POST', corpo: { disponivel } })

// `porcao` (#1542): `{ variacaoId }` ou `{ semPorcao: true }` em prato com porções; sem ela, o prato inteiro.
export const ajustarSaldoDoItem = (id, quantidadeContada, motivo, porcao = null) =>
  chamarApi(`${ITEM(id)}/saldo`, { metodo: 'POST', corpo: { quantidadeContada, motivo, ...(porcao ?? {}) } })

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
  // M1.3 (#1483): "" tira da categoria, id põe nela; ausente não mexe.
  ...('secaoId' in dados ? { secaoId: dados.secaoId ?? '' } : {}),
  // M1.4a (#1529): a lista inteira de porções; ausente não mexe.
  ...('porcoes' in dados ? { porcoes: dados.porcoes } : {}),
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

export const enviarFotoDoPrato = (id, arquivo, galeria = false) => {
  const formulario = new FormData()
  formulario.append('file', arquivo)
  return chamarApi(`${ITEM(id)}/foto?galeria=${galeria}`, { metodo: 'POST', formulario })
}

// Detalhe → valores do formulário (a comanda não traz a ficha: ingredientes e alérgenos).
export const detalheDaApi = (d) => ({
  nome: d.nome ?? '',
  linha: LINHA_DA_GESTAO[d.linha] ?? 'servir',
  porcao: d.porcao ?? '',
  preco: d.preco ?? 0,
  foto: urlDeExibicaoDaFoto(d.fotoUrl, API_BASE),
  fotos: (d.fotos ?? []).map((url) => urlDeExibicaoDaFoto(url, API_BASE)).filter(Boolean),
  produtoId: d.produtoId ?? null,
  descricao: d.descricao ?? '',
  ingredientes: d.ingredientes ?? '',
  alergenos: d.alergenos ?? '',
  tempoPreparoMinutos: d.tempoPreparoMinutos ?? null,
  instrucaoFinalizacao: d.instrucaoFinalizacao ?? '',
  novidadeAte: d.novidadeAte ? `${d.novidadeAte}T23:59:59-03:00` : null,
  secaoId: d.secaoId ?? null,
  porcoes: (d.porcoes ?? []).map((p) => ({
    id: p.id, rotulo: p.rotulo, preco: p.preco, peso: p.peso ?? '', disponivel: p.disponivel !== false, padrao: p.padrao === true, sku: p.sku ?? null,
    // M1.4e (#1552): a embalagem da porção. API anterior não manda o campo, e o console então não mexe nela.
    ...(p.embalagemId !== undefined ? { embalagemId: p.embalagemId ?? null } : {}),
  })),
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
  porcoes: i.porcoes ?? 0,
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
  // #1803: a seção (categoria nova) do item; null = ainda só no texto livre antigo.
  secaoId: i.secaoId ?? null,
  // #1808: o que falta na apresentação (SemFoto, SemCategoria, SemDescricao, DescricaoCurta).
  pendencias: i.pendencias ?? [],
})
