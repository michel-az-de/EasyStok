// #2232 (banca UX, L11): a faixa de "não entregues" pode ser dispensada. A dispensa guarda quais
// mensagens já foram vistas; a faixa volta só quando aparece uma falha que não estava lá. Puro,
// para a prova (`ferramentas/prova-2232-lista.mjs`) conferir a mesma regra da tela.

// #2285: a dispensa é de uma empresa e de um usuário. Sem escopo, quem entra depois na mesma aba herdava a
// faixa dispensada pela pessoa anterior. A chave antiga (sem escopo) não diz de quem era: é descartada, e o
// custo é a faixa reaparecer uma vez.
const CHAVE_ANTIGA = 'easystok.naoEntreguesDispensadas'
const PREFIXO = 'easystok.naoEntreguesDispensadas.v2'

export function chaveDaDispensa(sessao) {
  const empresa = sessao?.empresa?.id
  const usuario = sessao?.usuario?.id
  if (typeof empresa !== 'string' || !empresa || typeof usuario !== 'string' || !usuario) return null
  return `${PREFIXO}:${encodeURIComponent(empresa)}:${encodeURIComponent(usuario)}`
}

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
export function lerDispensa(chave) {
  try { globalThis.sessionStorage?.removeItem(CHAVE_ANTIGA) } catch { /* armazenamento indisponível */ }
  if (!chave) return null
  try {
    const valor = JSON.parse(globalThis.sessionStorage?.getItem(chave) ?? 'null')
    return Array.isArray(valor) ? valor : null
  } catch {
    return null
  }
}

export function gravarDispensa(vistas, chave) {
  if (!chave) return
  try {
    globalThis.sessionStorage?.setItem(chave, JSON.stringify(vistas))
  } catch {
    // Sem armazenamento a dispensa vale só enquanto a tela estiver aberta.
  }
}
