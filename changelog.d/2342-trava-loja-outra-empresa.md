### Security

- Caixa e troca de loja do aparelho: testes travam a recusa da loja de outra empresa. No caixa essa checagem é a única barreira (sem ela a API grava a abertura com a loja alheia), e agora um teste no Postgres real cai se ela sumir (#2342).
