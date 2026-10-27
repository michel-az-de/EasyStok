import { FUSO } from '../../../../dominio/formato'
import { barrasDoCode128, caminhoDoQr, codigoDaEtiqueta, matrizDoQr } from './codigos'
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

// Revisão da #2081 (R1): a etiqueta antiga do console imprimia os alérgenos; a de comida tem de
// declarar (RDC 26/2015). Os nomes são os do renderizador do PWA (pwa/etiqueta/render.js), em
// maiúsculas como pede a norma: "ALÉRGICOS: CONTÉM GLÚTEN, LACTOSE E OVO."
const ALERGENOS = {
  gluten: 'glúten', lactose: 'lactose', ovo: 'ovo', soja: 'soja', amendoim: 'amendoim',
  castanhas: 'castanhas', peixe: 'peixe', crustaceos: 'crustáceos', outros: 'outros',
}
function textoDosAlergenos(lista) {
  const nomes = (lista ?? []).map((a) => ALERGENOS[a] ?? String(a)).filter(Boolean)
  if (nomes.length === 0) return null
  const juntos = nomes.length === 1 ? nomes[0] : `${nomes.slice(0, -1).join(', ')} e ${nomes.at(-1)}`
  return `ALÉRGICOS: CONTÉM ${juntos.toUpperCase()}.`
}

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
  const alergenos = textoDosAlergenos(etiqueta.alergenos)
  // Revisão da #2081 (R2): QR, barras e texto com o mesmo código saneado.
  const codigo = codigoDaEtiqueta(etiqueta.codigo)
  return (
    <article className={`${css.etiqueta} ${alergenos ? css.comAlergenos : ''}`} aria-label={`Etiqueta ${etiqueta.sequencial} de ${total}, lote ${etiqueta.lote}`}>
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
          <Qr codigo={codigo} />
          <span className={css.sequencial}>{etiqueta.sequencial}/{total}</span>
        </div>
      </div>
      {alergenos && <p className={css.alergenos}>{alergenos}</p>}
      <Barras codigo={codigo} />
      <p className={css.codigo}>{codigo}</p>
    </article>
  )
}
