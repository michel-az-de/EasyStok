// De onde vêm as conversas (ADR-0054, F01). `VITE_FONTE_DADOS=api` liga a API real
// do EasyStok; qualquer outro valor mantém a massa simulada de demonstração.
// `VITE_API_BASE` vazio usa a mesma origem (produção atrás do Caddy e o proxy do dev).
export const FONTE_API = import.meta.env.VITE_FONTE_DADOS === 'api'
export const API_BASE = import.meta.env.VITE_API_BASE ?? ''
