import { FUSO } from '../../../../dominio/formato'
import { barrasDoCode128, caminhoDoQr, matrizDoQr } from './codigos'
import css from './etiqueta.module.css'

// #2081 (A4): a etiqueta de produção do console, a mesma do APK piloto: 80x40 mm, empresa, prato,
// fabricação, validade, lote e peso, com QR e código de barras (Code 128) do código da etiqueta.
// Só preto: é o que a térmica imprime. A folha de impressão põe uma por página (`@page` 80x40).
const data = (iso) => (iso ? new Date(iso).toLocaleDateString('pt-BR', { timeZone: FUSO, day: '2-digit', month: '2-digit', year: 'numeric' }) : '—')
const peso = (g) => {
  if (!(g > 0)) return null
  return g >= 1000 ? `${(g / 1000).toLocaleString('pt-BR', { maximumFractionDigits: 2 })} kg` : `${g} g`
}

const SILENCIO_QR = 2

function Qr({ codigo }) {
  const matriz = matrizDoQr(codigo)
  const lado = matriz.length + SILENCIO_QR * 2
  return (
    <svg className={css.qr} viewBox={`0 0 ${lado} ${lado}`} shapeRendering="crispEdges" aria-hidden="true">
      <rect width={lado} height={lado} className={css.papel} />
      <path d={caminhoDoQr(matriz, SILENCIO_QR)} className={css.tinta} />
    </svg>
  )
}

function Barras({ codigo }) {
  const { barras, total } = barrasDoCode128(codigo)
  return (
    <svg className={css.barras} viewBox={`0 0 ${total} 1`} preserveAspectRatio="none" shapeRendering="crispEdges" aria-hidden="true">
      <rect width={total} height={1} className={css.papel} />
      {barras.map((b) => <rect key={b.x} x={b.x} width={b.largura} height={1} className={css.tinta} />)}
    </svg>
  )
}

export function EtiquetaDeProducao({ etiqueta, empresa, total }) {
  const gramas = peso(etiqueta.pesoG)
  return (
    <article className={css.etiqueta} aria-label={`Etiqueta ${etiqueta.sequencial} de ${total}, lote ${etiqueta.lote}`}>
      <div className={css.topo}>
        <div className={css.dados}>
          {empresa && <p className={css.empresa}>{empresa}</p>}
          <p className={css.nome}>{etiqueta.nome}</p>
          <dl className={css.linhas}>
            <div><dt>FAB</dt><dd>{data(etiqueta.produzidoEm)}</dd></div>
            <div><dt>VAL</dt><dd>{data(etiqueta.validadeEm)}</dd></div>
            <div><dt>LOTE</dt><dd>{etiqueta.lote ?? '—'}</dd></div>
            {gramas && <div><dt>PESO</dt><dd>{gramas}</dd></div>}
          </dl>
        </div>
        <div className={css.lado}>
          <Qr codigo={etiqueta.codigo} />
          <span className={css.sequencial}>{etiqueta.sequencial}/{total}</span>
        </div>
      </div>
      <Barras codigo={etiqueta.codigo} />
      <p className={css.codigo}>{etiqueta.codigo}</p>
    </article>
  )
}
