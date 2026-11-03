import { chamarApi } from './cliente'

// Vitrine do site da empresa logada (P01-B): `api/minha-vitrine` (policy Admin, empresa do token).
const BASE = '/api/minha-vitrine'

// Sem vitrine a API responde 404; para a tela isso é "ainda não existe", não erro.
export async function obterVitrine() {
  try { return await chamarApi(BASE) }
  catch (erro) {
    if (erro.status === 404) return null
    throw erro
  }
}

export const criarVitrine = (corpo) => chamarApi(BASE, { metodo: 'POST', corpo })
export const obterConfiguracaoDoSite = () => chamarApi(`${BASE}/configuracao`)
// A resposta é a configuração inteira, já gravada.
export const salvarConfiguracaoDoSite = (corpo) => chamarApi(`${BASE}/configuracao`, { metodo: 'PUT', corpo })
