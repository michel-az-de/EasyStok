# Notificações de plataforma · e-mail e WhatsApp fora do atendimento

Plano SDD aprovado pelo Felipe em 2026-10-01 (issue #1344, ADR-0057). Ele foi revisado por uma banca
adversarial em três lentes: confiabilidade do motor, segurança e LGPD, produto e operação.

## Objetivo

Avisar os usuários internos (dona, equipe e superadmin) por e-mail e WhatsApp, por um remetente
próprio, nos casos abaixo:

| Caso | Quem recebe | Canais |
|---|---|---|
| Esqueci senha | o próprio usuário | e-mail com link; WhatsApp com código |
| Primeiro acesso | o funcionário novo | convite com link por e-mail e WhatsApp |
| Problemas no sistema | superadmin | e-mail e WhatsApp, com aviso de resolvido |
| Prazo estourado | dona e equipe | cliente sem resposta, pedido atrasado, impressão travada, caixa esquecido |
| Rotinas agendadas | dona | resumo diário, mais o motor genérico para as próximas |

Tudo isso precisa funcionar **mesmo antes de existir o produtor**: cada tipo tem rotina e template
no catálogo, e um disparo de teste por tipo prova o caminho inteiro (N13).

## Leitura

1. [00-diagnostico.md](00-diagnostico.md): o que foi medido em 01/10 no código, na VPS, no DNS e nas
   assinaturas.
2. [01-verdade-do-motor.md](01-verdade-do-motor.md): M0, com S0, N0, N2 e N1.
3. [02-email-real.md](02-email-real.md): M1, com N3.
4. [03-catalogo.md](03-catalogo.md): M2, com N13 e N5.
5. [04-equipe-avisada.md](04-equipe-avisada.md): M3, com N4, N10, N11 e N12.
6. [05-whatsapp-plataforma.md](05-whatsapp-plataforma.md): M4, com N6.
7. [06-acesso.md](06-acesso.md): M5, com N7, N8 e N9.
8. Decisões de arquitetura: [ADR-0057](../../adr/0057-notificacoes-de-plataforma.md).

## Ordem

```
Onda 0 (Felipe, sem código) ─────────────────────────────────────────────┐
S0 → N0 → N2 → N1 → N3 → N13 → N5 → N4 → (N10 ∥ N11 ∥ N12) → N6 → N7 → N8 → N9
      M0 verdade            M1  M2 catálogo  M3 equipe avisada     M4    M5 acesso
```

| Marco | Entrega visível | Specs |
|---|---|---|
| M0 | o motor deixa de mentir e volta a ler; backlog velho expira em vez de sair | S0, N0, N2, N1 |
| M1 | e-mail real pelo motor, com `dkim=pass` | N3 |
| M2 | todo tipo pedido tem rotina e template; disparo de teste por tipo | N13, N5 |
| M3 | a equipe recebe incidente, prazo estourado e resumo diário | N4, N10, N11, N12 |
| M4 | WhatsApp pelo 2º número, sem tocar o atendimento | N6 |
| M5 | esqueci senha e convite seguros, por e-mail e WhatsApp | N7, N8, N9 |

## Onda 0 · operação do Felipe (dia 1, sem código)

- [ ] **Hoje:** forma de pagamento na WABA. Desde 01/10/2026 a Meta cobra também mensagens de
      serviço acima da franquia, e isso afeta o atendimento que já está no ar. Conferir também a
      verificação da empresa e quantos números a conta tem: sem verificação, o limite é 2.
- [ ] Contratar o e-mail Hostinger para `easystok.online` e criar as caixas `seguranca@` e
      `avisos@`. A assinatura atual cobre `rigorsistemas.com.br`.
- [ ] DNS de `easystok.online`, todos registros que hoje faltam:
  - MX `mx1`/`mx2.hostinger.com`;
  - SPF `v=spf1 include:_spf.mail.hostinger.com ~all`;
  - DKIM `hostingermail-a/b/c._domainkey`;
  - DMARC `p=none` com `rua`, passando para `quarantine` depois de 2 a 4 semanas limpas.
- [ ] Rodar `scripts/diagnostico/notificacoes-s0.sql` na VPS e colar a saída na issue #1344.
- [ ] Chip do 2º número de WhatsApp.
- [ ] Contas grátis no Healthchecks.io e no UptimeRobot.
- [ ] `.env` da VPS, quando a N0 estiver publicada: `Smtp__*` na API e no Worker,
      `Auth__TrustedLinkOrigins__0` e `Notifications__Hosting__Mode=Disabled` na API. Segredo nunca
      pelo chat.

## Regras que valem para todas as specs

- **Uma issue, uma branch e uma PR por spec.** Teste Red antes do código; integração em
  `EasyStock.Infra.Postgre.IntegrationTests`, que está na CI, com o papel `rls_test_client`.
- **Remetente de plataforma nunca toca `Conversa`** nem o número da loja.
- **Stub em Production marca `Simulado`, nunca `Enviado`.**
- **Mensagem de categoria Segurança** não guarda token nem corpo depois de terminar.
- **Alerta sem dado pessoal.** Nada de mensagem de exceção, query string ou dado de cliente.
- **Migração só aditiva.** O deploy aplica a migração no startup e o banco não volta.

## Fora deste plano por ora

- **Verificação do telefone por código digitado pelo próprio usuário.** Nesta rodada a equipe é
  atestada pelo Admin (N4) e o convidado se verifica ao aceitar o convite (N9). A verificação por
  código vira uma spec seguinte à N8, que cria o segredo de uso único com tentativas.
- **SMS** (ADR-0057, item 10).

## Tier

Quase todas as specs passam de 100 linhas ou de 5 arquivos, então são ALTO pela R5. A ADR-0057
(item 11) manda valer a regra mais restritiva, e cada PR espera a label `aprovado` do Felipe. Só sai
como baixo a spec que realmente ficar abaixo desses limites e não tocar em migração, autenticação,
RLS ou policy.

## Estado

| Spec | Planejado | Executado | Testado | PR | Mergeado | Deployado | Validado |
|---|---|---|---|---|---|---|---|
| S0 | sim | #1344 | gate | aberta | Pendente | N/A | Pendente (SQL na VPS) |
| N0 a N13 | sim | Pendente | Pendente | Pendente | Pendente | Pendente | Pendente |
