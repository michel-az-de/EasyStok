// Site e impressos (#2070): a vitrine da loja em `api/minha-vitrine`.
//
// O PUT de `configuracao` trata nulo como "não muda", mas o grupo de marca (subtítulo, logo, cor,
// WhatsApp de pedidos e mensagem de fora da área) é gravado inteiro quando qualquer um deles vem
// não nulo (`Storefront.AtualizarBranding`): o que vier nulo é apagado, e vazio limpa. Se os cinco
// vierem nulos, a API pula a marca e responde 200 sem mudar nada. Por isso o corpo leva sempre os
// cinco, o subtítulo vai sempre como texto (vazio quando não há) para o grupo nunca chegar todo
// nulo, e o que a tela não mostra (logo e mensagem de fora da área) vai exatamente como veio.
import { LIMITE_MOEDA_CENTAVOS, mascaraTelefone } from './formato.js'

export const LIMITES_DO_SITE = { tituloPublico: 120, slug: 40, subtituloPublico: 240, corPrimaria: 7, whatsappPedidos: 20 }
// A tela não escreve cor no código (verificar-contraste): o exemplo mora aqui.
export const EXEMPLO_DE_COR = '#8B1E3F'
const ENDERECO_VALIDO = /^[a-z0-9]([a-z0-9]|-(?!-)){1,38}[a-z0-9]$/
const COR_VALIDA = /^#([\da-f]{3}|[\da-f]{6})$/i
export const TIPOS_DE_LOGO = ['image/png', 'image/jpeg']
export const TAMANHO_MAXIMO_DO_LOGO = 5 * 1024 * 1024

// O endereço é a parte final do link do site e não muda depois de criado.
export const sugerirEnderecoDoSite = (titulo) => String(titulo ?? '')
  .normalize('NFD').replace(/[̀-ͯ]/g, '')
  .toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '')
  .slice(0, 40).replace(/-+$/, '')

export function errosDaCriacao({ tituloPublico, slug }) {
  const erros = {}
  const titulo = String(tituloPublico ?? '').trim()
  if (!titulo) erros.tituloPublico = 'Informe o nome do site.'
  else if (titulo.length > LIMITES_DO_SITE.tituloPublico) erros.tituloPublico = `Use até ${LIMITES_DO_SITE.tituloPublico} caracteres.`
  if (!ENDERECO_VALIDO.test(String(slug ?? '').trim())) erros.slug = 'Use de 3 a 40 letras minúsculas, números ou hífen.'
  return erros
}

export const corpoDaCriacao = ({ tituloPublico, slug }) => ({
  tituloPublico: String(tituloPublico ?? '').trim(),
  slug: String(slug ?? '').trim().toLowerCase(),
})

const centavosDe = (reais) => (reais === null || reais === undefined ? null : Math.round(Number(reais) * 100))

export const formularioDoSite = (config) => ({
  subtituloPublico: config?.subtituloPublico ?? '',
  corPrimaria: config?.corPrimaria ?? '',
  whatsappPedidos: config?.whatsappPedidos ?? '',
  pedidoMinimoCentavos: centavosDe(config?.pedidoMinimoEntrega ?? 0),
  freteGratisCentavos: centavosDe(config?.freteGratisAcima),
})

const CAMPOS_DO_FORMULARIO = ['subtituloPublico', 'corPrimaria', 'whatsappPedidos', 'pedidoMinimoCentavos', 'freteGratisCentavos']
const mudou = (form, config, campo) => (form?.[campo] ?? null) !== (formularioDoSite(config)[campo] ?? null)
export const siteMudou = (form, config) => CAMPOS_DO_FORMULARIO.some((campo) => mudou(form, config, campo))

const digitosDoWhatsApp = (valor) => String(valor ?? '').replace(/\D/g, '').replace(/^55(?=\d{10,11}$)/, '')

