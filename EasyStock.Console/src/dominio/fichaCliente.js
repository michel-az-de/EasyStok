// #2495 (S6, I4): regras da ficha do cliente no editor de Cadastros. A API só adiciona e remove endereço e
// telefone extra (`POST` e `DELETE` em /api/clientes/{id}/enderecos e /telefones): não há edição de item gravado.
import { chaveDaTag, recusaDoEndereco } from './cliente'
import { dataHora, moeda, partesDoEndereco, plural } from './formato'
import { erroDoCep, erroDoTelefone } from './validacaoDoCadastro'

const texto = (valor) => String(valor ?? '').trim() || null
const digitos = (valor) => String(valor ?? '').replace(/\D/g, '')

export const ENDERECO_VAZIO = { logradouro: '', numero: '', complemento: '', bairro: '', cidade: '', estado: '', cep: '', referencia: '' }

export function errosDoEndereco(form) {
  const erros = {}
  if (!texto(form.logradouro)) erros.logradouro = 'Informe a rua ou avenida.'
  const cep = erroDoCep(form.cep) ?? (digitos(form.cep).length > 8 ? 'CEP precisa de 8 dígitos' : null)
  if (cep) erros.cep = cep
  if (texto(form.estado) && !/^[A-Za-z]{2}$/.test(texto(form.estado))) erros.estado = 'Use a sigla com 2 letras, como SP.'
  return erros
}

// `padrao` vem de `enderecoVaiComoPadrao`; trocar o padrão fica fora desta tela.
export const corpoDoEndereco = (form, empresaId, padrao) => ({
  empresaId, logradouro: texto(form.logradouro), numero: texto(form.numero), complemento: texto(form.complemento),
  bairro: texto(form.bairro), cidade: texto(form.cidade), estado: texto(form.estado)?.toUpperCase() ?? null,
  cep: digitos(form.cep) || null, referencia: texto(form.referencia), padrao: Boolean(padrao),
})

// Vai como padrão quando nenhum endereço que sobrou é o padrão (lista vazia ou o padrão foi removido).
export const enderecoVaiComoPadrao = (enderecos) => !enderecos.some(e => e.padrao)

// O que foi gravado entra na lista na hora; a recarga do detalhe só confirma. Se ela falhar, a lista não mente.
export function aplicarNaFicha(listas, acao, alvo, corpo, id) {
  if (acao === 'endereco') return { ...listas, enderecos: [...listas.enderecos, { id: id ?? `novo-${listas.enderecos.length}`, ...corpo }] }
  if (acao === 'telefone') return { ...listas, telefones: [...listas.telefones, { id: id ?? `novo-${listas.telefones.length}`, ...corpo }] }
  if (acao === 'removerEndereco') return { ...listas, enderecos: listas.enderecos.filter(e => e.id !== alvo.id) }
  return { ...listas, telefones: listas.telefones.filter(t => t.id !== alvo.id) }
}

export function cepNaTela(cep) {
  const d = digitos(cep)
  return d.length === 8 ? `${d.slice(0, 5)}-${d.slice(5)}` : d
}

export function linhaDoEndereco(e) {
  const rua = [texto(e.logradouro), texto(e.numero)].filter(Boolean).join(', ')
  const principal = texto(e.complemento) ? `${rua} - ${texto(e.complemento)}` : rua
  const cidade = texto(e.cidade) && texto(e.estado) ? `${texto(e.cidade)}/${texto(e.estado)}` : texto(e.cidade) ?? texto(e.estado)
  const detalhe = [texto(e.bairro), cidade, digitos(e.cep) && `CEP ${cepNaTela(e.cep)}`].filter(Boolean).join(' · ')
  return { principal: principal || 'Endereço sem rua', detalhe }
}

// Telefone com DDD, como no cadastro rápido (#2258). #2734: a rota do telefone extra aceita número de outro país
// (Telefone.cs, 7 a 15 dígitos); a da conversa, não.
export const erroDoTelefoneExtra = (numero) => erroDoTelefone(numero, { aceitaDeFora: true })
export const corpoDoTelefone = (numero, whatsapp, empresaId) => ({ empresaId, numero: String(numero ?? '').trim(), whatsapp: Boolean(whatsapp) })

export function resumoDePedidos(cliente) {
  const quantos = Number(cliente?.orderCount ?? 0)
  return {
    contagem: quantos > 0 ? plural(quantos, 'pedido', 'pedidos') : 'Nenhum pedido ainda',
    ultimo: cliente?.lastOrderAt ? `Último pedido em ${dataHora(cliente.lastOrderAt)}` : null,
  }
}

export function mensagemDaFicha(erro) {
  if (erro?.status === 403) return 'Seu acesso não permite mudar este cadastro. Peça a quem administra a loja.'
  return erro?.message || 'Não deu para concluir agora. Tente de novo.'
}

// #2734: o cadastro mostra o mesmo relacionamento da Ficha do Atendimento (notas, tags, bloqueio e conversas).
// Os tetos são os do servidor (nota 500, tag 40): viram mensagem no campo, nunca corte do texto colado.
export const LIMITE_DA_NOTA = 500
export const LIMITE_DA_TAG = 40
// O dossiê (S25) devolve as notas mais recentes até este número.
export const NOTAS_DO_DOSSIE = 20

const acimaDoLimite = (tamanho, limite) => (tamanho > limite ? `Use até ${limite} caracteres. Agora tem ${tamanho}.` : null)

export function erroDaNota(valor) {
  const nota = String(valor ?? '').trim()
  return nota ? acimaDoLimite(nota.length, LIMITE_DA_NOTA) : 'Escreva a nota.'
}

