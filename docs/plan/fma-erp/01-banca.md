# Banca revisora do protótipo FMA Gestão (#2387)

- Data: 2026-11-12
- Formato: 3 revisores independentes, em paralelo, cada um instruído a **refutar** a v1 (spec + protótipo).
- Total: 42 achados. Corrigidos na v2 ou incorporados à spec, salvo os marcados como adiados.

## Revisor 1: arquitetura e tenant

Veredito: a recomendação B da v1 **só se sustenta em parte**.

| Achado | Destino |
|---|---|
| Risco da B para a CdB é Médio, não Baixo: migrações e enum entram no release da CdB | Spec 2.2 corrigida |
| Seeds e jobs da CdB (`PerfisCasaDaBabaSeed`, `NotificacoesGlobaisSeed`, `MobileSchemaInitializer`, jobs de fundo) rodariam na instância FMA | Checklist na spec 2.3 |
| Cliente PJ conflita com a poda B2B do ADR-0056 | Spec 2.1 |
| `TenantFeatureFlag` não é lido em runtime: "desligado por flag" não existe | Spec 2.1 e 2.3 |
| ADR-0056 e ADR-0042 ainda Propostos | Ressalva na spec 2.1 |
| Front não decidido | Linha na tabela 2.1 |
| `OrigemContaFinanceira` exige migração; Lembrete acoplado | Marcados "Adaptar" |
| Faltava a opção D: fork enxuto em repo próprio | Spec 2.2; recomendação passa a D, com B como alternativa |

## Revisor 2: produto, UX e acessibilidade

| Achado | Correção na v2 |
|---|---|
| Valor inválido virava NaN; "1500.50" lido errado | `lerValor` aceita 1.500,50 / 1500,50 / 1500.50 e recusa ambíguo; erro no campo |
| Becos sem saída: proposta enviada sem ajuste, contrato sem cancelamento, entrega sem "pedir ajuste", lead perdido sem volta | `novaVersao`, `cancelarContrato`, `pedirAjuste`, `reabrirLead` |
| Lembretes órfãos (follow-up seguia aberto após aceite) | Chave estável por lembrete; o fluxo fecha pela chave |
| Sem formulário de lead, custo e conta a pagar | Formulários com validação por campo e `role=alert` |
| Ações irreversíveis sem confirmação | `confirm` em aceite, recusa, perda, cancelamentos e restauração |
| Cliente nascia no aceite | Cliente nasce na assinatura |
| Foco perdido ao trocar de tela; skip link quebrado | `h1` recebe foco; skip link foca o `h1` |
| Esteira sem semântica | `<ol>` com `aria-current` e texto para leitor de tela |
| Botões pequenos (36 px), botões repetidos sem nome distinto | 44 px e `aria-label` com o alvo |
| Anel de foco mostarda some no fundo claro | Anel navy com halo mostarda |
| KPI não clicável; formulário da NFS-e apertado | KPI vira link; formulário em grade responsiva |
| Toast vazio aparecia no canto | Escondido com `visibility` |

## Revisor 3: financeiro e fiscal (13 achados)

| Severidade | Achado | Destino |
|---|---|---|
| Crítico | Retenções ignoradas: bruto tratado como líquido | v2 separa bruto, retenções e líquido |
| Alto | Bloqueio rígido sem saída para nota errada | Mensagem orienta corrigir ou cancelar; `cancelarNota` com substituta |
| Alto | Sem cancelamento e substituição | `cancelarNota`; nota nova registra `substitui` |
| Alto | Competência ausente | Campo obrigatório, padrão = mês da aprovação |
| Alto | Sem provisão de imposto | Lembrete `imposto:AAAA-MM` |
| Médio | XML e PDF opcionais | Ambos obrigatórios |
| Médio | Recebimento sem valor pago | `valorPago` registrado |
| Médio | Contrato encerrava com etapa paga sem nota | Encerra só com todas pagas e com nota |
| Médio | Sinal e parcelamento dentro da etapa | **Adiado**: a confirmar com o contador |
| Médio | Multa, juros e reajuste | **Adiado**: fora do protótipo |
| Médio | Fato gerador (aprovação ou entrega) | **Adiado**: a confirmar com o contador |
| Baixo | Município e campos de retenção por tipo | Na spec (NfseAnexada) |
| Baixo | RLS por tabela nova | Invariante 6 da spec |

## Prova da v2

- `node prototipos/fma-erp/tests/regras.test.cjs`: 4 testes, todos passando (fluxo completo com
  retenção, cancelamento e substituta, nova versão, cancelamento de contrato).
- Navegador: fluxo L2 → proposta → contrato → assinatura → etapa → NFS-e com retenção de R$ 82,50
  → líquido R$ 1.417,50; erros de valor, arquivo e divergência exibidos no campo; 375 px sem
  rolagem horizontal. Prints em `prints/`.
