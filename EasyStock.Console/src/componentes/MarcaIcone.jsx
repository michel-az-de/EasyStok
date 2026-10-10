import css from './Icone.module.css'

// Casa da Baba V4 / Essencial (01/10/2026), geometria própria de icones.cjs.
// Origem: marca/sistema-visual-pranchas-v4; SVGs íntegros em assets/icones-marca-v4.
// Mesmos nós e atributos, convertidos apenas para a grafia JSX; nenhuma entrada HTML.
const DESENHOS = {
  abaixo: <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M5 11 16 22 27 11"/></g>,
  acima: <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M5 21 16 10 27 21"/></g>,
  adicionar: <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M16 5V27 M5 16H27"/></g>,
  buscar: <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><circle cx="13" cy="13" r="9"/><path d="M20 20 29 29"/></g>,
  cancelar: <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M7 7 25 25 M25 7 7 25"/></g>,
  confirmar: <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M5 17 12 24 27 8"/></g>,
  duplicar: <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><rect x="3" y="3" width="18" height="21" rx="1.5"/><path d="M25 8H29V29H11V27 M8 14H16 M12 10V18"/></g>,
  editar: <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M5 27 7 19 23 3 29 9 13 25Z M7 19 13 25 M20 6 26 12 M5 27 13 25"/></g>,
  excluir: <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M5 8H27 M11 8V4H21V8 M8 8 10 28H22L24 8 M13 13V23 M19 13V23"/></g>,
  filtrar: <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M3 5H29L19 17V26L13 29V17Z"/></g>,
  grade: <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><rect x="4" y="4" width="9" height="9" rx="1.5"/><rect x="19" y="4" width="9" height="9" rx="1.5"/><rect x="4" y="19" width="9" height="9" rx="1.5"/><rect x="19" y="19" width="9" height="9" rx="1.5"/></g>,
  informacao: <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><circle cx="16" cy="16" r="12"/><path d="M16 15V24 M13 24H19"/><circle cx="16" cy="9" r="0.7"/></g>,
  oculto: <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M3 3 29 29 M10 8C18 4 26 9 30 16L26 21 M22 25C13 29 6 23 2 16L6 11 M13 13C8 19 18 24 21 19"/></g>,
  ordenar: <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M7 4V28 M3 24 7 28 11 24 M16 7H29 M16 15H25 M16 23H21"/></g>,
  produto: <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M8 4H24L27 9V28H5V9Z M5 9H27 M8 4 10 9 M24 4 22 9"/><rect x="10" y="15" width="12" height="8" rx="2"/></g>,
  salvar: <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M5 4H23L28 9V28H5Z M10 4V12H22V4 M10 28V19H23V28 M18 5V9"/></g>,
  visivel: <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M2 16C9 3 23 3 30 16C23 29 9 29 2 16Z"/><circle cx="16" cy="16" r="5"/></g>,
}

export function MarcaIcone({ nome, tamanho = 24, rotulo, className }) {
  const desenho = DESENHOS[nome]
  if (!Object.hasOwn(DESENHOS, nome)) return null
  const medida = typeof tamanho === 'number' ? `${tamanho}px` : tamanho
  return (
    <svg
      className={className ? `${css.icone} ${className}` : css.icone}
      width={medida}
      height={medida}
      viewBox="0 0 32 32"
      role={rotulo ? 'img' : 'presentation'}
      aria-label={rotulo}
      aria-hidden={rotulo ? undefined : 'true'}
      focusable="false"
    >
      {desenho}
    </svg>
  )
}
