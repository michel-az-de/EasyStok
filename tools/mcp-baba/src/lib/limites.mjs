export const MAX_BYTES = 60_000;

export function limitar(texto, max = MAX_BYTES) {
  const buf = Buffer.from(texto, 'utf8');
  if (buf.length <= max) return texto;
  // mantem o fim: o que importa em log e o mais recente
  return `[... ${buf.length - max} bytes omitidos no inicio ...]\n` + buf.subarray(buf.length - max).toString('utf8');
}
