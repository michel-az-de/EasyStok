// ──────────────────────────────────────────────────────────────────────────
// Fonte UNICA das rampas de cor compartilhadas entre os apps (EasyStock.Web e
// EasyStock.Admin). Mata a 2a fonte de cor (E2): antes navy/orange estavam
// hardcoded e duplicados nos dois tailwind.config.js, podendo dessincronizar.
//
// Os valores espelham as rampas do tokens.css (design tokens canonicos).
// Cada config IMPORTA daqui e seleciona as shades que ja emitia — preservando
// o tailwind.dist.css atual (o safelist de cada app forca emissao das shades
// presentes, entao adicionar shades novas mudaria o output; por isso o pick).
// ──────────────────────────────────────────────────────────────────────────

const navy = {
  950: '#04102B', 900: '#06143A', 800: '#0A1F52', 700: '#0E2A6E',
  600: '#15388A', 500: '#1E48A8', 400: '#3B5BB8', 300: '#7B91D0',
  200: '#BCC8E5', 100: '#DDE4F2', 50: '#EDF1F9',
};

const orange = {
  950: '#4A1A03', 900: '#7A2A05', 800: '#9F3706', 700: '#BF4307',
  600: '#E85814', 500: '#F26B25', 400: '#F8884A', 300: '#FBA978',
  200: '#FAC8AB', 100: '#FCDCC8', 50: '#FEEFE3',
};

module.exports = { navy, orange };
