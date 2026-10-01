// Chaves das integrações no modo API (F16, #1246): o que a aba e a faixa do topo dizem sobre
// cada integração que o EasyStok devolve. Funções puras; o segredo nunca passa por aqui.

const ROTULOS = { accessToken: 'Access token', apiKey: 'Chave de API', apiSecret: 'Segredo da API' }

export const rotuloDoCampo = (campo) => ROTULOS[campo] ?? campo

const juntar = (nomes) => (nomes.length < 2 ? nomes.join('') : `${nomes.slice(0, -1).join(', ')} e ${nomes.at(-1)}`)

// Faixa vermelha do topo: só "parada" (último teste falhou). "Vencendo" fica no cartão.
export function textoDaFaixa(lista) {
  const paradas = (lista ?? []).filter((i) => i.alerta === 'parada').map((i) => i.nome)
  if (paradas.length === 0) return null
  return paradas.length === 1 ? `Integração parada: ${paradas[0]}` : `Integrações paradas: ${juntar(paradas)}`
}

const horaDe = (iso) => new Date(iso).toLocaleString('pt-BR', {
  timeZone: 'America/Sao_Paulo', day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit',
})

export function textoDoUltimoTeste(item) {
  if (!item?.ultimoTesteEm) return 'Ainda não testado'
  const quando = horaDe(item.ultimoTesteEm)
  return item.ultimoTesteOk
    ? `Ok em ${quando}${item.ultimoTesteMensagem ? `: ${item.ultimoTesteMensagem}` : ''}`
    : `Falhou em ${quando}: ${item.ultimoTesteMensagem ?? 'sem detalhe do provedor'}`
}

export const ORIGEM = { loja: 'Chave da loja', global: 'Chave da FMA' }
