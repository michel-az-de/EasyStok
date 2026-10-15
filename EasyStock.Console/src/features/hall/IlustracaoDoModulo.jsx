// #1839 pacote D: uma ilustração por módulo no cartão do hall, no traço da marca Casa da Baba
// (contorno em tinta, trigo dourado, fundo creme). As cores vêm de variáveis do tema, então o
// mesmo desenho serve no claro e no escuro. Decorativa: o nome do módulo já está no cartão.
const traco = { fill: 'none', stroke: 'var(--il-tinta)', strokeWidth: 2, strokeLinecap: 'round', strokeLinejoin: 'round' }
const papel = { ...traco, fill: 'var(--il-papel)' }
const ouro = { ...traco, fill: 'var(--il-ouro)' }
const marca = { ...traco, fill: 'var(--il-marca)' }

function Trigo({ x, y, espelhar = false }) {
  return <g transform={`translate(${x} ${y}) scale(${espelhar ? -1 : 1} 1)`} fill="var(--il-ouro)">
    <path d="M0 0C1-8 2-16 4-24" fill="none" stroke="var(--il-ouro)" strokeWidth="1.6" strokeLinecap="round" />
    <ellipse cx="4.5" cy="-26" rx="1.8" ry="3.4" />
    <ellipse cx="1" cy="-20" rx="1.6" ry="3.2" transform="rotate(-35 1 -20)" />
    <ellipse cx="6" cy="-19" rx="1.6" ry="3.2" transform="rotate(35 6 -19)" />
    <ellipse cx="0" cy="-13" rx="1.6" ry="3.2" transform="rotate(-35 0 -13)" />
    <ellipse cx="5" cy="-12" rx="1.6" ry="3.2" transform="rotate(35 5 -12)" />
  </g>
}