// Repetida pela mesma chave da Ficha (`chaveDaTag`): sem caixa, sem acento e com "_" valendo espaço.
export function erroDaTag(valor, existentes = []) {
  const tag = String(valor ?? '').trim()
  if (!tag) return 'Escreva a tag.'
  return acimaDoLimite(tag.length, LIMITE_DA_TAG)
    ?? (existentes.some(e => chaveDaTag(e) === chaveDaTag(tag)) ? 'Esta tag já existe.' : null)
}

// O telefone principal é o do campo Telefone do cadastro: aparece primeiro e não se remove pela lista.
export const telefonesDaFicha = (telefones = []) => [...telefones]
  .sort((a, b) => Number(Boolean(b.principal)) - Number(Boolean(a.principal)))
  .map(t => ({ ...t, removivel: !t.principal }))

// Atalho da Ficha do Atendimento para o cadastro completo. O roteador lê só módulo e tela; o terceiro
// segmento é de quem monta a lista (mesmo desenho de `filtroDoCardapio`).
const ID_DE_CADASTRO = /^[\da-f]{8}-[\da-f]{4}-[\da-f]{4}-[\da-f]{4}-[\da-f]{12}$/i
export const hashDoCadastroDoCliente = (id) => `#/m/cadastros/clientes/${id}`
export function clienteDaHash(hash) {
  const [, raiz, modulo, tela, id] = String(hash ?? '').split('/')
  return raiz === 'm' && modulo === 'cadastros' && tela === 'clientes' && ID_DE_CADASTRO.test(id ?? '') ? id : null
}

// As palavras são as da Ficha do Atendimento ("valor total", "costuma pedir"). Sem compra não há soma.
// #2849: o quadro que abre a ficha em qualquer tela, com as palavras do Atendimento ("compras válidas",
// "total comprado", "Ticket médio"). Sem o dossiê lido não há número; sem compra não há soma nem média.
export function quadroDoCliente(relacionamento) {
  if (!relacionamento) return []
  const compras = Number(relacionamento.pedidos ?? 0)
  const total = Number(relacionamento.totalGasto ?? 0)
  const quadro = [{ rotulo: compras === 1 ? 'compra válida' : 'compras válidas', valor: String(compras) }]
  if (compras > 0 && total > 0) quadro.push({ rotulo: 'total comprado', valor: moeda(total) }, { rotulo: 'Ticket médio', valor: moeda(total / compras) })
  return quadro
}

// #3249: as abas da ficha (Pedidos, Endereços, Telefones e Conversas). O número é sempre um total conhecido: pedidos
// pelo cadastro (orderCount; a lista da aba traz só os 5 últimos), endereços e telefones pela lista completa do detalhe.
// As conversas do dossiê têm teto, então a aba não leva número; sem dossiê lido não há aba de conversas.
const totalConhecido = (valor) => {
  const n = typeof valor === 'number' ? valor : typeof valor === 'string' && valor.trim() ? Number(valor) : NaN
  return Number.isInteger(n) && n >= 0 ? n : null
}
export function abasDaFicha({ pedidos, enderecos, telefones, comConversas = false }) {
  const aba = (id, rotulo, valor) => {
    const total = totalConhecido(valor)
    return total == null ? { id, rotulo } : { id, rotulo, contador: total, nome: `${rotulo}, ${total}` }
  }
  return [aba('pedidos', 'Pedidos', pedidos), aba('enderecos', 'Endereços', enderecos), aba('telefones', 'Telefones', telefones),
    comConversas && { id: 'conversas', rotulo: 'Conversas' }].filter(Boolean)
}

export const avisoDasNotas =(quantas) => (quantas >= NOTAS_DO_DOSSIE
  ? `Aqui aparecem as ${NOTAS_DO_DOSSIE} notas mais recentes. As anteriores estão na Ficha do Atendimento.` : null)

// #3076 (#2776): o Endereço do Cadastro de clientes ia só como texto solto; a Ficha dizia "Nenhum endereço cadastrado" e o
// Novo pedido abria sem endereço. O Cadastro segue a regra do pedido (CEP no fim) e grava o endereço estruturado padrão.
// Só confere quando mudou: cadastro antigo sem CEP continua salvando os outros campos. Sem `inicial`, é cadastro novo.
const enderecoMudou = (dados, inicial) => String(dados.endereco ?? '').trim() !== String(inicial?.endereco ?? '').trim()
export const erroDoEnderecoDoCadastro = (dados, inicial) => (enderecoMudou(dados, inicial) ? recusaDoEndereco(dados.endereco) : null)

const aptMudou = (dados, inicial) => String(dados.apt ?? '').trim() !== String(inicial?.apt ?? '').trim()
// #2776: o complemento também é do endereço de entrega; mudar só ele regrava o Padrão (sem CEP no endereço, não grava nem recusa).
export const gravaEnderecoDoCadastro = (dados, inicial) => Boolean(texto(dados.endereco)) && (enderecoMudou(dados, inicial) || aptMudou(dados, inicial)) && !recusaDoEndereco(dados.endereco)

// Endereço novo ou mudado, com CEP, vira o padrão do cliente (#2776: também de quem já tem um; a API desmarca o anterior).
// Em cadastro existente, sem a lista relida da API logo antes de gravar (`enderecos`), não grava.
export function enderecoDeEntregaDoCadastro(dados, { empresaId, inicial, enderecos } = {}) {
  if (!gravaEnderecoDoCadastro(dados, inicial)) return null
  if (inicial?.id && !Array.isArray(enderecos)) return null
  const p = partesDoEndereco(dados.endereco)
  const complemento = [p.complemento, texto(dados.apt)].filter(Boolean).join(', ')
  return corpoDoEndereco({ logradouro: p.rua, numero: p.numero, complemento, bairro: p.bairro, cidade: p.cidade, estado: p.uf, cep: p.cep }, empresaId, true)
}
