# Plano — ERP da Casa da Baba (front único, sala de módulos)

> **Levantamento atualizado em 09/10/2026:** [inventário, evidências e plano de continuidade em ondas](11-levantamento-e-ondas-2026-10-09.md). Use esse levantamento para priorizar a execução. As especificações M0–M8 abaixo continuam como referência, com as correções do ADR-0059. Os estados históricos desta página não comprovam merge, publicação ou homologação atuais.

Issue: #1316 · Decisão: [ADR-0056](../../adr/0056-erp-da-casa-da-baba-front-unico.md) · Data: 2026-10-01
Base medida: master `0a70eb56` (estudo da #1316). Plano irmão: [atendimento-whatsapp](../atendimento-whatsapp/README.md)
(dono de S01–S53 e F01–F18; este plano referencia aquelas fatias e não as reescreve).

## 1. O que vamos construir

```
              ┌──────────┐
              │  Login   │  e-mail e senha ou Google (#1325), tenant fixo
              └────┬─────┘
                   │ perfil decide a porta de entrada        entregador: sem login,
      ┌────────────┼──────────────┐             link da viagem no WhatsApp
      ▼            ▼              ▼
 Sala de       M3 Balcão      M4 Cozinha
 módulos       (Atendimento)  (tablet, sessão longa)
 (Dona)        perfil Atend.  perfil Cozinha
      │
      ▼  card liberado/bloqueado por perfil × módulo
 ┌────┬────┬────┬────┬────┬────┬────┬────┐
 │ M1 │ M2 │ M3 │ M4 │ M5 │ M6 │ M7 │ M8 │  cada módulo: menu lateral + atalhos "outras telas"
 └────┴────┴────┴────┴────┴────┴────┴────┘
```

| Módulo | Telas (rascunho do Felipe + o que já existe) | Backend hoje | Doc |
|---|---|---|---|
| M0 Fundação | login, sala de módulos, shell, tema da marca, perfis × módulos, tenant fixo, poda | permissões parciais | [01-fundacao.md](01-fundacao.md) |
| M1 Cardápio | produtos, categorias, preços, combos, disponibilidade | vivo, sem combos | [02-m1-cardapio.md](02-m1-cardapio.md) |
| M2 Produção | insumos, fichas técnicas, planejamento, produção do dia, perdas | parcial | [03-m2-producao.md](03-m2-producao.md) |
| M3 Atendimento | balcão (conversas), pedidos, clientes, canais, status, histórico | vivo | [04-m3-atendimento.md](04-m3-atendimento.md) |
| M4 Cozinha | fila de preparo, prioridades, itens em produção, tempo de preparo, expedição | vivo | [05-m4-cozinha.md](05-m4-cozinha.md) |
| M5 Caixa | abertura/fechamento, pagamentos, sangrias, movimentações, relatórios | vivo | [06-m5-caixa.md](06-m5-caixa.md) |
| M6 Campanhas | promoções, cupons, CRM, mensagens, resultados | parcial | [07-m6-campanhas.md](07-m6-campanhas.md) |
| M7 Configurações | usuários, permissões, integrações, impressão, parâmetros gerais | parcial | [08-m7-configuracoes.md](08-m7-configuracoes.md) |
| **M8 Entregas** | janelas, áreas, entregadores, viagens, chamados (Lalamove) | vivo, Lalamove ausente | [09-m8-entregas.md](09-m8-entregas.md) |
| Site e impressos | casadababa.com com a marca; impressos S49–S53 | vivo | [10-site-e-impressos.md](10-site-e-impressos.md) |

Fica no `EasyStock.Web` (bastidor, ADR-0056 item 7): gestão e histórico de estoque e lotes, compras e
fornecedores, financeiro (contas a pagar e a receber), etiquetas de lote, relatórios gerenciais. A operação
do dia (saldo, lote em porções, desacerto, produção, perda) vai para o console no M2.

> **Emendado em 08/10 pelo [ADR-0059](../../adr/0059-web-sai-pwa-continua-e-merge-de-conflitos.md).**
> O `EasyStock.Web` sai inteiro: todas as áreas acima vão para o console, e as telas antigas servem só
> de referência de regra. A PWA de produção e cadastro continua junto ao console como backup offline, e
> conflito entre os dois vai para um módulo de merge decidido pela dona. DM7-1, D-M5-02, D-M5-07 e a
> Fase B precisam ser revistas à luz disso.

## 2. Princípios (vinculantes para toda fatia)

1. **O jeito de operar é o do protótipo.** Atendimento é o cockpit (D5); avisa, não trava (D7);
   nada dispara sem marcação da dona (D8); janela é capacidade (D9); pedido antes do pagamento
   (RN-23). Fonte: `C:\rep\casa-da-baba-atendimento\prototipo-omni` e `Downloads/02`–`03`.
2. **Regra na API, tela só mostra** (ADR-0054 item 2). Permissão: a tela esconde, a API nega.
3. **Roupa da Casa da Baba.** Tokens do design-system-v1 (`--cdb-*`): Papel `#FEFEFE`, Creme
   `#FFF8E8`, Cacau `#422814`, Caramelo `#A25803`, Trigo `#E3A542`, Linha `#E5D5BE`; Lora nos
   títulos, Nunito Sans no texto; contraste ≥ 4,5:1; fonte grande. A camada de tela está em M0.1.
4. **Tenant fixo.** Nenhuma tela pergunta empresa. `EmpresaId` e RLS continuam em toda tabela nova.
5. **Reaproveitar antes de criar.** Toda fatia cita o que já existe (path) antes de propor entidade.
6. **Uma PR, uma funcionalidade** (ADR-0054 item 5): endpoint e tela juntos quando couber.

## 3. Ordem de execução

```
Fase 0  Go-live do atendimento: F16 (#1304), F13 (#1310)         ← plano irmão, já em curso
Fase A  M0 Fundação: (M0.1 tema ∥ M0.3 perfis × módulos) → M0.2 shell/sala → M0.4 tenant fixo + poda → M0.5 drop
Fase B  M5 Caixa (F14 + relatórios) → aposenta a PWA do caixa
Fase C  M1 Cardápio, M2 Produção
Fase D  M3/M4 fechamentos (F09–F12), M8 Entregas (F17)
Fase E  M6 Campanhas (F15 + promoções), M7 Configurações
Fase F  Site casadababa.com com a marca; impressos restantes (S50, S51, backlog)
```

A Fase A destrava todas as outras. B a F podem correr em paralelo depois de A, uma fatia por
worktree, respeitando os worktrees já ativos de outras sessões (ex.: `console-caixa-1244`,
`console-ficha-1239`).

## 4. Convenções do executor

Valem as do plano irmão ([README §Convenções](../atendimento-whatsapp/README.md)), com estes ajustes:

- **Fatias `Mx.y`** (x = módulo, y = ordem). Tier pelo ADR-0055: **baixo**, salvo migration, RLS,
  auth, permissão ou policy, que é **alto**.
- Fatia de console toca `EasyStock.Console/` e passa por `npm run qualidade`; fatia de backend passa
  pelo `gate.ps1`. Fatia mista passa pelos dois.
- Toda fatia termina com: aceite marcado, `changelog.d/<issue>.md`, e a linha dela neste README
  atualizada.

## 5. Decisões pendentes do Felipe (consolidado)

Detalhe e alternativas no fim de cada doc. A recomendação está entre parênteses. **Bloqueiam a Fase A**
as marcadas com ⚑ (todas respondidas em 01/10, ✅); as demais só travam a fatia do próprio módulo.

| # | Pergunta | Recomendação | Doc |
|---|---|---|---|
| ✅ D-01 | Perfis e matriz perfil × módulo | **DECIDIDO:** 3 perfis (Dona, Atendimento, Cozinha) | [01](01-fundacao.md) |
| ✅ D-02 | Nome na tela | **DECIDIDO:** "Casa da Baba · EasyStok" | [01](01-fundacao.md) |
| ✅ D-03 | Modo escuro | **DECIDIDO:** entra em tudo, logo em placa clara | [01](01-fundacao.md) |
| ✅ D-04 | Dados das outras empresas no banco | **DECIDIDO:** apagar na M0.5, com backup | [01](01-fundacao.md) |
| ✅ D-05 | Sessão do tablet da cozinha | **DECIDIDO:** sessão longa só na Cozinha | [01](01-fundacao.md) |
| D-M1-01 | Dono do preço e da categoria | o cardápio | [02](02-m1-cardapio.md) |
| D-M1-02 | O que é combo | preço fixo, componentes fixos que baixam estoque | [02](02-m1-cardapio.md) |
| D-M1-03 | Porção com saldo próprio | por `ProdutoVariacao` | [02](02-m1-cardapio.md) |
| D-M1-04 | Adicional | item do cardápio ligado ao prato | [02](02-m1-cardapio.md) |
| D-M1-05 | "Fora de hoje" volta sozinho | não, só manual | [02](02-m1-cardapio.md) |
| D-M1-06 | Fonte dos alérgenos | lista fechada no item + "outros" | [02](02-m1-cardapio.md) |
| D-M1-07 | Tirar do cardápio apaga | não, arquiva | [02](02-m1-cardapio.md) |
| D-M1-08 | Item novo em validação (RN-15) | sim | [02](02-m1-cardapio.md) |
| D-M2-01 | Produção baixa insumo pela receita | só produto marcado; avisa, não trava | [03](03-m2-producao.md) |
| D-M2-02 | Perda exige motivo | lista; texto só em "Outro" | [03](03-m2-producao.md) |
| D-M2-03 | Onde a embalagem desce | na produção, pela receita | [03](03-m2-producao.md) |
| D-M2-04 | Destino da porção no lote | vem da linha do item (RN-17) | [03](03-m2-producao.md) |
| D-M2-05 | Sugestão de produção | mínimo − saldo + agendados + descoberto | [03](03-m2-producao.md) |
| D-M2-06 | Quem lança produção e perda | perfis do M2; perda grande pede Gerente | [03](03-m2-producao.md) |
| D3-01 | Configuração do atendimento | abrir/fechar loja no M3, resto no M7 | [04](04-m3-atendimento.md) |
| D3-02 | iFood | fora até ter conta e volume | [04](04-m3-atendimento.md) |
| D3-03 | Atendentes múltiplos | só filtro Minhas/Sem dono e nome no selo | [04](04-m3-atendimento.md) |
| ✅ D3-04 | Quem cancela pedido pago | **DECIDIDO em 09/10:** Atendimento sem pagamento; com qualquer valor recebido, só Dona/gerente | [04](04-m3-atendimento.md) |
| D4-01 | Prioridade da fila | derivada do prazo, sem campo | [05](05-m4-cozinha.md) |
| D4-02 | Rastreio por item | soma por item; status por pedido | [05](05-m4-cozinha.md) |
| D4-03 | Encomendas na cozinha | Hoje/Amanhã/dia; futuro só leitura | [05](05-m4-cozinha.md) |
| D4-04 | Tempo real alimenta o previsto | painel sugere, dona confirma | [05](05-m4-cozinha.md) |
| D-M5-01 | O que o fechamento confere | só o dinheiro da gaveta | [06](06-m5-caixa.md) |
| D-M5-02 | Caixa da PWA na transição | só leitura após M5.1 e M5.2 | [06](06-m5-caixa.md) |
| D-M5-03 | Sem internet | console online; lança depois | [06](06-m5-caixa.md) |
| D-M5-04 | `/caixa` do Web | sai na M5.9 | [06](06-m5-caixa.md) |
| D-M5-05 | Pagamentos antigos "dinheiro" da PWA | aparecem como "não informado" | [06](06-m5-caixa.md) |
| D-M5-06 | Maquininha e vale-refeição | crédito/débito na hora; vale vira "outro" | [06](06-m5-caixa.md) |
| D-M5-07 | Push da PWA | morre; console avisa por som e SSE | [06](06-m5-caixa.md) |
| D-M5-08 | Reabrir fechamento | Gerente reabre com motivo, histórico preservado | [06](06-m5-caixa.md) |
| DM6-1 | Modelo da promoção | preço promocional por item com período | [07](07-m6-campanhas.md) |
| DM6-2 | Cupom sobre item em promoção | não acumula | [07](07-m6-campanhas.md) |
| DM6-3 | Promoção no site | "de R$ X por R$ Y" com data de fim | [07](07-m6-campanhas.md) |
| DM6-4 | Conversão no resultado | pedido pago em até 7 dias | [07](07-m6-campanhas.md) |
| DM6-5 | Nome do cupom do cliente | `Cupom`, depois da poda M0.4 | [07](07-m6-campanhas.md) |
| DM6-6 | Quem opera o M6 | dona e quem ela liberar | [07](07-m6-campanhas.md) |
| DM7-1 | Parâmetros de estoque | ficam no Web; M7 só aponta | [08](08-m7-configuracoes.md) |
| DM7-2 | Papel padrão de impressão | por dispositivo | [08](08-m7-configuracoes.md) |
| DM7-3 | Usuário novo | decidido em 01/10: convite com link por e-mail e WhatsApp, a pessoa cria a própria senha | [08](08-m7-configuracoes.md), [N9](../notificacoes-sistema/06-acesso.md) |
| DM7-4 | Templates de aviso ao cliente | sem tela por ora | [08](08-m7-configuracoes.md) |
| DM7-5 | Campos fiscais da vitrine | remover na poda M0.4 | [08](08-m7-configuracoes.md) |
| DM7-6 | Mensagem "fora da área" | a do atendimento (S08) é a única | [08](08-m7-configuracoes.md) |
| ✅ D8-01 | Acesso do entregador | **DECIDIDO:** link por viagem pelo WhatsApp, sem login | [09](09-m8-entregas.md) |
| D8-02 | Encomenda até quantos dias | 60 (limite que o código já tem) | [09](09-m8-entregas.md) |
| D8-03 | Pagamento da encomenda | na hora, como o pedido do dia | [09](09-m8-entregas.md) |
| D8-04 | Quem marca "entregue" (motoboy da casa) | o entregador; a dona desfaz | [09](09-m8-entregas.md) |
| D8-05 | Capacidade da janela com as duas linhas | conta pedidos até a M4.4 medir tempos | [09](09-m8-entregas.md) |
| D-SI1 | Publicação do site | script no repo do site, padrão `vps-deploy.sh`, rodado pelo Felipe | [10](10-site-e-impressos.md) |
| D-SI2 | Pedido do site fora da área | não registra; abre o WhatsApp; ponte #1308 sai | [10](10-site-e-impressos.md) |
| D-SI3 | Ativos que o DS v1 não tem | pedir DS v1.1 | [10](10-site-e-impressos.md) |
| D-SI4 | Raster do cupom 58 mm | na API (PNG 1 bit) | [10](10-site-e-impressos.md) |

Já decididas em 01/10: D-SI5 (site volta refeito por mockup, manutenção intencional) e o site como canal
de atendimento (SI.6).

## 6. Estado das fatias

Atualizado por cada PR. Legenda: ⬜ sem issue · 🟡 em PR · ✅ mergeada. 78 fatias.

| Doc | Fatias | Tier alto |
|---|---|---|
| [01 M0 Fundação](01-fundacao.md) | M0.1–M0.5 | M0.3, M0.4, M0.5 |
| [02 M1 Cardápio](02-m1-cardapio.md) | M1.1–M1.8 | ver doc |
| [03 M2 Produção](03-m2-producao.md) | M2.1–M2.7 | ver doc |
| [04 M3 Atendimento](04-m3-atendimento.md) | M3.1–M3.9 | ver doc |
| [05 M4 Cozinha](05-m4-cozinha.md) | M4.1–M4.7 | ver doc |
| [06 M5 Caixa](06-m5-caixa.md) | M5.1–M5.9 | M5.4, M5.9 |
| [07 M6 Campanhas](07-m6-campanhas.md) | M6.1–M6.7 | M6.3 |
| [08 M7 Configurações](08-m7-configuracoes.md) | M7.1–M7.6 | M7.2, M7.3 |
| [09 M8 Entregas](09-m8-entregas.md) | M8.1–M8.6 | M8.3 |
| [10 Site e impressos](10-site-e-impressos.md) | SI.1–SI.14 | SI.1 |

| Fatia | Título | Estado | Issue/PR |
|---|---|---|---|
| — | ADR-0056 e este plano | 🟡 | #1316 |
| M0.2 (parcial) | Hall de módulos, menu do módulo pela rota (`#/m/<modulo>/<tela>`) e abas da Gestão no módulo dono; sem login de um passo nem permissão por perfil (dependem da M0.3) | 🟡 | #1447 |
