import css from './Contador.module.css'

// UM desenho de contador numérico para o app todo (#2232, L2 e T4): pílula (#2246),
// só tokens. `neutro` para quantidade; `alerta` para o que pede ação ou está vencido.
// O número é decoração visual: quem usa põe a quantidade no nome acessível do controle
// ("Lembretes, 3"), por isso `aria-hidden` por padrão. `anunciar` o devolve ao leitor de tela.
export function Contador({ valor, tom = 'neutro', max = 99, anunciar = false, className, ...resto }) {
  // Quem quer esconder o zero não passa o valor; "0" também é informação (Notas 0).
  if (valor == null) return null
  const classes = [css.contador, tom === 'alerta' ? css.alerta : '', className].filter(Boolean).join(' ')
  return (
    <span className={classes} aria-hidden={anunciar ? undefined : 'true'} {...resto}>
      {valor > max ? `${max}+` : valor}
    </span>
  )
}
