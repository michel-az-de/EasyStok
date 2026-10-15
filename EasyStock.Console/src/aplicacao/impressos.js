import { DOCUMENTOS, PAPEIS, PERFIS_IMPRESSAO, orientacaoDoPapel } from '../dominio/impressos'
import { confirmarDocumentoImpresso, enviarLogoDaLoja, historicoDoPedido, obterOriginalImpresso, obterLojaDaEmpresa, prepararImpresso, previaDoImpresso, resumoDoAtendimentoImpresso } from '../infra/api/impressosApi'
import { gravarPreferenciasImpressao, lerPreferenciasImpressao } from '../infra/preferenciasImpressao'

// Impressos em HTML da API (#1632) para as telas: preferências do aparelho, o logo da loja e
// o que a ação "Imprimir" de um pedido precisa. As preferências são lidas na hora do clique,
// então a troca feita em Configurações › Impressão vale na próxima impressão.
export { enviarLogoDaLoja, gravarPreferenciasImpressao, lerPreferenciasImpressao, obterLojaDaEmpresa }

// Propriedades de `componentes/ImprimirPedido` para um pedido que existe no EasyStok.
export const impressaoDoPedido = (pedidoId, canhotoId = null, clienteNome = null) => ({
  contexto: pedidoId ? `#${pedidoId.replaceAll('-', '').slice(0, 8).toUpperCase()}` : null,
  clienteNome,
  documentos: DOCUMENTOS,
  papeis: PAPEIS,
  papelPadrao: (documento) => lerPreferenciasImpressao().papeis[documento],
  perfis: PERFIS_IMPRESSAO,
  perfilPadrao: (documento) => lerPreferenciasImpressao().perfis[documento],
  orientacao: orientacaoDoPapel,
  preparar: async (opcoes, sinal) => {
    const registro = await prepararImpresso(pedidoId, { ...opcoes, logoTermica: lerPreferenciasImpressao().logoTermica }, sinal)
    sinal.throwIfAborted()
    const html = await obterOriginalImpresso(pedidoId, registro.id, sinal)
    return { ...registro, html }
  },
  obterPrevia: (opcoes, sinal) => previaDoImpresso(pedidoId, { ...opcoes, logoTermica: lerPreferenciasImpressao().logoTermica }, sinal),
  historico: (pagina, sinal) => historicoDoPedido(pedidoId, pagina, sinal),
  confirmar: (registro) => confirmarDocumentoImpresso(registro.id, registro.originalId || registro.documento !== 'cozinha' ? null : canhotoId),
})

// #1839: o resumo do atendimento pelo mesmo modal, papel e prévia dos impressos do pedido.
// Não é de um pedido: sem histórico nem conferência de saída.
const DOCUMENTO_RESUMO = [{ valor: 'resumo', rotulo: 'Resumo do atendimento' }]
export const impressaoDoResumo = (corpo) => ({
  documentos: DOCUMENTO_RESUMO,
  papeis: PAPEIS,
  papelPadrao: () => 'a4',
  orientacao: orientacaoDoPapel,
  semHistorico: true,
  semCodigo: true,
  titulo: 'Imprimir resumo do atendimento',
  obterPrevia: ({ modelo }, sinal) => resumoDoAtendimentoImpresso(modelo, corpo, sinal),
  preparar: async () => null,
  historico: async () => [],
  confirmar: async () => {},
})
