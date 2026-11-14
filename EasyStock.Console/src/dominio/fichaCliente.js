// #2495 (S6, I4): regras da ficha do cliente no editor de Cadastros. A API só adiciona e remove endereço e
// telefone extra (`POST` e `DELETE` em /api/clientes/{id}/enderecos e /telefones): não há edição de item gravado.
import { dataHora, plural } from './formato'
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

// Mesma regra do cadastro rápido (#2258): telefone com DDD.
export const erroDoTelefoneExtra = (numero) => erroDoTelefone(numero)
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
