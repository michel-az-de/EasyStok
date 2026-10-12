import { DOCUMENTOS, PAPEIS } from '../dominio/impressos'
import { confirmarDocumentoImpresso, enviarLogoDaLoja, historicoDoPedido, obterOriginalImpresso, obterLojaDaEmpresa, prepararImpresso } from '../infra/api/impressosApi'
import { gravarPreferenciasImpressao, lerPreferenciasImpressao } from '../infra/preferenciasImpressao'

// Impressos em HTML da API (#1632) para as telas: preferências do aparelho, o logo da loja e
// o que a ação "Imprimir" de um pedido precisa. As preferências são lidas na hora do clique,
// então a troca feita em Configurações › Impressão vale na próxima impressão.
export { enviarLogoDaLoja, gravarPreferenciasImpressao, lerPreferenciasImpressao, obterLojaDaEmpresa }

// Propriedades de `componentes/ImprimirPedido` para um pedido que existe no EasyStok.
export const impressaoDoPedido = (pedidoId, canhotoId = null) => ({
  documentos: DOCUMENTOS,
  papeis: PAPEIS,
  papelPadrao: (documento) => lerPreferenciasImpressao().papeis[documento],
  preparar: async (opcoes, sinal) => {
    const registro = await prepararImpresso(pedidoId, { ...opcoes, logoTermica: lerPreferenciasImpressao().logoTermica }, sinal)
    sinal.throwIfAborted()
    const html = await obterOriginalImpresso(pedidoId, registro.id, sinal)
    return { ...registro, html }
  },
  historico: (pagina, sinal) => historicoDoPedido(pedidoId, pagina, sinal),
  confirmar: (registro) => confirmarDocumentoImpresso(registro.id, registro.originalId || registro.documento !== 'cozinha' ? null : canhotoId),
})
