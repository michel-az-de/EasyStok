import { Botao } from '../../../../componentes/Botao'
import { FolhaImpressao } from '../../../../componentes/FolhaImpressao'
import { Modal } from '../../../../componentes/Modal'
import { useEtiquetaDoMotor } from '../../../../aplicacao/etiquetaDoMotor'
import { EtiquetaDeProducao } from './EtiquetaDeProducao'
import css from './etiqueta.module.css'

// #2081 (A4) e #2247 (impressão v3, G1 item 7): a prévia das etiquetas de um lote, a mesma na
// Produção e no Estoque do dia. A etiqueta da prévia é a imagem que vai para o papel, em tamanho
// real; "Imprimir" só libera quando o motor desenhou, e a folha põe uma etiqueta por página.
// impressao: { loteId } buscando, com `folha` pronta, com `erro` falhou.
export function PreviaDasEtiquetas({ loteId, codigo, quantas, impressao, aoFechar }) {
  const doLote = impressao?.loteId === loteId
  const folha = doLote ? impressao.folha : null
  const erro = doLote ? impressao.erro : null
  const total = folha?.etiquetas.length ?? quantas
  const primeira = folha?.etiquetas[0] ?? null
  const desenho = useEtiquetaDoMotor(primeira, folha?.empresa)
  const pronta = Boolean(primeira) && !desenho.carregando && !desenho.recusa
  const rotulo = `Imprimir ${total} ${total === 1 ? 'etiqueta' : 'etiquetas'}`
  return (
    <>
      <Modal titulo={`Etiquetas do lote ${codigo}`} largura="min(520px, calc(100vw - 24px))" aoFechar={aoFechar}
        descricao="Uma etiqueta por porção, no papel de 80 x 40 mm. A prévia é o que a impressora recebe."
        rodape={<>
          <Botao variante="texto" onClick={aoFechar}>Fechar</Botao>
          <Botao variante="primario" icone="printer" disabled={!pronta} onClick={imprimirFolha}>{rotulo}</Botao>
        </>}>
        {erro ? <p className={css.recusa} role="alert">Não foi possível carregar as etiquetas deste lote. {erro}</p>
          : !folha ? <p className={css.situacao}>Buscando as etiquetas…</p>
            : !primeira ? <p className={css.situacao}>Este lote não tem etiquetas para imprimir.</p>
              : (
                <div className={css.previa}>
                  <div className={css.moldura}><EtiquetaDeProducao etiqueta={primeira} empresa={folha.empresa} total={total} /></div>
                  {total > 1 && <p className={css.situacao}>E mais {total - 1}, uma por página.</p>}
                </div>
              )}
      </Modal>
      {pronta && (
        <FolhaImpressao>
          <div className={css.folha}>
            {folha.etiquetas.map((e) => <EtiquetaDeProducao key={e.id} etiqueta={e} empresa={folha.empresa} total={total} />)}
          </div>
        </FolhaImpressao>
      )}
    </>
  )
}

// Imprime depois que as imagens da folha decodificaram: página em branco gasta papel.
async function imprimirFolha() {
  const imagens = [...(globalThis.document?.querySelectorAll('[data-imprimir] img') ?? [])]
  await Promise.all(imagens.map((i) => i.decode?.().catch(() => {})))
  window.print()
}
