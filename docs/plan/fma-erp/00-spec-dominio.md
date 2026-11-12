# ERP interno da FMA Software: spec do domínio e decisão de arquitetura

- Issue: #2387
- Status: protótipo revisado pela banca (`01-banca.md`); a decisão de arquitetura depende do Felipe (seção 2)
- Data: 2026-11-12

## 1. Problema

A FMA Software (engenharia de software, empresa do Felipe) não tem onde acompanhar o próprio
ciclo comercial. Leads ficam soltos, propostas não viram contrato rastreável, o contrato não gera
sozinho as parcelas a receber nem os custos a pagar, e não há lembrete de emitir e anexar a NFS-e
de cada etapa. O alinhamento comercial de 29/09/2026 exige **pagamento por etapa aprovada**, então
o sistema precisa ligar etapa aprovada, cobrança e nota.

Uso **interno**. Não é produto à venda (alinhamento comercial, itens 16 e 35).

## 2. Decisão de arquitetura

### 2.1 O que o levantamento mostrou

| Peça | Existe no EasyStok? | Reuso |
|---|---|---|
| Tenant (`Empresa`), claim `empresaId`, filtro EF + RLS | Sim (ADR-0010) | Direto |
| Contas a pagar e a receber, parcelas, categorias, centro de custo, estorno, Pix, avisos D-3/D-1 | Sim (`Domain/Entities/Financeiro/`) | **Adaptar**: `OrigemContaFinanceira` só tem Manual, Pedido, PedidoFornecedor e Importacao; `Contrato` exige migração no enum |
| Cliente PJ (`TipoPessoa`, `NomeFantasia`, `InscricaoEstadual`, CNPJ em `Documento`) | Sim (ADR-0048, #1018) | Direto, **mas** o ADR-0056 propõe podar o resíduo B2B (Cliente PJ, flags `modulo.comercial` e `modulo.crm`); reusar exige reverter essa poda |
| Auth JWT com refresh, login multiempresa, níveis | Sim (ADR-0047) | Direto |
| Máquina de estados de pedido | Sim (`PedidoStateMachine`) | Só o padrão; o `Pedido` é varejo |
| Lembrete, mensagem programada | Sim, acoplados a atendimento (`ConversaId`, `PedidoId`) | **Adaptar**: alvo genérico (tipo + id) |
| Anexo genérico | Só `IFileStorage`; sem anexo em conta ou documento | Falta |
| NF-e | Só NFC-e modelo 65 de produto | **Não serve**: serviço de software é NFS-e |
| Lead, Proposta, Contrato | Não existem | Greenfield |
| Enum `Modulo` | Fixo em 8 módulos de varejo | Precisa de módulo novo |
| Liga e desliga de módulo | `TenantFeatureFlag` existe mas **não é lido** em runtime | Falta mecanismo |
| Front | Console (React 19 + Vite) é o front vivo; Web em saída | Front novo FMA: caixa de conversas, copiloto e funil, não CRUD (este protótipo) |
| E-mail, WhatsApp, LLM | `SmtpEmailCanal`, `CanalWhatsApp` (Cloud API e WppConnect), `AnthropicMessagesClient` | **Reaproveitar** (seção 6) |

Governança vigente: o ADR-0048 (FMA como 2º tenant) foi **substituído** pelo ADR-0056 (ERP da Casa
da Babá, tenant único, front único). Qualquer opção abaixo que reabra a FMA dentro do EasyStok
reverte ou emenda o ADR-0056. Ressalva: ADR-0056 e ADR-0042 ainda estão com status **Proposto**.

### 2.2 Opções

| Critério | A. Novo tenant no deploy atual | B. Mesmo repo, instância e banco próprios | C. App novo do zero | D. Fork enxuto em repo próprio |
|---|---|---|---|---|
| Custo até o MVP | Baixo | Médio (2º deploy, 2º Postgres, flags) | Alto (refazer auth, financeiro, RLS) | Médio (fork, poda, 2º deploy) |
| Reuso | Total | Total do código | Só padrões | Total no dia do fork; depois manual |
| Acoplamento | Alto | Médio: mesmo código e release | Nenhum | Baixo: só a origem comum |
| Risco para a Casa da Babá | **Alto**: migração e enum no banco vivo | **Médio**: migrações e enum entram no release da CdB; seeds e jobs da CdB rodariam na instância FMA sem desligar | Nenhum | Nenhum |
| Isolamento de dados | Lógico (EF + RLS) | Físico | Físico | Físico |
| Governança | Reverte o ADR-0056 | Emenda o ADR-0056 e reverte a poda B2B | Não toca o EasyStok | Não toca o EasyStok |
| Correção do EasyStok chega à FMA | Automática | Automática | Não | Cherry-pick manual |

A banca de arquitetura refutou o risco "Baixo" que a primeira versão dava à B e trouxe a D.

### 2.3 Recomendação

**D, com B como alternativa.** A D tira o ERP da FMA do ciclo de release da Casa da Babá, que hoje
recebe merge e deploy de várias sessões por dia, e não depende de reverter o ADR-0056. O custo é
manter correções por cherry-pick. A **B** vale se o Felipe preferir uma base só, desde que antes
exista:

- [ ] mecanismo de flag lido em runtime (o `TenantFeatureFlag` não é);
- [ ] checklist da instância FMA desligando `PerfisCasaDaBabaSeed`, `NotificacoesGlobaisSeed`,
      `MobileSchemaInitializer` e os jobs de fundo ligados por padrão;
- [ ] ADR emendando o ADR-0056 (tenant único **por instância**) e mantendo o Cliente PJ;
- [ ] migração do enum `OrigemContaFinanceira` revisada como tier alto.

A fica descartada pelo risco ao tenant em produção; a C, pelo custo de refazer o que funciona.

**Pendente do Felipe**: escolher A, B, C ou D. O protótipo é **só front**, com dados fictícios, e
vale para qualquer opção.

## 3. Modelo de domínio

```
Lead ──qualifica──▶ Proposta ──aceita──▶ Contrato ──assinado──▶ Cliente (PJ ou PF)
                      │                    │
                      │ etapas             ├──▶ Etapa n: aprovada ──▶ ContaReceber (parcela)
                      │ (escopo, valor)    │                          └─▶ Lembrete "emitir NFS-e"
                      │                    │                                 └─▶ NFS-e anexada (XML + PDF)
                      │                    └──▶ Custos previstos ──▶ ContaPagar
                      └──▶ Lembrete "follow-up da proposta"
```

| Entidade | Campos essenciais | Estados |
|---|---|---|
| Lead | origem, empresa, contato, interesse, valor estimado | Novo, Qualificado, Proposta, Ganho, Perdido |
| Proposta | lead, versão, validade, etapas[] (escopo, entrega, aceite, valor), condições | Rascunho, Enviada, Aceita, Recusada, Expirada, Substituida |
| Contrato | proposta, cliente, vigência, etapas herdadas, anexos (PDF assinado) | AguardandoAssinatura, Vigente, Encerrado, Cancelado, Rescindido |
| Etapa do contrato | escopo, valor, data prevista | Planejada, EmExecucao, Entregue (volta a EmExecucao se o cliente pedir ajuste), Aprovada, Faturada, Paga |
| Cliente | reaproveita `Cliente` (PJ: CNPJ, razão social, IE; PF: CPF) | Ativo, Inativo |
| ContaReceber | reaproveita; `Origem = Contrato`, `OrigemRefId = etapa`; bruto, retenções, líquido, valor pago | os do financeiro |
| ContaPagar | reaproveita; `Origem = Contrato` (subcontratação, licença, imposto) | os do financeiro |
| Lembrete | chave estável (`followup:P`, `assinatura:C`, `aprovacao:E`, `nfse:E`, `vencimento:R`, `imposto:AAAA-MM`), alvo, quando | Pendente, Feito, Adiado |
| NfseAnexada | etapa, conta a receber, número, município, competência (mês da prestação), valor do serviço, retenções (ISS, IRRF, CSRF, INSS), XML, PDF, nota substituída | Anexada, Cancelada |

Invariantes:

1. Proposta aceita é imutável; mudança gera versão nova.
2. Contrato só nasce de proposta aceita. O Cliente nasce (ou é reaproveitado por documento) na **assinatura**, não no aceite; antes disso o contrato aponta para o lead. Custos previstos viram contas a pagar na assinatura.
3. A conta a receber de uma etapa só é emitida quando a etapa é **Aprovada** (pagamento por etapa aprovada).
4. Toda etapa Aprovada gera o lembrete "emitir NFS-e"; a etapa só vira **Faturada** com a NFS-e anexada.
5. Valor do serviço na NFS-e = valor **bruto** da etapa; divergência bloqueia a anexação. Retenções reduzem o **líquido** a receber (líquido = bruto − retenções). Nota cancelada reabre a etapa para uma substituta.
6. Tudo carrega `EmpresaId`; nenhum dado comercial existe sem tenant. Cada tabela nova ganha teste de RLS.
7. Lembretes abrem e fecham pela chave quando o passo acontece; nenhum fica órfão.
8. Contrato só encerra com todas as etapas pagas **e** com NFS-e.

## 4. Extensão do fluxo de pedido

O pedido da Casa da Babá é `Aguardando → Preparando → Pronto → Entregue`, com baixa de estoque e
cobrança. O fluxo comercial é a mesma ideia em escala de projeto: o **pedido** é o contrato, cada
**item** é uma etapa, **entregar** é a etapa aprovada, e a **cobrança** nasce na aprovação, não na
venda. Reaproveita-se o padrão de `PedidoStateMachine` (transições explícitas, evento por mudança),
não a entidade `Pedido` (ADR-0042 mantém o pedido de varejo canônico).

| Pedido (varejo) | Comercial (FMA) |
|---|---|
| Orçamento no WhatsApp | Proposta com etapas |
| Pedido confirmado | Contrato assinado |
| Item preparado e entregue | Etapa entregue e aprovada |
| Cobrança (Pix, caixa) | Parcela a receber da etapa |
| NFC-e modelo 65 | NFS-e (padrão nacional) anexada |

## 5. Fiscal

- Serviço de engenharia de software emite **NFS-e**, não NF-e nem NFC-e. Desde 2026 vale o padrão
  nacional (emissor nacional / ADN), com adesão municipal variável: confirmar o município da FMA.
- No MVP a nota é **emitida fora** (portal) e **anexada** (XML + PDF). Emissão integrada fica para
  depois, se houver volume.
- Reforma tributária: campos de IBS e CBS na NFS-e entram em 2026 em fase de teste. O modelo guarda
  o XML inteiro para não perder campo novo.
- Retenções (ISS retido, IRRF, CSRF, INSS) dependem do regime da FMA e do tomador. O protótipo já
  separa bruto, retenções e líquido; a regra de cálculo fica **a confirmar com o contador**.
- Competência é o mês da prestação (aprovação da etapa), não a data do anexo.
- Cancelamento e substituição: a nota cancelada fica no histórico e a substituta registra qual
  substitui.
- Cada competência com nota gera o lembrete de provisionar imposto; regime e alíquota com o contador.
- A confirmar com o contador: fato gerador (aprovação ou entrega), sinal e parcelamento dentro da
  etapa, multa, juros e reajuste de contrato longo.

## 6. Integrações e IA (v3)

O ERP parte da conversa, não do formulário. Felipe reprovou a v2 por parecer CRUD; a v3 troca a tela
de cadastro por fila do que pede decisão.

| Integração | No protótipo | No produto (reaproveita) |
|---|---|---|
| WhatsApp | Conversas, cobrança Pix e lembrete simulados | `Infra.Integrations/WhatsApp` (`CanalWhatsApp`, `WhatsAppCloudClient`, `WppConnectClient`) |
| E-mail | Envio de proposta e NFS-e simulado, com prévia | `Infra.Notifications/Email/SmtpEmailCanal` |
| LLM | Leitura da conversa, rascunho de resposta, proposta montada, briefing e copiloto, todos por regra local | `Infra.Integrations/Ia/AnthropicMessagesClient` |

| Fluxo | Comportamento |
|---|---|
| Conversa → proposta | A IA lê a mensagem, sugere etapas e custos; Felipe revisa e envia |
| Automações | Follow-up de proposta parada, cobrança no vencimento, lembrete de NFS-e pendente; cada uma liga e desliga |
| Copiloto | Responde sobre caixa, notas pendentes e funil com os dados do próprio ERP |
| Regra de ouro | IA rascunha, Felipe aprova: nada sai para cliente sem clique |

Decisões do Felipe antes da fatia real: enviar conversa de cliente a um LLM externo é decisão de
**privacidade** (consentimento, LGPD, retenção); número de WhatsApp oficial (Cloud API) ou WppConnect;
remetente de e-mail com SPF e DKIM.

## 7. Fora do escopo do protótipo

Backend, migração, auth, emissão de NFS-e, envio real de e-mail ou WhatsApp, chamada real a LLM,
assinatura eletrônica, deploy. O protótipo é estático em `prototipos/fma-erp/`, com dados **fictícios** (nenhum cliente,
CNPJ ou preço real).

## 8. Blog do Felipe

| Item | Estado |
|---|---|
| Blog da FMA | **Não existe** (nenhum repo, site ou plano editorial FMA encontrado) |
| Levante (`C:\rep\levante`, `michel-az-de/levante`) | Blog pessoal técnico; .NET + Next.js + MongoDB; artigos no banco via `/admin`; último commit 2026-08-13 |
| Domínio `felipemichel.com` | Comprado (ADR 0007 do Levante); cutover D0 em curso; falta e-mail, redirect `www` e flags de indexação |
| Rigor Sistemas | Blog de outra empresa (6 artigos em código), não é da FMA |

Não foram consultados: Hostinger, Google Drive, GitLab, repos fora de `michel-az-de`. O ERP
**não** inclui blog: o protótipo só mostra o estado e o link, para não duplicar o Levante.

## 9. Próximos passos

1. Felipe escolhe A, B, C ou D, onde fica o blog da FMA e a política de envio de dados ao LLM.
2. ADR da opção escolhida; spec da fatia 1 (Lead → Proposta → Contrato), tier alto.
3. Validar com o contador: retenções, competência, fato gerador, imposto.
4. As regras do protótipo têm teste executável: `node prototipos/fma-erp/tests/regras.test.cjs`.
