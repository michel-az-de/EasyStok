import css from './Icone.module.css'
import { Icone } from './Icone'

// Casa da Baba V4 / Essencial (01/10/2026), geometria própria de icones.cjs.
// Origem: marca/sistema-visual-pranchas-v4 (repositório da marca). Os SVGs de referência saíram
// do console em #1891 (código morto: nada os importava); este arquivo é a cópia viva.
// Mesmos nós e atributos, convertidos apenas para a grafia JSX; nenhuma entrada HTML.
const DESENHOS = {
  'producao': <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M6 11H26V23C26 28 6 28 6 23Z M3 11H29 M6 15H3V20H6 M26 15H29V20H26 M9 7C11 5 8 4 10 2 M16 7C18 5 15 4 17 2 M23 7C25 5 22 4 24 2"/></g>,
  'pedido-em-preparo': <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M8 28V4H24V14 M8 28L11 26 14 28 17 26"/><path d="M11 8H20 M11 12H16 M18 22H29V27H18Z M17 22H30 M21 19V17 M26 19V17"/></g>,
  'estoque': <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M4 4V28 M28 4V28 M4 16H28 M4 28H28"/><rect x="7" y="5" width="8" height="11" rx="1.5"/><rect x="18" y="8" width="7" height="8" rx="1.5"/><rect x="8" y="20" width="16" height="8" rx="1.5"/><path d="M11 5V9 M16 20V24"/></g>,
  'ingredientes': <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M16 29V5 M16 20C8 20 5 15 6 10C12 10 16 15 16 20 M16 13C23 13 27 8 26 4C19 4 16 8 16 13 M16 27C24 27 28 23 27 18C20 18 16 22 16 27"/></g>,
  'receita': <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M5 5C9 3 13 4 16 7C20 4 25 3 28 5V27C24 25 20 26 16 28C12 25 8 25 5 27Z M16 7V28 M8 10H12 M8 14H12 M20 10H25 M20 14H25 M8 19H12 M20 19H24"/></g>,
  'horarios': <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><circle cx="16" cy="16" r="12"/><path d="M16 8V16L22 19 M16 4V5 M28 16H27 M16 28V27 M4 16H5"/></g>,
  'financeiro': <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><circle cx="16" cy="16" r="11"/><path d="M19 11H14C10 11 10 16 14 16H18C22 16 22 21 17 21H12 M16 8V24"/></g>,
  'caixa': <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M7 5H25L27 20H5Z M3 20H29V28H3Z M8 24H12"/><rect x="10" y="8" width="12" height="5" rx="1.5"/><path d="M11 17H12 M16 17H17 M21 17H22"/></g>,
  'cupom': <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M4 7H28V12C22 12 22 20 28 20V25H4V20C10 20 10 12 4 12Z M18 8V10 M18 14V17 M18 21V24"/></g>,
  'mensagem': <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M4 5H28V22H14L6 29V22H4Z M9 11H23 M9 16H19"/></g>,
  'entrega': <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M3 7H20V23H3Z M20 12H25L29 18V23H20 M20 18H29"/><circle cx="8" cy="24" r="3"/><circle cx="24" cy="24" r="3"/><path d="M6 11H14 M6 15H11"/></g>,
  'link': <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M12 20 20 12 M10 17C4 22 4 28 9 29C12 30 15 27 18 24 M22 15C28 10 28 4 23 3C20 2 17 5 14 8"/></g>,
  'atencao': <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M16 3 30 28H2Z M16 11V19"/><circle cx="16" cy="24" r="0.7"/></g>,
  'anterior': <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M21 5 10 16 21 27"/></g>,
  'proximo': <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M11 5 22 16 11 27"/></g>,
  'bloqueado': <g fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><rect x="5" y="14" width="22" height="15" rx="2"/><path d="M10 14V8C10-1 22-1 22 8V14 M16 20V24"/></g>,
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
  if (nome === 'configuracoes') return <Icone nome="settings" tamanho={tamanho} rotulo={rotulo} className={className} />
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
