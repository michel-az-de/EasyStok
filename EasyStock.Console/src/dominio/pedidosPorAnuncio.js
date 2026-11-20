// #2928 (S5): pedidos por anúncio. O período é o dia de Brasília (data pura); o servidor inclui o último dia.
export const caminhoPedidosPorAnuncio = ({ inicio, fim }) =>
  `/api/relatorios/pedidos-por-anuncio?${new URLSearchParams({ de: inicio, ate: fim })}`

const NOMES = { google: 'Google', meta: 'Meta', outra: 'Outra', sem_origem: 'Sem anúncio' }
export const nomeDaPlataforma = (plataforma) => NOMES[plataforma] ?? plataforma ?? 'Sem anúncio'
