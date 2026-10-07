import { enderecoDasPartes, mascaraCep, partesDoEndereco } from '../../dominio/formato'

// Cliente da conversa entre o console e o EasyStok (#1276). A Ficha guarda o endereço numa
// string só ("Rua X, 120, apto 12, Bairro, 05500-000"); a API recebe e devolve em partes.

// String da Ficha -> corpo da API. Sem CEP não há endereço para gravar.
export function enderecoParaApi(texto) {
  const { rua, numero, complemento, bairro, cep } = partesDoEndereco(texto)
  const digitos = cep.replace(/\D/g, '')
  if (!digitos) return null
  return { cep: digitos, logradouro: rua || null, numero: numero || null, complemento: complemento || null, bairro: bairro || null }
}

// Dossiê (S25) -> o que a Ficha mostra. Endereço: o padrão, ou o único.
export function clienteDoDossie(dossie) {
  const dados = dossie?.cliente
  if (!dados?.id) return null
  const enderecos = dossie.enderecos ?? []
  const e = enderecos.find((x) => x.padrao) ?? (enderecos.length === 1 ? enderecos[0] : null)
  const endereco = e
    ? enderecoDasPartes({ rua: e.logradouro, numero: e.numero, complemento: e.complemento, bairro: e.bairro, cep: e.cep ? mascaraCep(e.cep) : '' })
    : null
  return {
    clienteId: dados.id,
    nome: dados.nome || null,
    cliente: {
      telefone: dados.telefone || null,
      endereco: endereco || null,
      pedidos: dossie.totalPedidos ?? 0,
      notas: (dossie.notas ?? []).map(notaDaApi),
      // #1436: últimos pedidos no formato do modal Histórico (o mesmo da massa de demonstração).
      // Fica em `cliente` porque a sincronização de 5 s preserva o cliente lido da API.
      historico: (dossie.ultimosPedidos ?? []).map(pedidoDaApi),
    },
  }
}

const quando = (iso) => new Date(iso).toLocaleString('pt-BR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })

const notaDaApi = (n) => ({ id: n.id, texto: n.texto, autor: n.autor, em: quando(n.criadoEm) })

const pedidoDaApi = (p) => ({
  numero: `#${p.id.slice(0, 8)}`,
  em: p.criadoEm.slice(0, 10),
  total: p.total,
  estado: String(p.status ?? '').toLowerCase().replaceAll('_', ' '),
  itens: (p.itens ?? []).map((i) => `${i.quantidade}× ${i.nome}`),
  nota: null,
})
