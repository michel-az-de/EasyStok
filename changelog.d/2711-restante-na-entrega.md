### Fixed
- **Entrega pelo PWA de pedido com sinal grava o restante** (#2711): o vínculo do aparelho não registrava recebimento
  quando o pedido já tinha qualquer pagamento, e o restante pago na entrega sumia do histórico do pedido. Agora a
  entrega grava o total menos o já recebido, com a forma informada no aparelho; o sinal fica no dia em que entrou e o
  reenvio do pedido não duplica.
