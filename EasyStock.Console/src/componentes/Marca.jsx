import css from './Marca.module.css'

// Derivada 420 × 280, sem recorte, do logo-oficial.png do design-system-v1.
// SHA-256 original: 785c453d6a011a0002efb8173c3e27d42508e9792d75826e7314b2ef63db56bc.
const logo = new URL('../assets/casa-da-baba.png', import.meta.url).href

export function Marca({ compacta = false }) {
  return (
    <span className={`${css.marca} ${compacta ? css.compacta : ''}`}>
      <img src={logo} width="120" height="80" alt="Casa da Baba" />
      {!compacta && <span className={css.produto}>EasyStok</span>}
    </span>
  )
}