const DESENHOS = {
  cardapio: <>
    <Trigo x={16} y={68} />
    <rect x="32" y="12" width="38" height="52" rx="3" {...papel} />
    <path d="M62 12V26L65 23 68 26V12" {...ouro} />
    <circle cx="48" cy="28" r="7" {...ouro} />
    <path d="M39 44H62 M39 50H57 M39 56H52" {...traco} />
    <path d="M76 66H102V52L76 58Z" {...ouro} />
    <path d="M76 62H102" {...traco} />
    <circle cx="94" cy="50" r="3" {...marca} />
  </>,
  producao: <>
    <Trigo x={14} y={68} />
    <path d="M30 46H74C74 60 64 66 52 66 40 66 30 60 30 46Z" {...papel} />
    <path d="M36 46C40 38 48 36 52 38 58 34 66 38 68 46" {...ouro} />
    <path d="M28 46H76 M60 40 72 22" {...traco} />
    <ellipse cx="76" cy="15" rx="4" ry="8" transform="rotate(35 76 15)" {...traco} />
    <g transform="rotate(-10 94 61)">
      <rect x="82" y="57" width="24" height="8" rx="4" {...ouro} />
      <path d="M82 61H76 M106 61H112" {...traco} />
    </g>
    <circle cx="86" cy="42" r="1.4" fill="var(--il-ouro)" />
    <circle cx="92" cy="47" r="1.4" fill="var(--il-ouro)" />
  </>,
  atendimento: <>
    <Trigo x={110} y={68} espelhar />
    <path d="M22 14H66A6 6 0 0 1 72 20V40A6 6 0 0 1 66 46H40L30 54V46H22A6 6 0 0 1 16 40V20A6 6 0 0 1 22 14Z" {...papel} />
    <path d="M26 24H60 M26 31H52 M26 38H46" {...traco} />
    <path d="M72 30H96A8 8 0 0 1 104 38V46A8 8 0 0 1 96 54H94L98 62 86 54H72A8 8 0 0 1 64 46V38A8 8 0 0 1 72 30Z" {...ouro} />
    <path d="M84 49C78 45 78 38 81.5 38 83 38 84 39.5 84 40.5 84 39.5 85 38 86.5 38 90 38 90 45 84 49Z" {...marca} strokeWidth="1.5" />
  </>,
  cozinha: <>
    <Trigo x={104} y={68} espelhar />
    <path d="M46 16C42 12 50 8 46 3 M58 14C54 10 62 6 58 1 M70 16C66 12 74 8 70 3" {...traco} stroke="var(--il-marca)" />
    <path d="M34 40H26V46H34 M82 40H90V46H82" {...traco} />
    <rect x="34" y="34" width="48" height="24" rx="4" {...papel} />
    <path d="M36 34C40 24 76 24 80 34Z" {...ouro} />
    <path d="M30 34H86" {...traco} />
    <circle cx="58" cy="23" r="3" {...ouro} />
    <path d="M47 68C44 66 46 62 49 60 50 63 53 64 51 68Z M57 68C54 66 56 62 59 60 60 63 63 64 61 68Z M67 68C64 66 66 62 69 60 70 63 73 64 71 68Z" {...marca} strokeWidth="1.5" />
  </>,
  financeiro: <>
    <Trigo x={14} y={68} />
    <path d="M64 22H74V34L72 33 70 34 68 33 66 34 64 33Z" {...papel} />
    <rect x="34" y="22" width="26" height="12" rx="2" {...ouro} />
    <path d="M47 34V40 M39 28H50" {...traco} />
    <path d="M28 40H76L80 66H24Z" {...papel} />
    <path d="M26 59H78" {...traco} />
    {[36, 44, 52, 60].flatMap((x) => [48, 54].map((y) => <circle key={`${x}-${y}`} cx={x} cy={y} r="1.7" fill="var(--il-tinta)" />))}
    <rect x="84" y="48" width="20" height="16" fill="var(--il-ouro)" />
    <path d="M84 48V64 M104 48V64 M84 64A10 3.5 0 0 0 104 64 M84 53.3A10 3.5 0 0 0 104 53.3 M84 58.6A10 3.5 0 0 0 104 58.6" {...traco} />
    <ellipse cx="94" cy="48" rx="10" ry="3.5" {...ouro} />
  </>,
  campanhas: <>
    <Trigo x={108} y={70} espelhar />
    <g transform="rotate(-8 60 42)">
      <path d="M26 26H94V36A6 6 0 0 0 94 48V58H26V48A6 6 0 0 0 26 36Z" {...papel} />
      <path d="M74 29V55" {...traco} strokeDasharray="3 4" />
      <circle cx="40" cy="36" r="3" {...traco} />
      <circle cx="56" cy="50" r="3" {...traco} />
      <path d="M56 33 40 53" {...traco} />
      <path d="M84 35 85.76 39.57 90.66 39.84 86.85 42.93 88.11 47.66 84 45 79.89 47.66 81.15 42.93 77.34 39.84 82.24 39.57Z" {...ouro} strokeWidth="1.5" />
    </g>
    <path d="M102 6 104 12 110 14 104 16 102 22 100 16 94 14 100 12Z" fill="var(--il-marca)" />
    <path d="M18 14 19.2 17.8 23 19 19.2 20.2 18 24 16.8 20.2 13 19 16.8 17.8Z" fill="var(--il-marca)" />
  </>,
  configuracoes: <>
    <Trigo x={112} y={68} espelhar />
    <rect x="18" y="18" width="56" height="48" rx="5" {...papel} />
    <path d="M18 34H74" {...traco} />
    <circle cx="30" cy="26" r="4" {...traco} /><path d="M30 26 32 23" {...traco} />
    <circle cx="42" cy="26" r="4" {...traco} /><path d="M42 26 39 24" {...traco} />
    <circle cx="64" cy="26" r="2.4" fill="var(--il-marca)" />
    <rect x="26" y="40" width="40" height="18" rx="3" {...ouro} />
    <path d="M32 46H44" {...traco} stroke="var(--il-papel)" />
    <path d="M103 42H107 M100.36 48.36 103.19 51.19 M94 51V55 M87.64 48.36 84.81 51.19 M85 42H81 M87.64 35.64 84.81 32.81 M94 33V29 M100.36 35.64 103.19 32.81" {...traco} strokeWidth="4.5" />
    <circle cx="94" cy="42" r="9" {...ouro} />
    <circle cx="94" cy="42" r="3.5" {...papel} />
  </>,
  entregas: <>
    <Trigo x={110} y={68} espelhar />
    <path d="M4 38H14 M7 46H17 M5 54H13" {...traco} stroke="var(--il-marca)" />
    <path d="M20 48H50 M37 60H79 M80 60 72 30 M67 30H79" {...traco} />
    <path d="M42 56C42 48 52 45 64 47L68 56Z" {...marca} />
    <path d="M46 44H60" {...traco} strokeWidth="3" />
    <rect x="22" y="28" width="26" height="20" rx="2" {...ouro} />
    <path d="M35 28V48 M22 37H48" {...traco} />
    <path d="M35 28C30 21 26 26 35 28 44 26 40 21 35 28Z" {...papel} />
    <circle cx="76" cy="38" r="2.2" fill="var(--il-ouro)" />
    <circle cx="30" cy="60" r="7" {...papel} /><circle cx="30" cy="60" r="2" fill="var(--il-tinta)" />
    <circle cx="86" cy="60" r="7" {...papel} /><circle cx="86" cy="60" r="2" fill="var(--il-tinta)" />
  </>,
}

export function IlustracaoDoModulo({ id, className }) {
  const desenho = DESENHOS[id]
  if (!desenho) return null
  return <svg className={className} viewBox="0 0 120 80" aria-hidden="true" focusable="false" data-ilustracao={id}>
    <path d="M20 40C18 20 40 8 62 10 88 12 106 22 104 44 102 64 80 72 58 70 34 68 22 58 20 40Z" fill="var(--il-fundo)" />
    <path d="M10 68H110" stroke="var(--il-chao)" strokeWidth="2" strokeLinecap="round" />
    {desenho}
  </svg>
}
