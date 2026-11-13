### Tests
- **Criação de cadastro repetida com a mesma chave** (#2462): teste de integração com Postgres real. Dois POSTs de
  fornecedor com a mesma `Idempotency-Key` gravam um único cadastro, e o segundo volta como replay. Com outro corpo,
  a resposta é 422.
