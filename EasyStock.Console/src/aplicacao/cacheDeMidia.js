// Cache pequeno dos endereços locais (object URL) das mídias da conversa (#1577). Trocar de conversa
// liberava todos os arquivos e voltar baixava tudo de novo; a API não manda validade de cache nessa
// rota, então o navegador também não guardava. Aqui os últimos usados ficam, e o mais antigo sem
// balão na tela é liberado quando passa do limite. Endereço em uso nunca é liberado: o áudio que
// está tocando continua tocando.
const LIMITE_PADRAO = 30

export function criarCacheDeMidia({ limite = LIMITE_PADRAO, revogar = (url) => URL.revokeObjectURL(url) } = {}) {
  // A ordem do Map é a de uso: o primeiro é o usado há mais tempo.
  const itens = new Map()

  const tocar = (id, item) => {
    itens.delete(id)
    itens.set(id, item)
    item.usos += 1
    return item.url
  }
  const expulsar = () => {
    for (const [id, item] of itens) {
      if (itens.size <= limite) return
      if (item.usos > 0) continue
      itens.delete(id)
      revogar(item.url)
    }
  }

  return {
    // Balão que aparece: devolve o endereço guardado e passa a segurá-lo, ou null se ainda não baixou.
    usar(id) {
      const item = itens.get(id)
      return item ? tocar(id, item) : null
    },
    // Download que terminou; quem guarda já segura. Se outro balão guardou antes, vale o dele.
    guardar(id, url) {
      const antes = itens.get(id)
      if (antes) {
        if (antes.url !== url) revogar(url)
        return tocar(id, antes)
      }
      return tocar(id, { url, usos: 0 })
    },
    // Balão que sai da tela.
    soltar(id) {
      const item = itens.get(id)
      if (!item) return
      item.usos = Math.max(item.usos - 1, 0)
      expulsar()
    },
    // Fim da sessão: nada de uma conta fica na memória para a próxima.
    limpar() {
      for (const item of itens.values()) revogar(item.url)
      itens.clear()
    },
    tamanho: () => itens.size,
  }
}
