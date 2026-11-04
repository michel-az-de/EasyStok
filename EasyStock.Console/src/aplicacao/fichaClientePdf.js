import { obterFichaClientePdf } from '../infra/api/fichaClientePdfApi'

export async function gerarFichaClientePdf(clienteId) {
  const arquivo = await obterFichaClientePdf(clienteId)
  const bytes = await arquivo.arrayBuffer()
  if (new TextDecoder().decode(bytes.slice(0, 5)) !== '%PDF-') throw new Error('A ficha não foi recebida em PDF. Tente de novo.')
  return { bytes, nomeArquivo: `ficha-cliente-${clienteId}.pdf` }
}
