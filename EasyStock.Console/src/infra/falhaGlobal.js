// #2393: erro de handler ou promessa solta não passa por LimiteDeErro e antes só ia ao console.
// Este módulo guarda a última falha (uma faixa discreta mostra) e instala os ouvintes globais.
// Não há endpoint de log no console: o registro é o console do navegador.
export const TEXTO_DA_FALHA = 'Algo falhou. Confira o que estava fazendo.'

export function criarFaixaDeFalha() {
  let estado = { falhou: false, total: 0 }
  const ouvintes = new Set()
  const mudar = (novo) => { estado = novo; ouvintes.forEach((o) => o()) }
  return {
    registrar: () => mudar({ falhou: true, total: estado.total + 1 }),
    dispensar: () => { if (estado.falhou) mudar({ ...estado, falhou: false }) },
    ver: () => estado,
    assinar: (ouvinte) => { ouvintes.add(ouvinte); return () => ouvintes.delete(ouvinte) },
  }
}

export const faixaDeFalha = criarFaixaDeFalha()

export function instalarFalhaGlobal(janela, faixa = faixaDeFalha, log = console) {
  const aoRejeitar = (evento) => { log.error('[falha global] promessa sem tratamento:', evento?.reason); faixa.registrar() }
  const aoErro = (evento) => {
    // Erro de carregamento de imagem ou script chega sem `error`: não é falha de ação.
    if (!evento?.error && !evento?.message) return
    log.error('[falha global] erro não tratado:', evento.error ?? evento.message)
    faixa.registrar()
  }
  janela.addEventListener('unhandledrejection', aoRejeitar)
  janela.addEventListener('error', aoErro)
  return () => { janela.removeEventListener('unhandledrejection', aoRejeitar); janela.removeEventListener('error', aoErro) }
}
