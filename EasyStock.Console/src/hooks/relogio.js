// Próximo instante do relógio da aplicação (F07, item 1). Puro.
//
// Demonstração: o protótipo parte de um instante fixo e soma o passo, para a
// tela abrir sempre igual. Modo API: o relógio é o real, relido a cada tique.
// Somar o passo ali atrasava tudo que depende da hora (janela de 24 h, loja
// aberta, atraso do KDS) quando a aba ia para segundo plano ou o notebook
// dormia, porque o navegador segura o `setInterval` e o tique perdido não volta.
export const proximoInstante = (anterior, passoMs, real, agoraReal) =>
  (real ? agoraReal : anterior + passoMs)
