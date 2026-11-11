import { useEffect, useRef, useState } from 'react'
import { baixarPdf } from '../../componentes/pdfNoNavegador'
import { gerarFichaClientePdf } from '../../aplicacao/fichaClientePdf'

// #2232 (F1): o PDF da ficha deixou de ser um botão só de ícone no cabeçalho e virou item do menu
// "Ações da ficha". A lógica do botão de antes mora aqui: gera, baixa e devolve o motivo quando não dá.
export function useBaixarFicha({ clienteId, fonteApi }) {
  const [baixando, setBaixando] = useState(false)
  const [erro, setErro] = useState(null)
  const montado = useRef(true)
  useEffect(() => { montado.current = true; return () => { montado.current = false } }, [])
  const motivo = !fonteApi ? 'Conecte-se à loja para baixar a ficha.' : !clienteId ? 'Cadastre o cliente para baixar a ficha.' : null
  async function baixar() {
    if (baixando || motivo) return
    setBaixando(true); setErro(null)
    try {
      const arquivo = await gerarFichaClientePdf(clienteId)
      if (montado.current) baixarPdf(arquivo)
    } catch (falha) {
      if (montado.current) setErro(falha.message || 'Não foi possível baixar a ficha. Tente novamente.')
    } finally { if (montado.current) setBaixando(false) }
  }
  return { baixar, baixando, erro, motivo }
}