// Só o que ela mudou é conferido: um valor antigo fora do padrão não trava o resto.
export function errosDoSite(form, config) {
  const erros = {}
  const texto = (campo) => String(form?.[campo] ?? '').trim()
  if (mudou(form, config, 'subtituloPublico') && texto('subtituloPublico').length > LIMITES_DO_SITE.subtituloPublico) {
    erros.subtituloPublico = `Use até ${LIMITES_DO_SITE.subtituloPublico} caracteres.`
  }
  if (mudou(form, config, 'corPrimaria') && texto('corPrimaria') && !COR_VALIDA.test(texto('corPrimaria'))) {
    erros.corPrimaria = `Use o código da cor, como ${EXEMPLO_DE_COR}.`
  }
  if (mudou(form, config, 'whatsappPedidos') && texto('whatsappPedidos') && ![10, 11].includes(digitosDoWhatsApp(form.whatsappPedidos).length)) {
    erros.whatsappPedidos = 'Informe o WhatsApp com DDD.'
  }
  for (const campo of ['pedidoMinimoCentavos', 'freteGratisCentavos']) {
    if (mudou(form, config, campo) && (form[campo] ?? 0) > LIMITE_MOEDA_CENTAVOS) erros[campo] = 'Valor alto demais.'
  }
  // Nulo no PUT é "não muda": a API não tem como tirar o frete grátis depois de definido.
  if (mudou(form, config, 'freteGratisCentavos') && form.freteGratisCentavos === null) {
    erros.freteGratisCentavos = 'O EasyStok ainda não apaga o frete grátis. Informe um valor.'
  }
  return erros
}

const marcaComoVeio = (config) => ({
  subtituloPublico: config?.subtituloPublico ?? '',
  logoUrl: config?.logoUrl ?? null,
  corPrimaria: config?.corPrimaria ?? null,
  whatsappPedidos: config?.whatsappPedidos ?? null,
  mensagemForaArea: config?.mensagemForaArea ?? null,
})

export function corpoDoSite(config, form) {
  const corpo = marcaComoVeio(config)
  const texto = (campo) => String(form[campo] ?? '').trim()
  if (mudou(form, config, 'subtituloPublico')) corpo.subtituloPublico = texto('subtituloPublico')
  // Cor vazia volta para a cor padrão do site; a API recusa texto vazio nela, mas aceita nulo.
  if (mudou(form, config, 'corPrimaria')) corpo.corPrimaria = texto('corPrimaria') || null
  // WhatsApp apagado vai vazio: nulo seria "não muda" se fosse o único campo de marca preenchido.
  if (mudou(form, config, 'whatsappPedidos')) {
    const digitos = digitosDoWhatsApp(form.whatsappPedidos)
    corpo.whatsappPedidos = digitos ? mascaraTelefone(digitos) : ''
  }
  if (mudou(form, config, 'pedidoMinimoCentavos')) corpo.pedidoMinimoEntrega = (form.pedidoMinimoCentavos ?? 0) / 100
  if (mudou(form, config, 'freteGratisCentavos') && form.freteGratisCentavos !== null) corpo.freteGratisAcima = form.freteGratisCentavos / 100
  return corpo
}

// O logo novo vai com a marca como está gravada: uma edição ainda não salva não sai junto.
export const corpoDoLogoNoSite = (config, logoUrl) => ({ ...marcaComoVeio(config), logoUrl })

// O logo que sai nos impressos: o da loja e, sem ele, o do site (`PedidoImpressoQueries.ObterCasaAsync`).
export const logoDosImpressos = (logoDaLoja, logoDoSite) => logoDaLoja || logoDoSite || null

// A Impressão troca só o logo da loja (e o upload apaga o anterior): o site pode ficar apontando
// para outro arquivo, ou para nenhum, enquanto os impressos já usam o da loja.
export const siteComOutroLogo = (logoDaLoja, logoDoSite) => Boolean(logoDaLoja) && logoDaLoja !== (logoDoSite || null)

export function motivoDoArquivoDeLogo(arquivo) {
  if (!TIPOS_DE_LOGO.includes(arquivo?.type)) return 'Escolha uma imagem PNG ou JPG.'
  if (arquivo.size > TAMANHO_MAXIMO_DO_LOGO) return 'A imagem não pode ser maior que 5 MB.'
  return null
}
