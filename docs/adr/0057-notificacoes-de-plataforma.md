# ADR-0057 · Notificações de plataforma: motor interno, remetente próprio e entrega honesta

- Status: Proposto
- Data: 2026-10-01
- Emenda: ADR-0055. As fatias `S0` e `N0` a `N13` do plano `docs/plan/notificacoes-sistema/` contam
  como spec de plano aprovado.
- Mantém: a decisão de 2026-08-08 de consertar o motor interno em vez de levar as notificações ao
  Hiram (#831, #832) e de não ligar o `CronExpression` da rotina.
- Relacionados: ADR-0010 (RLS), ADR-0030 (outbox transacional), ADR-0050 e ADR-0051 (Meta e canais),
  ADR-0056 (tenant único), issues #1344, #783, #301.

## Contexto

O EasyStok precisa avisar os usuários internos (dona, equipe e superadmin) por e-mail e WhatsApp.
Os casos são esqueci senha, primeiro acesso, problemas no sistema, prazo estourado e rotinas
agendadas, e nada disso pode passar pelo número do atendimento.

O motor já existe (`EasyStock.Infra.Notifications`, `Application/Services/Notifications`), mas o
levantamento de 01/10 (`docs/plan/notificacoes-sistema/00-diagnostico.md`) mediu que ele não entrega
nada em produção:

- O papel do banco é `NOBYPASSRLS` e os loops nunca ligam o bypass, então leem 0 linhas.
- A API e o Worker sobem os loops ao mesmo tempo.
- Os stubs e o console de e-mail devolvem sucesso, e a mensagem fica `Enviado` sem ter saído.
- Os produtores perdem eventos sem registrar erro.
- O destinatário só vem do payload.
- O provider da Meta está acoplado à conversa do atendimento.

## Decisão

1. **Motor interno, Worker como único host.** A API fica com os loops desligados por código. Não
   haverá serviço novo nem Hiram.
2. **Remetente de plataforma, separado da loja.**
   - **WhatsApp:** o 2º número da mesma WABA, só com template.
   - **E-mail:** `seguranca@easystok.online` para a categoria Segurança e `avisos@easystok.online`
     para o resto.
   - O remetente é escolhido por mensagem: o da Loja para avisos ao cliente e campanhas, o da
     Plataforma para o sistema.
   - A notificação de plataforma nunca toca `Conversa` nem o número da loja.
3. **Isolamento.**
   - Os loops só reservam trabalho com bypass, por uma porta, numa transação curta com
     `FOR UPDATE SKIP LOCKED`, que é o padrão da S39.
   - Cada item é processado num escopo próprio, com o tenant fixado.
   - As linhas globais do catálogo ficam legíveis para o tenant.
4. **Entrega.**
   - E-mail, in-app e push saem ao menos uma vez.
   - WhatsApp e SMS saem no máximo uma vez: o status `EmEnvio` é gravado antes da chamada. Se der
     timeout, a mensagem fica `Indeterminado` e não é reenviada sozinha. O status final vem pelo id
     do provider, via webhook.
5. **Verdade do envio.**
   - Stub e console marcam `Simulado`, nunca `Enviado`.
   - Stub em Production deixa o health degradado.
   - Erro definitivo vira falha permanente, sem retentativa.
6. **Backlog com prazo.** Antes de destravar, cada tipo de evento ganha um prazo de validade. O que
   venceu vira `Expirado` e nunca sai.
7. **Eventos sem tenant próprio**, como incidente e aviso ao superadmin, usam a empresa padrão. É a
   mesma de `Auth:Google:EmpresaPadrao`, coerente com o tenant único da ADR-0056.
8. **Segurança da conta.**
   - O link de redefinição vai só por e-mail. Pelo WhatsApp vai só código, em template de
     autenticação.
   - O primeiro acesso é por convite com link: a DM7-3 foi decidida pela opção (c) em 01/10.
   - A categoria Segurança apaga corpo, payload e metadados quando a mensagem termina.
9. **Agendamento por horário diário local**, com a janela gravada em `ProximaTentativaEm` e
   idempotência por período. O `CronExpression` continua sem uso.
10. **SMS fica fora agora.** No Brasil, o número comum serve só para conversa entre pessoas, e o
    remetente alfanumérico leva semanas para registrar. O código Twilio fica como está; a limpeza de
    providers inertes segue na #783.
11. **Tier.** As fatias deste plano seguem a ADR-0055. Quando ela e a R5 do `CLAUDE.md` (mudança
    grande é ALTO) divergirem, vale a regra mais restritiva.

## Alternativas descartadas

- **Hiram como motor.** Já foi descartado em 2026-08-08. Exigiria cutover e PRs em outro
  repositório.
- **Mesmo número do atendimento.** Mistura sistema e cliente na mesma conversa e na mesma janela de
  24 h.
- **Gmail pessoal, `rigorsistemas.com.br` ou `casadababa.com`.** Os dois primeiros são marcas
  erradas e o Gmail tem limite baixo. O `casadababa.com` tem DNS fora da Hostinger e não tem SPF.
- **Link de redefinição no WhatsApp.** A Meta reserva verificação para o template de
  autenticação, que não aceita URL, e o link fica no histórico e em encaminhamentos.
- **Empresa "Plataforma" fictícia.** Contraria o tenant único da ADR-0056.
- **Claim nova de carimbo no JWT.** O `iat` que o token já traz basta.

## Consequências

- O plano `docs/plan/notificacoes-sistema/` vira a fonte das fatias. As issues #831 e #832 ficam
  superadas e a #783 é reposicionada.
- **Custo recorrente:**
  - caixas de e-mail em `easystok.online`;
  - mensagens utility e de autenticação da Meta, cerca de R$ 0,035 cada (fonte terceira, a
    conferir no painel).
  - Desde 01/10/2026 a Meta cobra também mensagens de serviço acima da franquia, o que exige forma
    de pagamento na WABA, inclusive para o atendimento.
- O motor ganha um vigia externo (Healthchecks.io e UptimeRobot), porque o alerta de que o motor
  parou não pode depender do próprio motor.
