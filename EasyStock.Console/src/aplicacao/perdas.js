import { estornarSaida, lancarPerda, obterPerdas, obterVencidos, resumoDePerdasDaApi, vencidoDaApi } from '../infra/api/producaoApi'

// Perdas no modo API (M2.6, #1511). D-M2-02: o motivo vem da lista e "Outro" pede texto; acima de
// R$ 50 o EasyStok devolve 403 e a tela avisa que é do Gerente (D-M2-06). Desfazer é o estorno de
// saída (Gerente). Separado do hook para a prova rodar sem React.
export const MOTIVOS_PERDA = [
  { id: 'Vencido', rotulo: 'Vencido' },
  { id: 'PerdaNoPreparo', rotulo: 'Perda no preparo' },
  { id: 'Doacao', rotulo: 'Doação' },
  { id: 'Degustacao', rotulo: 'Degustação' },
  { id: 'Outro', rotulo: 'Outro' },
]

export const lerResumoDePerdas = async (de, ate) => resumoDePerdasDaApi(await obterPerdas(de, ate))
export const lerVencidos = async () => ((await obterVencidos()) ?? []).map(vencidoDaApi)

const numero = (texto) => (String(texto ?? '').trim() === '' ? NaN : Number(String(texto).replace(',', '.')))

export function erroDaPerda({ produtoId, quantidade, motivo, texto }) {
  if (!produtoId) return 'Escolha o prato ou o insumo perdido.'
  if (!(numero(quantidade) > 0)) return 'Quantidade perdida maior que zero.'
  if (!MOTIVOS_PERDA.some((m) => m.id === motivo)) return 'Escolha o motivo da perda.'
  if (motivo === 'Outro' && String(texto ?? '').trim().length < 3) return 'Em "Outro", escreva o motivo da perda.'
  return null
}

export function criarGestaoPerdas({ recarregar, aoErro, aoFeito = () => {} }) {
  const gravar = async (chamada, sucesso) => {
    try {
      const r = await chamada()
      aoFeito(sucesso(r))
      return true
    } catch (e) {
      aoErro(e.status === 403 ? (e.codigo === 'PERDA_EXIGE_GERENTE' ? e.message : 'Isso é do Gerente.') : e.message)
      return false
    } finally {
      await recarregar()
    }
  }

  return {
    lancar: async (rascunho) => {
      const erro = erroDaPerda(rascunho)
      if (erro) { aoErro(erro); return false }
      return gravar(() => lancarPerda({
        produtoId: rascunho.produtoId,
        itemEstoqueId: rascunho.itemEstoqueId ?? null,
        quantidade: numero(rascunho.quantidade),
        motivo: rascunho.motivo,
        texto: rascunho.motivo === 'Outro' ? String(rascunho.texto).trim() : null,
      }), (r) => `Perda lançada: ${r?.quantidade ?? rascunho.quantidade} de ${rascunho.nome ?? 'item'}.`)
    },
    // Sugestão de lote vencido: lança o lote inteiro como Vencido, só quando ela confirma.
    lancarVencido: (v) => gravar(
      () => lancarPerda({ produtoId: v.produtoId, itemEstoqueId: v.itemEstoqueId, quantidade: v.quantidade, motivo: 'Vencido' }),
      () => `Lote ${v.lote ?? ''} lançado como vencido.`),
    desfazer: (lancamento) => gravar(
      () => estornarSaida(lancamento.id, 'Perda desfeita pelo console'),
      () => `Perda de ${lancamento.nome} desfeita.`),
  }
}
