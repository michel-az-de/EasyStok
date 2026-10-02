# Postmortem: deploy de SHA antigo regrediu a produção (#1336)

**Data:** 2026-10-01
**Severidade:** SEV2 (funcionalidades já entregues sumiram da produção; sem perda de dados conhecida)
**Status:** Resolvido (produção republicada com o `origin/master`); guarda no script em #1336
**Formato:** blameless

## Resumo

Às 23:36 UTC (20:36 BRT) `scripts/deploy/vps-deploy.sh` foi executado com o SHA `98ed2cc1`
(#1308). Medido depois, o `98ed2cc1` está 11 commits atrás do `origin/master` (`3ede9d57`). O script aceitava qualquer
commit sem conferir contra o trunk, então buildou e publicou a versão antiga. O
`buildSha` de `https://api.easystok.online/health/version` passou a mostrar `98ed2cc1` e a
produção perdeu o que tinha entrado depois:

| PR | O que sumiu |
|----|-------------|
| #1325, #1327 | login no console com a conta Google |
| #1321 | caderno da loja em trechos para o agente |
| #1312, #1331, #1332 | correções do atendimento |

A produção voltou ao normal quando foi republicada a partir do `origin/master`. Medido depois do
conserto: `buildSha` = `3ede9d57` (#1333), igual ao `origin/master`.

## Causa

1. **O script não tinha guarda de trunk.** `vps-deploy.sh <sha>` aceitava qualquer commit que o
   `git rev-parse` resolvesse. Um SHA velho (de um worktree parado, de um histórico de terminal ou
   colado de uma conversa antiga) era publicado sem aviso.
2. **Não havia trilha de quem publicou.** Só existia `~/build/easystok-current` com o SHA no ar.
   Não dá para saber, pela VPS, quem rodou o deploy das 23:36.
3. **O script roda do lado de quem chama.** Toda a lógica, inclusive o script remoto, sai da cópia
   local. Uma cópia antiga do script (worktree parado) não teria nenhuma guarda nova.

O banco não foi revertido: a migration roda no startup da API e não há migration de descida. Não
houve relato de dado perdido, mas registros criados pelo código novo continuaram no banco enquanto
o código antigo servia.

## Correção (#1336)

- **Guarda de trunk:** antes do `git archive`, o script faz `git fetch` e recusa (exit 7) o SHA
  que não seja o `origin/master` atual nem descendente dele. A mensagem lista os commits que
  seriam desfeitos e aponta os dois caminhos certos.
- **Rollback explícito:** voltar a uma versão anterior só com
  `--rollback <sha> --motivo "<por que>"`. O SHA precisa estar na história do `origin/master`; o
  script mostra o que será desfeito e pede para digitar o SHA (ou `ROLLBACK_CONFIRMA=<sha8>` em
  automação). Sem motivo ou sem confirmação: exit 8.
- **Cópia desatualizada do script:** se a cópia executada difere da do `origin/master`, o deploy
  real é recusado (exit 7) e o `--dry-run` só avisa.
- **Histórico:** cada execução grava em `/home/felipe/build/deploy-history.log` uma linha com data
  UTC, modo (`deploy` ou `rollback`), SHA, código de saída, quem (nome e e-mail do Git, usuário e
  máquina) e motivo. Vale também para tentativa barrada pela trava de deploy simultâneo.
- `--dry-run` aplica a mesma guarda, então o teste antes do deploy já mostra a recusa.

## Riscos que continuam

- **Cópias antigas do script.** Quem rodar a versão anterior a #1336 (de um worktree que não foi
  atualizado) não passa por nenhuma guarda, e a recusa por cópia desatualizada só protege a partir
  desta versão. Uma guarda do lado da VPS (wrapper ou hook que confira o SHA) fecharia isso.
- **Recusa local não vai para o histórico.** A tentativa barrada pela guarda não chega à VPS e só
  aparece no terminal de quem rodou.
- **Branch que altera o próprio script.** Um descendente do master que mude o `vps-deploy.sh` é
  recusado pela checagem de cópia; o caminho é mergear primeiro.

## Lições

- Deploy por SHA livre é rollback disfarçado. Rollback precisa ser uma decisão nomeada, com
  motivo e confirmação.
- `health/version` com `buildSha` foi o que permitiu medir o incidente. Continua sendo a primeira
  checagem depois de qualquer publicação.
