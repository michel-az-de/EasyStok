import { useCallback, useState } from 'react'

// Lê um File escolhido no computador como data URL (FileReader). Puro
// wrapper de API de navegador, sem domínio: quem chama valida tipo e tamanho
// (dominio/anexos.js) antes ou depois de ler. Usado pelo anexo de
// arquivo do composer e pela foto da peça da galeria (frente Anexos, rodada 7).
export function useArquivoComoDataUrl() {
  const [carregando, setCarregando] = useState(false)

  const ler = useCallback((arquivo) => new Promise((resolve, reject) => {
    setCarregando(true)
    const leitor = new FileReader()
    leitor.onload = () => { setCarregando(false); resolve(leitor.result) }
    const falhar = (erro) => { setCarregando(false); reject(erro) }
    leitor.onerror = () => falhar(leitor.error ?? new Error('Falha ao ler o arquivo.'))
    leitor.onabort = () => falhar(new DOMException('Leitura do arquivo cancelada.', 'AbortError'))
    try { leitor.readAsDataURL(arquivo) } catch (erro) { falhar(erro) }
  }), [])

  return { carregando, ler }
}
