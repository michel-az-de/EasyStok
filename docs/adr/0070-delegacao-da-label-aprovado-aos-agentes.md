# ADR-0070 · O agente aplica a label `aprovado` em nome do Felipe

- Status: Aceito
- Data: 2026-11-21
- Decisor: Felipe
- Amplia: ADR-0043 e ADR-0055, nos tiers de merge (quem aplica a label de tier ALTO) e no aceite de pós-merge (item 6, qualquer tier). A lista do que é tier ALTO não muda.
- Relacionados: issues #3069 e #3077; `CLAUDE.md`, seção Autorizações

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

Ajuste do mesmo dia, às 15:45: as primeiras PRs sob a delegação tinham itens de aceite que só
existem depois do merge (CI no commit mergeado, validação em produção). Lida ao pé da letra, a
condição "aceite todo marcado" tornava a label impossível, e as sessões já deixavam esses itens
abertos sem regra escrita. À pergunta "Ajusto a ADR-0070 para aceitar item de pós-deploy aberto na
hora da label?", o Felipe respondeu "Sim, ajuste". O item 6 da Decisão registra o ajuste. A
pergunta dizia "pós-deploy"; a regra cobre também o CI do master no commit mergeado, que tem a
mesma natureza e antecede o deploy (precedente: PR #3070, issue #3063). Essa leitura é da sessão
gestora, não frase do Felipe; ele pode restringir a regra ao pós-deploy. Também é leitura da sessão
estender o mecanismo ao merge de tier baixo (a pergunta falava só da hora da label); ele pode
restringi-lo ao tier alto.

## Decisão

1. **A label `aprovado` passa a ser aplicada pelo agente, em nome do Felipe.** Vale para toda PR de
   tier ALTO. Depois da label, o agente mergeia e faz deploy sem perguntar.
2. **Condições, todas medidas no commit que será mergeado:** CI verde, `/code-review` sem achado Critical e
   aceite da issue todo marcado, salvo o aceite de pós-merge do item 6. O review é de subagente
   revisor sem o contexto da sessão autora, porque a ADR-0043 já registra que autor, revisor e
   merger são a mesma conta. Faltou uma, a PR fica aberta com os achados comentados, sem label,
   sem merge e sem perguntar ao Felipe. Push novo depois da label remove a label e refaz as três
   condições.
3. **Registro.** Antes da label, o agente comenta na PR que aplica por delegação desta ADR e cola a
   prova das três condições, com o link ou a saída que prova cada item do aceite e a lista dos
   itens de pós-merge que ficam abertos, cada um com o motivo.
4. **O que não muda.** A lista "Exige GO nesta sessão" do `CLAUDE.md` (force push, `reset --hard`,
   `dotnet ef database update`, segredo de produção, mudança em produção fora dos scripts de deploy
   e as demais) continua exigindo o GO do Felipe. Segredo ou dado que só ele tem continua parando o
   agente.
5. **Migração.** Migração mergeada sob esta delegação vai à produção pelo `vps-deploy.sh`, que faz
   `pg_dump` e deixa a migration rodar no startup da API. Isso está dentro da delegação. O
   `dotnet ef database update` manual continua exigindo GO.
6. **Aceite de pós-merge (ajuste de 21/11/2026, issue #3077).** Fica aberto na hora da label (ou do
   merge, no tier baixo) o item de aceite que, por natureza, só se mede depois do merge, e somente
   estes: CI do master no commit mergeado, deploy e validação em produção.
   O que dá para medir na PR (teste,
   build, revisão, prova local) não é pós-merge e continua barrando a label. Validação em produção
   não dispensa a prova na PR: o mesmo comportamento precisa estar provado por teste ou prova local
   antes da label; comportamento que só existe em produção e não tem prova na PR fica sem label.
   Salvaguardas: a prova da label lista cada item aberto e por que só se mede
   depois; a PR usa `refs #N`, não `closes #N`, para o merge não fechar a issue; no merge, a sessão
   comenta na issue os itens pendentes; ela mede cada item, marca com a prova e fecha a issue antes
   de se declarar concluída. Se ela cair antes de medir, qualquer sessão retoma pela issue aberta.
   Item que falhar depois do merge mantém a issue aberta e a sessão corrige por nova PR no mesmo
   ciclo. O `vps-deploy.sh --rollback` (sha, motivo e confirmação; não reverte o banco) só segura a
   produção até essa correção, que vem antes do próximo deploy. O `revert` no master continua
   exigindo GO. O mecanismo (`refs #N`, issue aberta até a prova) vale para qualquer tier.

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
- Com o aceite de pós-merge aberto na label, o erro que só aparece em produção é descoberto depois
  do merge. A issue aberta e a lista de itens na prova da PR registram o que falta medir.
- A label continua existindo: marca na PR que as condições foram conferidas e por quem.
- O Felipe revoga quando quiser, revertendo esta ADR e a seção Autorizações do `CLAUDE.md`.
