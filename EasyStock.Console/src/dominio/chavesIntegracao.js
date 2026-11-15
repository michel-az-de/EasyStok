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

// #2600 (Q02): o "Ligado" da lista diz só que há credencial. Quem envia é o `provider` do
// `GET api/integracoes/whatsapp/status` (o mesmo da tela Canais). Sem o status (ainda carregando,
// 403 ou 404), o cartão fica como a API diz.
const NUMERO_DE_TESTE_DA_META = /\(Test Number\)/i

export function situacaoDoCartao(item, statusWhatsApp) {
  const padrao = { pilula: { tom: item?.ativo ? 'ok' : 'neutro', texto: item?.ativo ? 'Ligado' : 'Desligado' }, avisos: [] }
  if (item?.provider !== 'whatsapp' || !item.ativo) return padrao

  if (statusWhatsApp?.provider === 'wppconnect') {
    return {
      pilula: { tom: 'neutro', texto: 'Não é o canal que envia' },
      avisos: [{ tom: 'neutro', texto: 'As mensagens saem hoje pela conexão por QR. Veja o estado dela em Canais.' }],
    }
  }
  if (NUMERO_DE_TESTE_DA_META.test(item.ultimoTesteMensagem ?? '')) {
    return {
      pilula: { tom: 'aviso', texto: 'Número de teste' },
      avisos: [{ tom: 'perigo', texto: 'Número de teste da Meta: não conversa com clientes.' }],
    }
  }
  return padrao
}
