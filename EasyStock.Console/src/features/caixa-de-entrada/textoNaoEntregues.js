// #3322: o aviso de não entregues cabe numa linha na coluna de Conversas. O visível é curto ("6 não entregues");
// o leitor de tela ouve o mesmo começo e o complemento (WCAG 2.5.3: o nome contém o texto visível, na ordem).
export function textoDaFaixaNaoEntregues({ total = 0, erro = false, carregando = false } = {}) {
  if (erro) return { visivel: 'Mensagens sem atualizar', complemento: null }
  if (carregando && total === 0) return { visivel: 'Conferindo mensagens…', complemento: null }
  return {
    visivel: total === 1 ? '1 não entregue' : `${total} não entregues`,
    complemento: total === 1 ? ', mensagem para o cliente.' : ', mensagens para clientes.',
  }
}
