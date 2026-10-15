// Prefixos de fórmula não podem virar comandos quando o CSV abre em uma planilha.
const celula = (valor) => {
  const texto = typeof valor === 'number' ? valor.toFixed(2).replace('.', ',') : String(valor ?? '')
  return `"${(/^[\s]*[=+@-]/.test(texto) && typeof valor !== 'number' ? "'" : '') + texto.replaceAll('"', '""')}"`
}
export function baixarCsv(nome, cabecalho, linhas) {
  const csv = '\ufeff' + [cabecalho, ...linhas].map((linha) => linha.map(celula).join(';')).join('\r\n')
  const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }))
  const link = document.createElement('a')
  link.href = url; link.download = nome; link.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
