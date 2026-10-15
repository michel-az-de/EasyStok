import { prepararGeometriaImpresso } from './geometriaImpresso.js'

// Manda imprimir uma página HTML pronta (#1632, impressos servidos pela API). A página já traz
// o `@page` da folha; a bobina recebe aqui o comprimento medido (#1756), então o diálogo abre no
// tamanho certo. Irmão de `pdfNoNavegador.js`.

// Um só quadro vivo por vez: o da impressão anterior sai quando a próxima começa (tirar antes
// cancelaria o diálogo ainda aberto, e o `afterprint` não dispara em todo navegador).
let quadroAnterior = null
const MS_ATE_LIBERAR = 60000
// Fonte que não chega (rede ruim) não segura o papel: imprime com a fonte que tiver.
const MS_ESPERA_FONTES = 4000
// O `load` espera as imagens; logo que não responde não pode deixar o botão preso em "Preparando".
const MS_ESPERA_CARGA = 8000

const esperar = (ms) => new Promise((ok) => { setTimeout(ok, ms) })

// Plano B: a página numa aba nova, que tem o imprimir do próprio navegador.
// Sem 'noopener': com ele o `open` devolve sempre null e não dá para saber se foi bloqueado.
function abrirEmAba(html) {
  const url = URL.createObjectURL(new Blob([html], { type: 'text/html;charset=utf-8' }))
  const aba = window.open(url, '_blank')
  setTimeout(() => URL.revokeObjectURL(url), MS_ATE_LIBERAR)
  if (!aba) throw new Error('O navegador bloqueou a aba de impressão. Libere pop-ups para o EasyStok e tente de novo.')
}

function removerQuadro(quadro) {
  quadro.remove()
  if (quadroAnterior === quadro) quadroAnterior = null
}

// Resolve quando o diálogo de impressão foi chamado (ou a aba do plano B abriu); rejeita com
// mensagem legível quando nem o plano B deu certo.
export function imprimirHtml(html, { sinal } = {}) {
  if (typeof html !== 'string' || !html.trim()) return Promise.reject(new Error('O impresso veio vazio.'))
  return new Promise((resolve, reject) => {
    const abortado = () => { if (sinal?.aborted) throw new DOMException('Impressão cancelada.', 'AbortError') }
    const concluir = (destino) => { sinal?.removeEventListener('abort', cancelar); resolve(destino) }
    const falhar = (erro) => { sinal?.removeEventListener('abort', cancelar); reject(erro) }
    // A aba do plano B leva a cópia já medida, quando houver.
    let paraImprimir = html
    const planoB = () => { try { abortado(); abrirEmAba(paraImprimir); concluir('aba') } catch (erro) { falhar(erro) } }
    if (quadroAnterior) removerQuadro(quadroAnterior)
    const quadro = document.createElement('iframe')
    quadro.title = 'Impressão'
    quadro.setAttribute('aria-hidden', 'true')
    // A página é só papel, sem script. `allow-modals` é o que deixa o `print()` abrir.
    quadro.setAttribute('sandbox', 'allow-same-origin allow-modals')
    quadro.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;border:0;visibility:hidden'
    let decidido = false
    const cancelar = () => {
      decidido = true
      clearTimeout(prazo)
      removerQuadro(quadro)
      falhar(new DOMException('Impressão cancelada.', 'AbortError'))
    }
    const prazo = setTimeout(() => {
      if (decidido) return
      decidido = true
      removerQuadro(quadro)
      planoB()
    }, MS_ESPERA_CARGA)
    quadro.addEventListener('load', async () => {
      if (decidido) return
      decidido = true
      clearTimeout(prazo)
      try {
        // `load` já esperou as imagens (logo, QR); as fontes da página têm promessa própria.
        await Promise.race([quadro.contentDocument?.fonts?.ready, esperar(MS_ESPERA_FONTES)])
        abortado()
        try { prepararGeometriaImpresso(quadro.contentDocument) } catch (erro) {
          // Papel errado não abre o diálogo: o motivo volta para a tela.
          removerQuadro(quadro)
          falhar(erro)
          return
        }
        const raiz = quadro.contentDocument?.documentElement
        if (raiz) paraImprimir = '<!DOCTYPE html>' + raiz.outerHTML
        quadro.contentWindow.addEventListener('afterprint', () => removerQuadro(quadro), { once: true })
        quadro.contentWindow.focus()
        quadro.contentWindow.print()
        concluir('dialogo')
      } catch (erro) {
        removerQuadro(quadro)
        if (sinal?.aborted) falhar(erro)
        else planoB()
      }
    }, { once: true })
    sinal?.addEventListener('abort', cancelar, { once: true })
    if (sinal?.aborted) { cancelar(); return }
    quadro.srcdoc = html
    document.body.appendChild(quadro)
    quadroAnterior = quadro
  })
}
