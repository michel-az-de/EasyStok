import { corpoDosAlertas } from '../dominio/alertasDeEstoque'
import { lojasAtivas } from '../dominio/dadosDaLoja'
import { atualizarConfiguracaoDaLoja, obterConfiguracaoDaLoja } from '../infra/api/configuracoesDaLojaApi'
import { listarLojasDaEmpresa } from '../infra/api/lojasApi'

// Alertas de estoque (#2070): a configuração da primeira loja ativa (tenant único, uma loja), como
// o logo dos impressos. `totalDeLojas` deixa a tela avisar quando há outras.
export async function carregarAlertasDeEstoque() {
  const lojas = lojasAtivas(await listarLojasDaEmpresa())
  const [loja] = lojas
  if (!loja) throw new Error('Nenhuma loja ativa encontrada para esta empresa.')
  return { loja, totalDeLojas: lojas.length, configuracao: await obterConfiguracaoDaLoja(loja.empresaId, loja.id) }
}

export const salvarAlertasDeEstoque = ({ loja, configuracao }, form) =>
  atualizarConfiguracaoDaLoja(corpoDosAlertas(configuracao, form, { empresaId: loja.empresaId, lojaId: loja.id }))
