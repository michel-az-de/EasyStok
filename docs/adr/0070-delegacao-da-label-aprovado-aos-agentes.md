# ADR-0070 · O agente aplica a label `aprovado` em nome do Felipe

- Status: Aceito
- Data: 2026-11-21
- Decisor: Felipe
- Amplia: ADR-0043 e ADR-0055, nos tiers de merge (quem aplica a label de tier ALTO). A lista do que é tier ALTO não muda.
- Relacionados: issue #3069; `CLAUDE.md`, seção Autorizações

## Contexto

A ADR-0055 deixou em tier ALTO o que precisa de portão: migração, RLS, autenticação, policy,
processo e `feat` ou `refactor` sem spec. O `CLAUDE.md` dizia que a label `aprovado` "só o Felipe
aplica". Na prática o Felipe delegou a aprovação conversa por conversa, e cada sessão nova voltava a
parar e perguntar, porque a regra escrita mandava. Em 21/11/2026 uma PR de ADR esperou a label e a
de implementação reaproveitou a mesma frase de aprovação, com dúvida sobre a validade.

Decisão dita pelo Felipe na sessão gestora, em 21/11/2026 às 14:39 (horário de São Paulo):

> vale pra tudo, EU JA DISSE MILHARES DE VEZES EU AUTORIZO MERGIAR APROVAR EMMEU NOME E FAER DEPLOY TA ATE NO PEDIDO INCIAL

Em seguida, à pergunta "Gravo essa autorização no CLAUDE.md para nenhuma conversa perguntar de
novo?", respondeu "Sim, grave".

## Decisão

1. **A label `aprovado` passa a ser aplicada pelo agente, em nome do Felipe.** Vale para toda PR de
   tier ALTO. Depois da label, o agente mergeia e faz deploy sem perguntar.
2. **Condições, todas medidas no commit que será mergeado:** CI verde, `/code-review` sem achado Critical e
   aceite da issue todo marcado. O review é de subagente revisor sem o contexto da sessão autora,
   porque a ADR-0043 já registra que autor, revisor e merger são a mesma conta. Faltou uma, a PR
   fica aberta com os achados comentados, sem label, sem merge e sem perguntar ao Felipe. Push
   novo depois da label remove a label e refaz as três condições.
3. **Registro.** Antes da label, o agente comenta na PR que aplica por delegação desta ADR e cola a
   prova das três condições, com o link ou a saída que prova cada item do aceite.
4. **O que não muda.** A lista "Exige GO nesta sessão" do `CLAUDE.md` (force push, `reset --hard`,
   `dotnet ef database update`, segredo de produção, mudança em produção fora dos scripts de deploy
   e as demais) continua exigindo o GO do Felipe. Segredo ou dado que só ele tem continua parando o
   agente.
5. **Migração.** Migração mergeada sob esta delegação vai à produção pelo `vps-deploy.sh`, que faz
   `pg_dump` e deixa a migration rodar no startup da API. Isso está dentro da delegação. O
   `dotnet ef database update` manual continua exigindo GO.

## Alternativas descartadas

- **Manter "só o Felipe aplica".** É o que gerou a fila e as perguntas repetidas, contra a decisão
  dele.
- **Rebaixar tudo a tier BAIXO.** Perde o registro de que a PR é de natureza sensível e a exigência
  de review sem Critical antes do merge.
- **Delegar só à sessão gestora.** O Felipe disse "vale pra tudo"; uma sessão única vira gargalo e
  ponto de falha.

## Consequências

- O portão de tier ALTO deixa de ser humano e passa a ser CI, review independente e aceite. Erro que
  passar pelos três chega à produção sem o olho do Felipe. Ele aceitou esse risco ao delegar.
- A label continua existindo: marca na PR que as condições foram conferidas e por quem.
- O Felipe revoga quando quiser, revertendo esta ADR e a seção Autorizações do `CLAUDE.md`.
