// Ação do passo "aguardando pagamento" da barra do pedido, pura e sem JSX para a prova importar (#1875).
// O passo "aguardando pagamento" não tem um botão único: o próximo passo
// muda com a situação da cobrança (secao 2, "Alerta do Pix"). Substitui a
// antiga trava, que só mostrava texto e travava a barra inteira.

export function acaoDoPix(situacaoCob, { prerequisito, rotuloGerar, linkVencidoApi, linkPendenteApi = false, aoAbrirBaixa, aoGerarPix, aoReenviarPix }) {
  switch (situacaoCob.chave) {
    case 'nenhuma':
      return {
        rotuloEstado: 'Cobrança não gerada',
        // #1474 (R3): o que falta vem pronto de quem monta a barra (comanda vazia, cadastro,
        // endereço, janela), para o botão nascer desabilitado com o motivo à vista.
        motivo: prerequisito,
        // Atalho da barra: gera no meio marcado nos chips do bloco Cobrança;
        // sem meio marcado, pergunta antes (ponta a da integração).
        botoes: [{ rotulo: rotuloGerar, icone: 'dollar-sign', onClick: aoGerarPix, desabilitado: Boolean(prerequisito) }],
      }
    case 'expirada':
      // #1586: no modo API quem reemite (ou cancela) é o job do EasyStok; o botão só avisava.
      // No lugar dele, o texto diz o que vai acontecer.
      // #1683: quem pagou por fora com o link vencido ainda precisa da baixa à mão.
      if (linkVencidoApi) {
        return {
          rotuloEstado: 'Cobrança venceu', motivo: linkVencidoApi,
          botoes: [{ rotulo: 'Marcar pago', icone: 'dollar-sign', onClick: aoAbrirBaixa }],
        }
      }
      return {
        rotuloEstado: 'Cobrança venceu',
        botoes: [{ rotulo: 'Reenviar cobrança', icone: 'refresh-cw', onClick: aoReenviarPix }],
      }
    case 'em-conferencia':
      return {
        rotuloEstado: 'Conferindo comprovante',
        botoes: [{ rotulo: 'Marcar pago', icone: 'dollar-sign', onClick: aoAbrirBaixa }],
      }
    case 'divergente':
      return { rotuloEstado: 'Valor diferente', divergente: true }
    default:
      // #1875: com o link do provedor aberto o Mercado Pago confirma sozinho; dar baixa à mão é a exceção
      // (dinheiro que entrou por fora), então deixa de ser o botão mais forte da barra. Na demonstração, na
      // maquininha e no sinal manual não há link e o botão segue primário.
      if (linkPendenteApi && situacaoCob.chave === 'aguardando') {
        return {
          rotuloEstado: 'Aguardando pagamento',
          nota: 'O Mercado Pago confirma sozinho quando o cliente pagar. Use Marcar pago só se o dinheiro entrou por fora.',
          botoes: [{ rotulo: 'Marcar pago', icone: 'dollar-sign', onClick: aoAbrirBaixa, secundario: true }],
        }
      }
      return {
        rotuloEstado: 'Aguardando pagamento',
        botoes: [{ rotulo: 'Marcar pago', icone: 'dollar-sign', onClick: aoAbrirBaixa }],
      }
  }
}

