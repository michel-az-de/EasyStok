// Conversa com o navegador para o PDF (rodada 11, issue #9): baixar o arquivo
// e mandar imprimir. Fora de `AcoesPdf.jsx` porque a gaveta da fila de
// canhotos também imprime, sem os dois botões.

// Um só quadro de impressão vivo por vez: o da impressão anterior sai quando a
// próxima começa (tirar antes disso cancelaria o diálogo ainda aberto).
let quadroAnterior = null

function urlDoPdf(bytes) {
  return URL.createObjectURL(new Blob([bytes], { type: 'application/pdf' }))
}

export function baixarPdf({ bytes, nomeArquivo }) {
  const url = urlDoPdf(bytes)
  const link = document.createElement('a')
  link.href = url
  link.download = nomeArquivo
  document.body.appendChild(link)
  link.click()
  link.remove()
  // O navegador precisa da URL viva até o download começar.
  setTimeout(() => URL.revokeObjectURL(url), 60000)
}

// Imprime o PRÓPRIO PDF, não a tela: o diálogo recebe a página com o tamanho
// certo (80 mm na bobina, A4 na rota), igual ao arquivo baixado. Precisa do
// leitor de PDF do navegador; sem ele (Chrome no Android) ou se o quadro
// falhar, cai no `planoB`: a folha de impressão da tela quando existe
// (`window.print()` do canhoto), senão o PDF aberto numa aba nova, que tem o
// próprio botão de imprimir.
export function imprimirPdf({ bytes }, { planoB } = {}) {
  const url = urlDoPdf(bytes)
  const semVisualizador = () => {
    if (planoB) { URL.revokeObjectURL(url); planoB(); return }
    // Sem 'noopener': com ele o `open` devolve sempre null e não dá para
    // saber se o navegador bloqueou a aba.
    if (!window.open(url, '_blank')) window.location.assign(url)
  }
  if (!navigator.pdfViewerEnabled) { semVisualizador(); return }

  if (quadroAnterior) {
    URL.revokeObjectURL(quadroAnterior.src)
    quadroAnterior.remove()
  }
  const quadro = document.createElement('iframe')
  quadro.title = 'Impressão'
  quadro.setAttribute('aria-hidden', 'true')
  quadro.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0;visibility:hidden'
  quadro.addEventListener('load', () => {
    try {
      quadro.contentWindow.focus()
      quadro.contentWindow.print()
    } catch {
      semVisualizador()
    }
  }, { once: true })
  quadro.src = url
  document.body.appendChild(quadro)
  quadroAnterior = quadro
}
