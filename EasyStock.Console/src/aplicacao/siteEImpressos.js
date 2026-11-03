import { lojasAtivas } from '../dominio/dadosDaLoja'
import { corpoDaCriacao, corpoDoLogoNoSite, corpoDoSite } from '../dominio/vitrineDoSite'
import { enviarArquivoDoLogo, urlDoLogo } from '../infra/api/impressosApi'
import { listarLojasDaEmpresa } from '../infra/api/lojasApi'
import { criarVitrine, obterConfiguracaoDoSite, obterVitrine, salvarConfiguracaoDoSite } from '../infra/api/vitrineApi'

// Site e impressos (#2070): a vitrine da empresa, a configuração que o passo mostra e um logo só
// para os impressos e o site.
export { urlDoLogo }

// A loja que recebe o logo é a primeira ativa (tenant único, uma loja), a mesma da aba Impressão.
// O logo dela vem como a API gravou: é o que os impressos usam e o que o site guarda.
export async function carregarSite() {
  const vitrine = await obterVitrine()
  if (!vitrine) return { vitrine: null, configuracao: null, loja: null, totalDeLojas: 0 }
  const [configuracao, lojas] = await Promise.all([obterConfiguracaoDoSite(), listarLojasDaEmpresa().then(lojasAtivas)])
  return { vitrine, configuracao, loja: lojas[0] ?? null, totalDeLojas: lojas.length }
}

export const criarSite = (form) => criarVitrine(corpoDaCriacao(form))

export const salvarSite = (configuracao, form) => salvarConfiguracaoDoSite(corpoDoSite(configuracao, form))

// Um envio: o logo da loja (impressos) e, com a URL que a API devolveu, o logo do site. O envio
// apaga o logo anterior da loja, então o site precisa da URL nova logo em seguida. A configuração
// é lida depois do upload (`lerConfiguracao`): o PUT leva a marca gravada mais recente.
export async function enviarLogoDoSite(loja, lerConfiguracao, arquivo) {
  if (!loja) throw new Error('Nenhuma loja ativa encontrada para esta empresa.')
  const url = await enviarArquivoDoLogo(loja.id, arquivo)
  if (!url) throw new Error('O EasyStok não devolveu o endereço do logo. Tente de novo.')
  try {
    return { configuracao: await salvarConfiguracaoDoSite(corpoDoLogoNoSite(lerConfiguracao(), url)), logoDaLoja: url }
  } catch (erro) {
    throw new Error(`O logo já vale para os impressos, mas o site não foi atualizado: ${erro.message} Envie o logo de novo.`, { cause: erro })
  }
}

// O logo trocado só na loja (aba Impressão) passa a valer também no site, sem subir arquivo.
export const usarLogoDaLojaNoSite = (logoDaLoja, lerConfiguracao) =>
  salvarConfiguracaoDoSite(corpoDoLogoNoSite(lerConfiguracao(), logoDaLoja))
