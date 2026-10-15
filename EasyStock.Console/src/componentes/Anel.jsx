import css from './Anel.module.css'

const clamp01 = (v) => Math.min(1, Math.max(0, v))

// Anel genérico (seção 6 e 8 da direção visual, passo zero): tamanho, traço,
// fração preenchida, tom da cor e o conteúdo do centro. Nasceu do anel do
// Pix (RelogioPix.jsx), que passa a usar este componente sem mudar o visual;
// o relógio pequeno de cada entrega (44 px, seção 6) é a mesma família, só
// com `tamanho`/`traco` menores.
export function Anel({
  tamanho = 128, traco = 10, fracao, tom = 'ok', trilhoParado = false, children,
}) {
  const raio = (tamanho - traco) / 2
  const circunferencia = 2 * Math.PI * raio
  const offset = -circunferencia * (1 - clamp01(fracao))
  const classeTrilho = [css.trilho, trilhoParado ? css.trilhoParado : ''].filter(Boolean).join(' ')
  return (
    <span className={css.aro} style={{ width: tamanho, height: tamanho }}>
      <svg className={css.anel} width={tamanho} height={tamanho} viewBox={`0 0 ${tamanho} ${tamanho}`}>
        <circle className={classeTrilho} cx={tamanho / 2} cy={tamanho / 2} r={raio} strokeWidth={traco} />
        <circle
          className={`${css.arco} ${css[tom]}`} cx={tamanho / 2} cy={tamanho / 2} r={raio}
          strokeWidth={traco} strokeDasharray={`${circunferencia} ${circunferencia}`}
          strokeDashoffset={offset}
        />
      </svg>
      <span className={css.centro}>{children}</span>
    </span>
  )
}
