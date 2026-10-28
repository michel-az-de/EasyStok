### Fixed
- **Registrar pagamento e recebimento de conta exige gerenciar** (#2095, #476): quem só consulta contas a pagar ou a
  receber não lança mais saída ou entrada no Caixa (antes bastava a permissão de visualizar).
- **Baixa manual por Pix só vale para a própria parcela** (#2095): `POST /api/contas-a-receber/parcelas/{id}/reconciliar`
  confere que o txid é da parcela da rota e da empresa de quem chama, e o valor vem da consulta à Efí. O corpo deixa de
  aceitar `valorPagoEfi` e `pagoEm` (antes baixava qualquer parcela de qualquer empresa com o valor informado).
