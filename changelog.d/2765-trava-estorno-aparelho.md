### Fixed
- **Estorno feito pelo aparelho provado contra o fechamento do Caixa** (#2765): a exclusão de lançamento no app
  espera o fechamento em curso e recusa o dia que ele acabou de fechar, inclusive o fechamento da empresa. Os testes
  do aparelho rodavam sem essa trava; agora um teste no Postgres real cai se ela sumir. Só teste.
