### Fixed

- **Cobrança na entrega** (#1776, item 19): o pedido só aparece "Pago" com o saldo zerado no servidor; com sinal pago, Entregas, a viagem e o roteiro impresso dizem "Receber R$ X na entrega". Marcar entregue com saldo a receber pela Cozinha, pelo Balcão ou pela viagem pede confirmação, e a API recusa sem ela (409 `SALDO_A_RECEBER`). (#1839)
- **Janelas, bloqueios e zonas de frete** (#1776, itens 20 a 22): a API recusa janela sobreposta no mesmo dia e mais de 500 vagas; bloqueio no passado, além de um ano, duplicado ou com janela de outro dia da semana. A lista de bloqueios mostra o ano inteiro, a janela editada acompanha o horário novo e a zona de frete ganha editar, excluir e aviso de bairro ou faixa de CEP repetidos. (#1839)
