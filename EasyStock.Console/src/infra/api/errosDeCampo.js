// #2457 (B5): o `ValidationProblemDetails` da API (`{ title, errors: { Campo: [mensagem] } }`) chegava ao
// formulário só como "Confira os campos.". Aqui cada chave vira o nome do campo no formulário (camelCase, sem
// o prefixo `$.` do JSON nem o do comando), com a primeira mensagem dele.
// Item de coleção ("Variacoes[0].Nome") não é campo do formulário: fica só na mensagem geral (revisão da #2460).
const nomeDoCampo = (chave) => {
  if (/\[\d*\]/.test(chave)) return ''
  const ultimo = String(chave).replace(/^\$\.?/, '').split('.').pop().replace(/\[\d+\]$/, '')
  return ultimo ? ultimo[0].toLowerCase() + ultimo.slice(1) : ''
}

export function camposDoErro(json) {
  const erros = json?.errors
  if (!erros || typeof erros !== 'object' || Array.isArray(erros)) return {}
  const campos = {}
  for (const [chave, mensagens] of Object.entries(erros)) {
    const nome = nomeDoCampo(chave)
    const mensagem = (Array.isArray(mensagens) ? mensagens : [mensagens]).find(m => typeof m === 'string' && m.trim())
    if (nome && mensagem && !campos[nome]) campos[nome] = mensagem.trim()
  }
  return campos
}
