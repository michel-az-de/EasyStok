import { Botao } from './Botao'
import { baixarPdf, imprimirPdf } from './pdfNoNavegador'

// "Baixar PDF" e "Imprimir" (rodada 11, issue #9), para qualquer documento que
// sai em papel. Quem chama entrega `gerar`, que devolve `{ bytes, nomeArquivo }`
// já montados (dominio/impressao.js); este arquivo só sabe falar com o
// navegador. Gera na hora do clique, nunca no render: o PDF sai sempre do
// estado de agora.
export function AcoesPdf({ gerar, aoImprimir, planoB, varianteImprimir = 'primario' }) {
  const imprimir = () => {
    imprimirPdf(gerar(), { planoB })
    aoImprimir?.()
  }
  return (
    <>
      <Botao icone="download" onClick={() => baixarPdf(gerar())}>Baixar PDF</Botao>
      <Botao variante={varianteImprimir} icone="printer" onClick={imprimir}>Imprimir</Botao>
    </>
  )
}
