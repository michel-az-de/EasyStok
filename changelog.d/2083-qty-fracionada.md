### Fixed
- **Pedido do console com quantidade fracionada chega inteiro ao aparelho e à Venda** (#2083): 1,5 kg descia ao PWA
  como 1 e a Venda da entrega saía com 1 (R$ 30 em vez de R$ 45). O pull leva a quantidade como está (o inteiro
  continua saindo como inteiro), o envio do aparelho aceita 1,5 sem travar o sync do pedido, e a Venda do pedido que
  nasceu no console sai dos itens do próprio Pedido. O espelho `mobile_order_items.qty` segue inteiro até a #2707.
