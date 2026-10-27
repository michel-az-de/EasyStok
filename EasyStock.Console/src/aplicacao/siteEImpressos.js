import { corpoDaCriacao, corpoDoLogoNoSite, corpoDoSite } from '../dominio/vitrineDoSite'
import { enviarArquivoDoLogo, obterLojaDaEmpresa, urlDoLogo } from '../infra/api/impressosApi'
import { criarVitrine, obterConfiguracaoDoSite, obterVitrine, salvarConfiguracaoDoSite } from '../infra/api/vitrineApi'

// Site e impressos (#2070): a vitrine da empresa, a configuração que o passo mostra e um logo só
// para os impressos e o site.
export { urlDoLogo }

export async function carregarSite() {
  const vitrine = await obterVitrine()
  if (!vitrine) return { vitrine: null, configuracao: null }
  return { vitrine, configuracao: await obterConfiguracaoDoSite() }
}

export const criarSite = (form) => criarVitrine(corpoDaCriacao(form))

export const salvarSite = (configuracao, form) => salvarConfiguracaoDoSite(corpoDoSite(configuracao, form))

// Um envio: o logo da loja (impressos) e, com a URL que a API devolveu, o logo do site. O envio
// apaga o logo anterior da loja, então o site precisa da URL nova logo em seguida. A configuração
// é lida depois do upload (`lerConfiguracao`): o PUT leva a marca gravada mais recente.
export async function enviarLogoDoSite(lerConfiguracao, arquivo) {
  const loja = await obterLojaDaEmpresa()
  const url = await enviarArquivoDoLogo(loja.id, arquivo)
  if (!url) throw new Error('O EasyStok não devolveu o endereço do logo. Tente de novo.')
  try {
    return await salvarConfiguracaoDoSite(corpoDoLogoNoSite(lerConfiguracao(), url))
  } catch (erro) {
    throw new Error(`O logo já vale para os impressos, mas o site não foi atualizado: ${erro.message} Envie o logo de novo.`, { cause: erro })
  }
}
