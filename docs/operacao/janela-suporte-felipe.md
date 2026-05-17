# Janela de suporte do Felipe — módulo Caixa Conciliado

> Este documento é parte do contrato operacional entre Felipe (dev EasyStock)
> e a Casa da Babá (cliente piloto do módulo Caixa Conciliado). **Precisa de
> assinatura escrita da Casa da Babá antes do go-live de produção (F8).**
>
> Versão: 1 — 2026-05-16. Origem: `docs/plan/07-faseamento.md` H.5 + Premissa N.11.

## Por que este documento existe

Felipe é o único desenvolvedor do EasyStock e trabalha na Avanade durante
o dia útil. O módulo de Caixa Conciliado é crítico para o fechamento diário
da Casa da Babá — se falhar fora da janela de suporte do Felipe, a
operadora precisa ter um caminho de fallback automático que **não dependa
de telefonar para ele**.

Este documento declara honestamente:

1. Quando Felipe está disponível para suporte humano.
2. Quando não está, e o que acontece nesse intervalo.
3. Como a Casa da Babá aciona o fallback sem depender de Felipe.

## Janela real de suporte

| Horário | Disponibilidade | Como acionar |
|---|---|---|
| Seg–sex 09h–18h | **INDISPONÍVEL** (Avanade — trabalho contratado) | Não acionar; mensagem fica para 19h |
| Seg–sex 19h–22h | Disponível | WhatsApp do Felipe |
| Seg–sex 22h–09h | Indisponível (sono + tempo família) | Mensagem fica para 19h do dia útil seguinte |
| Sáb 09h–22h | Disponível | WhatsApp |
| Dom 09h–22h | Disponível | WhatsApp |
| Dom 22h–seg 19h | **Janela mais longa de indisponibilidade** (pior caso: 15h sem resposta) | Mensagem fica para 19h de segunda |

## Fallback automático — flag de emergência

Para todos os horários **fora da janela do Felipe**, a Casa da Babá usa o
botão **"Desligar Caixa Novo (emergência)"** disponível no admin do
EasyStock.

**O que o botão faz** (em ≤ 1 minuto):

1. Desliga a feature flag `CaixaConciliadoV2` para a empresa Casa da Babá.
2. A UI do PWA volta automaticamente para o fluxo de Caixa antigo (versão
   anterior ao deploy de meados de julho de 2026).
3. Backend para de gravar em `sessoes_caixa` e `movimentos_caixa` linkados —
   pagamentos continuam funcionando via `PedidoPagamento` (fluxo legacy).
4. Casa da Babá não perde funcionalidade básica — perde apenas os ganhos
   novos (conferência, PDF, hash) até Felipe investigar.
5. Felipe recebe notificação por WhatsApp + email assim que possível.

**Quem pode usar**:
- Operadora Thatiane (treinada em 15min em F8).
- Qualquer admin da empresa.

**Endpoint técnico**:
```
POST /api/empresa/feature-flags/desligar-emergencia
Authorization: Bearer <jwt admin>
```
Idempotente (chamar 2x não causa efeito adicional). Registrado em
`AuditLog`.

## SLA combinado com a Casa da Babá

- **Suporte humano em janela**: resposta de Felipe em até 30 min via
  WhatsApp; resolução depende da gravidade.
- **Fora da janela — falha não-crítica** (bug cosmético, UI estranha mas
  caixa fecha): Casa da Babá registra ocorrência; Felipe trata na próxima
  janela.
- **Fora da janela — falha crítica** (operadora não consegue registrar
  pagamento OU caixa fecha com valor errado): Thatiane (ou admin) aperta
  botão "Desligar Caixa Novo (emergência)". Voltam para o fluxo antigo em
  ≤ 1 min. Felipe trata na próxima janela.
- **Tempo de resposta de Felipe a mensagens fora da janela**: até 19h do
  próximo dia útil **ou** 09h do próximo sábado/domingo, o que vier antes.

## O que NÃO está coberto

- Plantão 24/7. Felipe não é on-call profissional. Não vai responder
  WhatsApp às 3h da manhã.
- Bugs do EasyStock fora do módulo Caixa Conciliado (pedidos, estoque,
  produtos, etc.) — mantém-se o canal usual de suporte do EasyStock.
- Infraestrutura Fly.io / Postgres caindo — mitigação via healthchecks e
  backups Fly; Felipe não acelera resposta da Fly.

## Treinamento da Thatiane (15min)

Antes do go-live, Felipe demonstra ao vivo (em call ou presencial):

1. Como reconhecer "algo está errado" no Caixa Novo.
2. Onde encontrar o botão "Desligar Caixa Novo (emergência)".
3. Como o sistema fica depois de apertar o botão (printa antes/depois).
4. Quando ligar para Felipe vs quando esperar a próxima janela.
5. Como descrever a falha por WhatsApp para acelerar diagnóstico.

Após a demo: Thatiane faz o passo 2 sozinha em staging, sem ajuda. Critério
de pronto: ela consegue desligar a flag em < 60 segundos sem assistência.

## Janela de go-live escolhida

**Go-live em sábado de manhã (07h–10h)** — não domingo à noite.

Justificativa: maximiza janela de suporte do Felipe à frente do primeiro uso
real. Felipe acompanha sábado inteiro (até 22h) + domingo inteiro até 22h.
Total: ~30h de janela ativa nas primeiras 60h pós-deploy.

## Renovação deste documento

- Revisar trimestralmente ou a cada mudança de emprego/disponibilidade de
  Felipe.
- Próxima revisão obrigatória: 2026-08-16.

## Assinaturas

| Parte | Nome | Data | Assinatura |
|---|---|---|---|
| EasyStock (dev) | Felipe Michel de Azevedo | _____ | _____ |
| Casa da Babá (gestão) | _________________ | _____ | _____ |
| Casa da Babá (operadora) | Thatiane _______ | _____ | _____ |

> Após assinatura, anexar foto/scan deste documento em
> `docs/operacao/janela-suporte-felipe-assinada-YYYY-MM-DD.pdf` e referenciar
> aqui.
