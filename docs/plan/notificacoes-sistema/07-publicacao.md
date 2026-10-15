# Publicação e smoke das notificações de plataforma

Código mergeado em 02/10: S0 e N0 a N13 (#1348 a #1390). Este roteiro liga as notificações em
produção. **Segredo nunca pelo chat:** os valores vão direto no `/opt/stacks/easystok/.env`.

## 1. Antes de publicar (Felipe)

- [ ] **Forma de pagamento na WABA.** Desde 01/10 a Meta cobra mensagens de serviço acima da
      franquia, e isso vale também para o atendimento.
- [ ] **E-mail `easystok.online` no hPanel.** Uma caixa Starter basta (`avisos@`), com `seguranca@`
      como alias, ou então duas caixas. O DNS precisa de MX, SPF, DKIM `hostingermail-a/b/c` e
      DMARC `p=none` com `rua`.
- [ ] **2º número de WhatsApp** registrado na mesma WABA. Anotar o `phone_number_id`.
- [ ] **Modelos aprovados na Meta:**
  - `codigo_redefinir_senha` (autenticação, copiar código)
  - `convite_acesso_link`
  - `incidente_sistema`
  - `prazo_estourado`
  - `resumo_diario`
  - `senha_alterada`
- [ ] **Check no Healthchecks.io** criado; guardar a URL de ping. **Monitor no UptimeRobot** em
      `https://<api>/health/notificacoes`.

## 2. `.env` da VPS e compose (nomes das chaves)

As linhas abaixo entram no `environment:` da **api** e do **worker** do `compose.yaml`, apontando
para variáveis do `.env`:

```yaml
# api e worker
Smtp__Host: smtp.hostinger.com
Smtp__Port: "465"                       # SSL implícito; 587 também funciona (STARTTLS)
Smtp__Username: "${EZ_SMTP_USER}"       # avisos@easystok.online
Smtp__Password: "${EZ_SMTP_PASSWORD}"
Smtp__FromEmail: avisos@easystok.online
Smtp__FromName: EasyStok
Smtp__Seguranca__Username: "${EZ_SMTP_SEG_USER:-${EZ_SMTP_USER}}"
Smtp__Seguranca__Password: "${EZ_SMTP_SEG_PASSWORD:-${EZ_SMTP_PASSWORD}}"
Smtp__Seguranca__FromEmail: seguranca@easystok.online
Auth__Google__EmpresaPadrao: "Casa da Baba"   # o worker também precisa (eventos de plataforma)
Notifications__WhatsApp__Meta__AccessToken: "${EZ_META_ACCESS_TOKEN:-}"
Notifications__WhatsApp__Plataforma__Provider: "${EZ_WA_PLATAFORMA_PROVIDER:-stub}"   # meta quando o 2º número existir
Notifications__WhatsApp__Plataforma__PhoneNumberId: "${EZ_WA_PLATAFORMA_PHONE_ID:-}"
Notifications__WhatsApp__Plataforma__VerifyToken: "${EZ_WA_PLATAFORMA_VERIFY:-}"

# só api
Auth__TrustedLinkOrigins__0: "https://${APP_HOST}"
Auth__LinkRedefinirSenha: "https://${APP_HOST}/auth/redefinir-senha?token={0}"
Auth__LinkConvite: "https://${APP_HOST}/auth/convite?token={0}"

# só worker
Notifications__Monitoring__PingUrl: "${EZ_HEALTHCHECKS_URL:-}"
```

A API não roda mais os loops do motor (decisão no código, N1), então
`Notifications__Hosting__Mode` não é necessário.

**Para aplicar a mudança:** `docker compose up -d api worker`. Depois, conferir no log do
`ez-worker` a linha de início dos loops e que não há erro de validação na subida.

## 3. Publicação

```bash
scripts/deploy/vps-deploy.sh
```

São 6 migrações aditivas, aplicadas no startup da API. O script faz o `pg_dump` antes e volta as
imagens sozinho se o health falhar.

## 4. Smoke em produção

1. **Medição.** Rodar `scripts/diagnostico/notificacoes-s0.sql`. Os 3 eventos antigos `Pendente`
   devem virar `Processado` ou `Expirado`, e nenhum evento ficar `Pendente` acima do prazo.
2. **Disparo de teste por tipo** (superadmin): `POST api/admin/notificacoes/disparo-teste` com
   `ResetSenha`, `ConviteAcesso`, `IncidenteSistema`, `PrazoEstourado` e `ResumoDiario`. Em cada
   e-mail recebido, o cabeçalho deve ter `dkim=pass`.
3. **Status no log.** Em `notif_logs_envio`, o provider precisa ser real (`smtp`, `meta`). Nenhum
   `Simulado` pode aparecer em canal configurado.
4. **Esqueci senha** de um usuário de teste: o e-mail chega com link de 30 min. Com telefone
   verificado, chega também o código no WhatsApp pelo 2º número.
5. **Convite:** criar um usuário sem senha e aceitar pelo link. A dona vê "aceito via".
6. **Resumo diário:** criar a rotina da Casa da Baba (a global é só o modelo) e receber o envio
   das 20:00.
7. **Vigia externo:** parar o `ez-worker` por 5 min deve fazer o Healthchecks.io alertar.

## 5. Rollback

- **Por canal:**
  - WhatsApp de plataforma: `Notifications__WhatsApp__Plataforma__Provider=stub`.
  - E-mail: remover `Smtp__Host`; o envio cai no console e vira `Simulado`.
- **Sessão:** `Auth__SessoesRevogaveis=false` desliga a revogação.
- **Versão:** `scripts/deploy/vps-deploy.sh --rollback <sha> --motivo "..."`. O banco não volta,
  mas as migrações são aditivas.
