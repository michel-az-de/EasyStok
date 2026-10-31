import { useEffect, useRef, useState } from 'react'
import { BotaoIcone } from '../../componentes/BotaoIcone'
import { baixarPdf } from '../../componentes/pdfNoNavegador'
import { gerarFichaClientePdf } from '../../aplicacao/fichaClientePdf'
import css from './ficha.module.css'

export function BotaoBaixarFicha({ clienteId, fonteApi }) {
  const [baixando, setBaixando] = useState(false)
  const [erro, setErro] = useState(null)
  const montado = useRef(true)
  useEffect(() => { montado.current = true; return () => { montado.current = false } }, [])
  async function baixar() {
    if (baixando || !fonteApi || !clienteId) return
    setBaixando(true); setErro(null)
    try {
      const arquivo = await gerarFichaClientePdf(clienteId)
      if (montado.current) baixarPdf(arquivo)
    } catch (falha) {
      if (montado.current) setErro(falha.message || 'Não foi possível baixar a ficha. Tente novamente.')
    } finally { if (montado.current) setBaixando(false) }
  }
  return <span className={css.pdfDaFicha}>
    <BotaoIcone className={css.iconeFicha} rotulo={baixando ? 'Preparando PDF' : 'Baixar ficha em PDF'} icone="download"
      dica={!fonteApi ? 'Conecte-se à loja para baixar a ficha.' : !clienteId ? 'Cadastre o cliente para baixar a ficha.' : 'Baixar ficha em PDF'}
      disabled={!fonteApi || !clienteId} aria-disabled={baixando || !fonteApi || !clienteId} aria-busy={baixando} onClick={baixar} />
    {erro && <output role="alert" className={css.erroPdf}>{erro}</output>}
  </span>
}
