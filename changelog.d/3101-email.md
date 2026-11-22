### Fixed
- **Busca de cliente por e-mail no atendimento** (#3101): a consulta que acha o cliente pelo e-mail de quem escreveu passa a usar um índice, em vez de varrer todos os clientes da empresa. O resultado não muda: continua ignorando maiúsculas, minúsculas e espaços nas pontas.
