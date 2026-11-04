// Impressão automática do canhoto (#2096): o PC da cozinha imprime sozinho cada pedido que entra
// na fila de impressão da API (S20). Aqui só as regras puras; o laço mora em
// `aplicacao/useImpressaoAutomatica.js` e o papel em `componentes/htmlNoNavegador.js`.

// Uma aba só imprime: as outras esperam este nome na Locks API.
export const NOME_DA_TRAVA = 'easystok-impressao-automatica'
export const INTERVALO_DA_FILA_MS = 10000
// Depois de mandar uma folha, espera antes da próxima: o Chrome em --kiosk-printing devolve na hora.
export const ESPERA_ENTRE_FOLHAS_MS = 3000

// O que sai agora: pendentes ainda não tentadas nesta visita, criadas depois de ligar a impressão
// automática, das mais antigas para as mais novas.
export function pendentesParaImprimir(lista, { tentadas = new Set(), desde = null } = {}) {
  const limite = desde ? Date.parse(desde) : Number.NaN
  return (Array.isArray(lista) ? lista : [])
    .filter((item) => item?.id && item.pedidoId && (item.status ?? 'pendente') === 'pendente' && !tentadas.has(item.id))
    .filter((item) => !Number.isFinite(limite) || !(Date.parse(item.criadaEm) < limite))
    .sort((a, b) => (Date.parse(a.criadaEm) || 0) - (Date.parse(b.criadaEm) || 0))
}

// Contagem do dia neste aparelho: troca de dia zera.
export const contagemDoDia = (contagem, hoje) => (contagem?.dia === hoje && Number.isInteger(contagem.total) && contagem.total > 0
  ? { dia: hoje, total: contagem.total } : { dia: hoje, total: 0 })
export const somarImpressa = (contagem, hoje) => {
  const atual = contagemDoDia(contagem, hoje)
  return { dia: hoje, total: atual.total + 1 }
}

export const textoDaContagem = (total) => (total === 1 ? '1 canhoto hoje' : `${total} canhotos hoje`)

// Atalho do Windows que abre o console sem o diálogo de impressão.
export const CAMINHO_DO_CHROME = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'
export const atalhoDoChrome = (endereco) => `"${CAMINHO_DO_CHROME}" --kiosk-printing --app=${endereco}`
