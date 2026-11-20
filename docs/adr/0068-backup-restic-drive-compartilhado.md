# ADR-0068 · Backup completo com restic num Drive compartilhado da empresa, com cópia puxada e retenção por tag

- Status: Proposto (aguarda a label `aprovado` na PR da #2977)
- Data: 2026-11-20
- Decisor: Felipe
- Substitui: ADR-0012 (backup de rótulos no R2 por GitHub Actions) e o backup externo da #1253
- Relacionados: issue #2977, plano `docs/plan/esteira-deploy-backup/README.md` (E3, S3.0 a S3.7),
  `revisao-adversarial.md` (seção E3), runbook `docs/dev/backup-drive.md`

## Contexto

O inventário S3.0 (20/11/2026) mediu o estado real:

- O envio da #1253 (rclone crypt para um Drive pessoal) falha desde 07/11 por cota esgotada.
- A ADR-0012 nunca rodou: os workflows `backup-rotulos.yml` e `restore-test-rotulos.yml` dependem de
  bucket R2, Fly volume e segredos que não existem, e a tabela de rótulos ainda não existe.
- O `.env` da produção guarda a KEK do cofre de credenciais (`EZ_CRYPTO_KEK` e a anterior). Sem ela,
  um dump restaurado tem as credenciais de integração ilegíveis.
- A revisão adversarial apontou três críticos: retenção com `--prune` na VPS deixa um invasor apagar
  o histórico; a VPS não pode alcançar a cópia de segurança; o `.env` com as KEKs vai para o backup.

Escolhas do Felipe por múltipla escolha (P1 a P3): Drive compartilhado da empresa; senha do
repositório no cofre de senhas e em papel; nível 1 (diário e pré-deploy); alerta pela estação local
com issue.

## Decisão

1. **restic 0.19.1 sobre rclone** num Drive compartilhado da empresa (`rclone:ezdrive:easystok-restic`).
   Conteúdo e nomes cifrados pelo restic; o Drive só vê pacotes opacos. Versão fixada com SHA256.
2. **Snapshot diário às 03:00 de Brasília** por timer systemd de usuário (`Persistent=true`), com
   `flock` contra execução dupla. Leva: dump `-Fc` e manifesto de contagens tirados do mesmo snapshot
   MVCC exportado; globals sem hash de senha (role `easystock` NOBYPASSRLS e grants); o initdb; o
   `.env` e os composes numa lista fechada; os volumes de uploads, DataProtection e WhatsApp. O stage
   em claro é apagado ao fim, com sucesso ou falha.
3. **Pré-deploy:** o `vps-deploy.sh` entrega o dump de cada deploy ao `backup.sh pre-deploy` em
   segundo plano; falha do backup não segura o deploy (o vigia acusa).
4. **Defesa contra exclusão.** A VPS só faz `forget`, nunca `--prune`, com uma conta de serviço de
   papel Content manager. A estação local, com outra conta de serviço (Manager) que nunca sai dela,
   faz `restic copy` para um repositório local que a VPS não alcança, e só então `prune` no Drive,
   bloqueado se o remoto tiver menos de 7 snapshots ou cair mais de 3 desde a última puxada.
5. **Retenção (S3.7):** diário 7 dias, 4 semanas, 12 meses; pré-deploy os 10 últimos; rótulos 5 anos
   por tag própria (`--keep-within 5y`), aplicada só ao recorte das tabelas de rótulo, dormente até a
   tabela existir. Políticas agrupadas por `host,tags`, nunca somadas entre tags.
6. **KEK no backup, senha fora dele.** O `.env` entra cifrado no snapshot; a senha do repositório vive
   no cofre de senhas e em papel, e na estação só com DPAPI. As KEKs também vão ao cofre e ao papel,
   para que a perda da VPS e do Drive juntos não leve o cofre de credenciais.
7. **Batimento e prova.** A estação roda o vigia todo dia (snapshot diário com no máximo 26 h, estado
   do `backup.sh` na VPS, última puxada) e a restauração mensal num `postgres:17` descartável, que
   aprova 4 itens: contagens iguais ao manifesto, RLS (0 linhas sem empresa), decifração das
   credenciais com a KEK restaurada e um arquivo de uploads legível. Falha abre ou comenta uma issue
   `backup` no repositório, sem dado nem caminho sensível; a volta ao normal fecha a issue.

## LGPD

- Finalidade: continuidade do serviço e obrigação de guarda. Base: execução de contrato e obrigação
  legal (rótulos, 5 anos).
- Minimização: só o banco `easystock` e os volumes do EasyStok. Os outros bancos do Postgres
  compartilhado ficam fora. Cópias `.bak` do `.env` ficam fora. Cache do navegador do WhatsApp fica
  fora.
- Prazo: o dado pessoal some do backup em no máximo 12 meses depois de apagado na produção (o mensal
  mais antigo), exceto o recorte de rótulos, que é dado regulatório e não pessoal.
- Local: Google Drive da empresa, conta Workspace, cifrado de ponta a ponta. A senha não sai do
  controle do Felipe.

## Consequências

- O backup antigo da #1253 e os workflows da ADR-0012 são removidos nesta PR. O cron antigo na VPS e
  os dados no Drive pessoal saem no passo "Corte" do runbook, com o GO do Felipe.
- A estação local passa a ser peça do backup: se ficar desligada, a puxada atrasa e o vigia avisa.
- Restaurar exige a senha (cofre ou papel) e, para o cofre de credenciais, as KEKs (já no snapshot).

## Rollback

Desligar o timer (`systemctl --user disable --now easystok-backup.timer`) e as tarefas da estação. O
repositório no Drive e o local seguem legíveis com a senha. Nada muda na produção.
