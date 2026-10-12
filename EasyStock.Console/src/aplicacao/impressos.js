import { DOCUMENTOS, PAPEIS } from '../dominio/impressos'
import { enviarLogoDaLoja, obterHtmlDoImpresso, obterLojaDaEmpresa } from '../infra/api/impressosApi'
import { gravarPreferenciasImpressao, lerPreferenciasImpressao } from '../infra/preferenciasImpressao'

// Impressos em HTML da API (#1632) para as telas: preferências do aparelho, o logo da loja e
// o que a ação "Imprimir" de um pedido precisa. As preferências são lidas na hora do clique,
// então a troca feita em Configurações › Impressão vale na próxima impressão.
export { enviarLogoDaLoja, gravarPreferenciasImpressao, lerPreferenciasImpressao, obterLojaDaEmpresa }

// Propriedades de `componentes/ImprimirPedido` para um pedido que existe no EasyStok.
export const impressaoDoPedido = (pedidoId) => ({
  documentos: DOCUMENTOS,
  papeis: PAPEIS,
  papelPadrao: (documento) => lerPreferenciasImpressao().papeis[documento],
  buscarHtml: (documento, modelo) => obterHtmlDoImpresso(pedidoId, {
    documento, modelo, logoTermica: lerPreferenciasImpressao().logoTermica,
  }),
})
