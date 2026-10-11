import { erroDaLinha } from '../dominio/producao'
import { corpoDaProducao, etiquetasDaApi, obterEtiquetasDoLote, registrarProducao } from '../infra/api/producaoApi'
import { criarChaveDeEnvio } from './chaveDeEnvio'

// Produção do dia no modo API (M2.2, #1491). A chave de idempotência vale para o mesmo corpo até
// gravar: se a rede cair e ela clicar de novo, o EasyStok devolve o mesmo lote em vez de criar
// outro. Corpo corrigido depois da falha é outra produção e ganha chave nova (#1548): a API recusa
// a mesma chave com outro corpo. Separado do componente para a prova rodar sem React.
export function criarProducaoDoDia({ gerarChave, agora } = {}) {
  const envio = criarChaveDeEnvio(gerarChave, agora)

  return {
    produzir: async (linhas) => {
      const erro = linhas.length === 0 ? 'Adicione ao menos um prato.' : linhas.map(erroDaLinha).find(Boolean)
      if (erro) return { ok: false, erro }
      const chave = envio.para(corpoDaProducao(linhas))
      try {
        const r = await registrarProducao(linhas, chave)
        envio.confirmar(chave)
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
