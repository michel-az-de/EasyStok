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
    cliente: { telefone: dados.telefone || null, endereco: endereco || null },
  }
}
