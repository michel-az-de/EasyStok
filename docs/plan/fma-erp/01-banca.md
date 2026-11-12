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

## Prova da v2 (substituída pela v3)

- `node prototipos/fma-erp/tests/regras.test.cjs`: 4 testes, todos passando (fluxo completo com
  retenção, cancelamento e substituta, nova versão, cancelamento de contrato).
- Navegador: fluxo L2 → proposta → contrato → assinatura → etapa → NFS-e com retenção de R$ 82,50
  → líquido R$ 1.417,50; erros de valor, arquivo e divergência exibidos no campo; 375 px sem
  rolagem horizontal. Prints em `prints/`.

## v3: reprovação do Felipe e redesenho

Veredito do Felipe sobre a v2: visual quadrado e com cara de CRUD; pediu UX de verdade e integração
com e-mail, WhatsApp e LLM. Resposta da v3:

| Achado | Resposta |
|---|---|
| Cara de CRUD | Telas viram fila de decisão: Hoje (briefing da IA, "Pede você", caixa, "Feito sozinho"), Conversas, Negócios em kanban |
| Visual quadrado | Raios de 12 a 32 px, sombras suaves, trilho navy com marcador mostarda, paleta FMA |
| Sem integração | Caixa de conversas WhatsApp e e-mail, rascunho e proposta montados pela IA, automações, copiloto; tudo **simulado** e marcado como tal |
| Acoplamento | Regras isoladas em `dominio.js` (sem DOM); `app.js` só desenha |

## Prova da v3 (substituída pela v4)

- `node prototipos/fma-erp/tests/regras.test.cjs`: 7 testes, todos passando.
- Navegador, sem erro no console: conversa → proposta montada pela IA → envio → aceite → contrato
  assinado ("Gama Varejo agora é cliente") → etapa aprovada gera parcela de R$ 1.200,00 → NFS-e sem
  arquivo recusada ("Anexe o XML e o PDF da NFS-e.") → retenção de R$ 82,50 → líquido R$ 1.117,50 →
  cobrança Pix simulada → recebido.
- 375 px sem rolagem horizontal em todas as telas. Prints da v3 substituídos pelos da v4.

## v4: reprovação da v3 e análise do contexto

Veredito do Felipe sobre a v3: "muito carregado, muita funcionalidade". Pediu publicar e gerenciar
artigos e vídeos no LinkedIn e Instagram e uma leitura melhor do contexto dele.

| Achado | Resposta |
|---|---|
| Carregado demais | 9 áreas viram 4: Conteúdo, Contatos, Contratos, Contas |
| Contexto ignorado | A FMA vende pela autoridade do Felipe; Conteúdo vira a tela inicial |
| Redes sociais | Calendário, rascunho da IA, aprovação e agendamento via Metricool (simulado), métricas e leads por post |
| Risco de banimento | Nada de automação não oficial; abordagem limitada a 5 por dia, enviada pelo próprio Felipe |
| Excesso de IA | Copiloto, Ctrl+K, kanban, gráfico e automações cortados |

## Prova da v4

- `node prototipos/fma-erp/tests/regras.test.cjs`: 10 testes, todos passando.
- Navegador, sem erro no console: ideia → rascunho da IA → aprovado → Instagram em post de texto
  recusado no campo → data passada recusada → agendado → publicado. Comentário vira lead, comentário
  frio é ignorado, 6ª abordagem do dia bloqueada ("Você já mandou 5 mensagens hoje").
- Conversa → proposta P103 (R$ 7.500,00) → aceite → contrato C105 assinado ("Gama Varejo agora é
  cliente"). Contrato C1: NFS-e com XML e PDF e retenção de R$ 82,50 → líquido R$ 1.917,50 →
  cobrança Pix simulada → recebido.
- 375 px sem rolagem horizontal nas 8 telas; corrigido o card de "Pede você hoje" espremido no celular.

| Print | Tela |
|---|---|
| `01-conteudo.jpg` | Conteúdo: pede você hoje, calendário, em produção, publicados |
| `02-post-erro-instagram.jpg` | Editor do post com erro de Instagram só texto |
| `03-contatos.jpg` | Contatos: chegou dos posts, leads e conversas |
| `04-abordar-limite.jpg` | Abordagem bloqueada no limite de 5 por dia |
| `05-proposta-da-conversa.jpg` | Proposta montada a partir da conversa |
| `06-contrato-nfse.jpg` | Contrato com NFS-e anexada e líquido |
| `07-contas.jpg` | Contas com recebimento registrado |
| `08-celular-conteudo.jpg` | Conteúdo a 375 px |
| `09-celular-post.jpg` | Post publicado a 375 px |
