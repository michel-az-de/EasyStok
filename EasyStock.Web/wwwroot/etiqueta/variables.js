// Canonical variable allowlist — mirrors backend LayoutJsonValidator
const ALLOWLIST = new Set([
  'produto.nome', 'produto.marca',
  'produto.ficha.kcal', 'produto.ficha.proteina_g', 'produto.ficha.carbs_g',
  'produto.ficha.gordura_g', 'produto.ficha.gordura_saturada_g',
  'produto.ficha.fibras_g', 'produto.ficha.sodio_mg', 'produto.ficha.porcao_g',
  'etiqueta.codigo', 'etiqueta.sequencial',
  'lote.codigo', 'lote.validadeEm', 'lote.criadoEm',
  'empresa.nome',
]);

const VAR_RE = /\{([a-z]+(?:\.[a-z_]+)*)(?::([^}]+))?\}/g;

/**
 * Resolve all variables in a content string against the dados context.
 * Unknown variables → em-dash. Null/undefined values → em-dash.
 * @param {string} content - Template string with {variable} placeholders
 * @param {Object} dados - { produto, etiqueta, lote, empresa }
 * @returns {string}
 */
export function resolveVariables(content, dados) {
  if (!content) return '';
  return content.replace(VAR_RE, (_, path, fmt) => {
    const val = getPath(dados, path, fmt);
    return val !== null && val !== undefined && val !== '' ? String(val) : '—';
  });
}

function getPath(dados, path, fmt) {
  switch (path) {
    case 'produto.nome':              return dados?.produto?.Nome ?? null;
    case 'produto.marca':             return dados?.produto?.Marca ?? null;
    case 'produto.ficha.kcal':        return dados?.produto?.FichaKcal ?? null;
    case 'produto.ficha.proteina_g':  return dados?.produto?.FichaProteinaG ?? null;
    case 'produto.ficha.carbs_g':     return dados?.produto?.FichaCarbsG ?? null;
    case 'produto.ficha.gordura_g':   return dados?.produto?.FichaGorduraG ?? null;
    case 'produto.ficha.gordura_saturada_g': return dados?.produto?.FichaGorduraSaturadaG ?? null;
    case 'produto.ficha.fibras_g':    return dados?.produto?.FichaFibrasG ?? null;
    case 'produto.ficha.sodio_mg':    return dados?.produto?.FichaSodioMg ?? null;
    case 'produto.ficha.porcao_g':    return dados?.produto?.FichaPorcaoG ?? null;
    case 'etiqueta.codigo':           return dados?.etiqueta?.Codigo ?? null;
    case 'etiqueta.sequencial':       return dados?.etiqueta?.Sequencial ?? null;
    case 'lote.codigo':               return dados?.lote?.Codigo ?? null;
    case 'lote.validadeEm':           return formatDate(dados?.lote?.ValidadeEm, fmt || 'dd/MM/yyyy');
    case 'lote.criadoEm':             return formatDate(dados?.lote?.CriadoEm, fmt || 'dd/MM HH:mm');
    case 'empresa.nome':              return dados?.empresa?.Nome ?? null;
    default:                          return null;
  }
}

function formatDate(val, fmt) {
  if (!val) return null;
  const d = new Date(val);
  if (isNaN(d)) return null;
  const pad = (n) => String(n).padStart(2, '0');
  // America/Sao_Paulo offset: -3 (no DST since 2019)
  const brt = new Date(d.getTime() - 3 * 60 * 60 * 1000);
  return fmt
    .replace('yyyy', brt.getUTCFullYear())
    .replace('MM',   pad(brt.getUTCMonth() + 1))
    .replace('dd',   pad(brt.getUTCDate()))
    .replace('HH',   pad(brt.getUTCHours()))
    .replace('mm',   pad(brt.getUTCMinutes()));
}

export { ALLOWLIST };

// Global expose for Razor ViewComponent (non-module script tags)
if (typeof window !== 'undefined') window.resolveVariables = resolveVariables;
