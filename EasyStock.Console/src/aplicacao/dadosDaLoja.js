import { CAMPOS_DA_LOJA, corpoDaLoja, lojasAtivas } from '../dominio/dadosDaLoja'
import { atualizarLoja, listarLojasDaEmpresa } from '../infra/api/lojasApi'

// Dados da loja (#2070): as lojas ativas da empresa e o PUT com os cinco campos.
export const carregarLojasAtivas = async () => lojasAtivas(await listarLojasDaEmpresa())

// O PUT responde 204: o que ficou gravado é o corpo enviado.
export async function salvarDadosDaLoja(loja, form) {
  const corpo = corpoDaLoja(loja, form)
  await atualizarLoja(loja.id, corpo)
  return { ...loja, ...Object.fromEntries(CAMPOS_DA_LOJA.map((campo) => [campo, corpo[campo]])) }
}
