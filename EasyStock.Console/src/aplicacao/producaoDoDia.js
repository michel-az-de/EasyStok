import { erroDaLinha } from '../dominio/producao'
import { etiquetasDaApi, obterEtiquetasDoLote, registrarProducao } from '../infra/api/producaoApi'

// Produção do dia no modo API (M2.2, #1491). A chave de idempotência nasce na primeira tentativa e
// só é trocada depois de gravar: se a rede cair e ela clicar de novo, o EasyStok devolve o mesmo
// lote em vez de criar outro. Separado do componente para a prova rodar sem React.
export function criarProducaoDoDia({ gerarChave = () => crypto.randomUUID() } = {}) {
  let chave = null

  return {
    produzir: async (linhas) => {
      const erro = linhas.length === 0 ? 'Adicione ao menos um prato.' : linhas.map(erroDaLinha).find(Boolean)
      if (erro) return { ok: false, erro }
      chave ??= gerarChave()
      try {
        const r = await registrarProducao(linhas, chave)
        chave = null
        return {
          ok: true,
          loteId: r.loteId,
          codigo: r.codigoLote,
          etiquetas: r.totalEtiquetas,
          pratos: (r.pratos ?? []).map((p) => ({ sku: p.cardapioItemId, nome: p.nome, porcoes: p.porcoes, sobraG: p.sobraG })),
          // D-M2-01 (#1499): falta de insumo na baixa automática avisa, não trava.
          avisos: r.avisos ?? [],
        }
      } catch (e) {
        return { ok: false, erro: e.message }
      }
    },
    etiquetas: async (loteId) => etiquetasDaApi(await obterEtiquetasDoLote(loteId)),
  }
}
