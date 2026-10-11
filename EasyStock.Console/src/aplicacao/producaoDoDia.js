import { erroDaLinha } from '../dominio/producao'
import { corpoDaProducao, etiquetasDaApi, obterEtiquetasDoLote, registrarProducao } from '../infra/api/producaoApi'
import { criarChaveDeEnvio } from './chaveDeEnvio'

// #1585: a tela desmonta a cada troca de rota. O que ela digitou e os lotes lançados na visita
// ficam em memória de módulo, como as porções planejadas de planejamento.js; recarregar a página zera.
let rascunho = null
let lancados = []
const ouvintes = new Set()

export const guardarRascunho = (linhas) => { rascunho = linhas }

// As linhas com que a tela abre, ou null quando não há nada guardado. Vindo do Planejamento
// ("Revisar na produção"), o prato planejado entra com as porções do plano e o rascunho de outro
// prato continua; linha sem prato sai.
export function linhasDoRascunho(planejadas) {
  if (!planejadas?.length) return rascunho?.length ? rascunho : null
  const doPlano = new Set(planejadas.map((p) => p.sku))
  return [...(rascunho ?? []).filter((l) => l.sku && !doPlano.has(l.sku)), ...planejadas]
}

// Os lotes lançados na visita, o mais novo primeiro. Cada um segue na lista até ser dispensado.
export const lotesLancados = () => lancados
export const dispensarLote = (loteId) => {
  lancados = lancados.filter((l) => l.loteId !== loteId)
  return lancados
}

// Avisa quando um lote grava. O envio pode terminar depois de ela sair da tela e voltar: quem está
// na tela nessa hora mostra o lote e tira as linhas já lançadas.
export const aoLancarLote = (ouvinte) => {
  ouvintes.add(ouvinte)
  return () => ouvintes.delete(ouvinte)
}

// O erro diz de qual prato é, com o número que a tela mostra no cartão ("Prato 02 (Ravióli): ...").
function erroDasLinhas(linhas) {
  if (linhas.length === 0) return 'Adicione ao menos um prato.'
  const indice = linhas.findIndex((l) => erroDaLinha(l))
  if (indice < 0) return null
  const { nome } = linhas[indice]
  return `Prato ${String(indice + 1).padStart(2, '0')}${nome ? ` (${nome})` : ''}: ${erroDaLinha(linhas[indice])}`
}

// Produção do dia no modo API (M2.2, #1491). A chave de idempotência vale para o mesmo corpo até
// gravar: se a rede cair e ela clicar de novo, o EasyStok devolve o mesmo lote em vez de criar
// outro. Corpo corrigido depois da falha é outra produção e ganha chave nova (#1548): a API recusa
// a mesma chave com outro corpo. Separado do componente para a prova rodar sem React.
export function criarProducaoDoDia({ gerarChave, agora } = {}) {
  const envio = criarChaveDeEnvio(gerarChave, agora)

  return {
    produzir: async (linhas) => {
      const erro = erroDasLinhas(linhas)
      if (erro) return { ok: false, erro }
      const chave = envio.para(corpoDaProducao(linhas))
      let lote
      try {
        const r = await registrarProducao(linhas, chave)
        envio.confirmar(chave)
        lote = {
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
      // #1585: gravou, o rascunho sai e o lote entra na lista, mesmo com a tela já desmontada.
      rascunho = null
      lancados = [lote, ...lancados.filter((l) => l.loteId !== lote.loteId)]
      ouvintes.forEach((ouvinte) => ouvinte(lote))
      return lote
    },
    etiquetas: async (loteId) => etiquetasDaApi(await obterEtiquetasDoLote(loteId)),
  }
}

// A produção que a tela usa. Uma só: a chave do envio que falhou continua valendo para o mesmo
// corpo quando ela sai da tela e volta com o rascunho (#1585).
export const producaoDoDia = criarProducaoDoDia()
