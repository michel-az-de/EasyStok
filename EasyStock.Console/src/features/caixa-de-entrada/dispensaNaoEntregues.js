// #2232 (banca UX, L11): a faixa de "não entregues" pode ser dispensada. A dispensa guarda quais
// mensagens já foram vistas; a faixa volta só quando aparece uma falha que não estava lá. Puro,
// para a prova (`ferramentas/prova-2232-lista.mjs`) conferir a mesma regra da tela.

const CHAVE = 'easystok.naoEntreguesDispensadas'

export function assinaturaDaDispensa(itens) {
  return (itens ?? []).map((item) => String(item?.mensagem?.id ?? '')).filter(Boolean)
}

export function faixaDispensada(itens, vistas) {
  if (!Array.isArray(vistas)) return false
  const conhecidas = new Set(vistas)
  return assinaturaDaDispensa(itens).every((id) => conhecidas.has(id))
}

// Sessão do navegador: a dispensa vale até fechar a aba. Armazenamento bloqueado (aba anônima,
// política do navegador) só faz a faixa voltar no próximo carregamento.
export function lerDispensa() {
  try {
    const valor = JSON.parse(globalThis.sessionStorage?.getItem(CHAVE) ?? 'null')
    return Array.isArray(valor) ? valor : null
  } catch {
    return null
  }
}

export function gravarDispensa(vistas) {
  try {
    globalThis.sessionStorage?.setItem(CHAVE, JSON.stringify(vistas))
  } catch {
    // Sem armazenamento a dispensa vale só enquanto a tela estiver aberta.
  }
}
