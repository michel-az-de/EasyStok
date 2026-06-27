# Baseline de Usabilidade do Admin — Fase 0

> **Issue:** #709 (parte de #708). **Status:** roteiro pronto para rodar.
> **Objetivo desta fase:** medir o **"antes"** com números honestos, antes de
> mexer em qualquer tela. Sem este baseline não há gate de melhoria confiável —
> qualquer ganho posterior vira opinião, não medição.
>
> **Quem roda:** Felipe (facilitador). **Com quem:** 2–3 operadores que já
> reclamaram do Admin. **Onde:** Admin **atual** em produção/sandbox, sem nenhuma
> mudança aplicada. **Quanto tempo:** ~30–40 min por operador.

---

## 1. Como conduzir

Regras do facilitador. Quebrar qualquer uma delas contamina o baseline.

- **Pensar-alto.** Peça ao operador para narrar em voz alta o que está
  procurando, o que espera que aconteça e onde fica em dúvida. O silêncio
  esconde o atrito.
- **Não ajudar.** Nada de "clica ali", "é no menu de cima", "tá quase". Se o
  operador pedir ajuda, anote como **pedido de ajuda** e responda só: *"o que
  você tentaria agora?"*. A ajuda só entra se ele estiver 100% travado e
  prestes a desistir — e isso conta como **tarefa não concluída sem ajuda**.
- **Cronometrar.** Comece o cronômetro quando ler a tarefa em voz alta e pare
  quando o operador disser "pronto" (mesmo que o resultado esteja errado —
  registre o erro à parte). Use o cronômetro do celular; anote em segundos.
- **Não explicar a tarefa duas vezes de jeitos diferentes.** Leia o **cenário**
  exatamente como está escrito. Reformular vira dica.
- **Um operador por vez, tela compartilhada ou ombro-a-ombro.** Grave a tela se
  o operador consentir (ajuda a recontar becos depois).
- **Conta como ambiente real:** use uma empresa/cliente de teste já existente no
  Admin (não crie atalhos nem deixe telas pré-abertas). O operador começa
  sempre do **Dashboard** (`/`).

**Definições de medição (valem para todas as tarefas):**

| Métrica | Como medir |
|---|---|
| Concluiu sem ajuda (S/N) | Chegou ao critério de sucesso **sem** nenhuma dica do facilitador. |
| Tempo (s) | Da leitura da tarefa até o operador declarar "pronto". |
| Becos / cliques errados | Toda navegação que ele desfez: abriu a tela errada, clicou em botão que não era, voltou. Conte 1 por desvio. |
| Pedidos de ajuda | Quantas vezes pediu orientação (mesmo que você não tenha dado). |
| Onde travou | Tela/elemento onde ficou parado >15s ou expressou confusão ("cadê isso?"). Anote o nome da tela. |

---

## 2. Tarefas-roteiro

Quatro tarefas reais sobre fluxos que os operadores de fato executam. Rode na
ordem. Se faltar tempo, as **T1, T2 e T3** são obrigatórias (cobrem os fluxos
mais citados); a **T4** é desejável.

> Antes de começar: prepare 1 cliente/empresa de teste com assinatura ativa e 1
> storefront ainda **não ativado**, para a T3 ter de onde partir.

### T1 — Suspender um cliente com motivo

**Cenário (ler em voz alta):**
> *"O cliente **[NOME DA EMPRESA DE TESTE]** está inadimplente há dois meses.
> Suspenda o acesso dele e registre o motivo 'inadimplência — 2 faturas em
> aberto'."*

**Critério de sucesso:** a assinatura do cliente fica com status **Suspensa** e
o motivo informado foi salvo/visível na ficha do cliente.

**Planilha de coleta:**

| Operador | Concluiu s/ ajuda (S/N) | Tempo (s) | Becos / cliques errados | Pedidos de ajuda | Onde travou |
|---|---|---|---|---|---|
| Op. 1 | | | | | |
| Op. 2 | | | | | |
| Op. 3 | | | | | |

### T2 — Emitir uma fatura avulsa

**Cenário (ler em voz alta):**
> *"O cliente **[NOME DA EMPRESA DE TESTE]** pediu uma cobrança extra de
> consultoria. Emita para ele uma **fatura avulsa** no valor de **R$ 250,00**,
> com a descrição 'Consultoria de configuração'."*

**Critério de sucesso:** existe uma fatura de origem **Avulsa**, valor R$ 250,00,
vinculada ao cliente certo, no status **Emitida** (não ficou em Rascunho).

**Planilha de coleta:**

| Operador | Concluiu s/ ajuda (S/N) | Tempo (s) | Becos / cliques errados | Pedidos de ajuda | Onde travou |
|---|---|---|---|---|---|
| Op. 1 | | | | | |
| Op. 2 | | | | | |
| Op. 3 | | | | | |

> **Atenção ao valor:** observe se o operador digita `250,00` ou `250.00` e se o
> sistema aceita. Anote em "onde travou" se o valor sair errado — é um ponto
> sensível conhecido em formulários de moeda do Admin.

### T3 — Configurar e **ativar** um storefront do zero

**Cenário (ler em voz alta):**
> *"Esse cliente vai começar a vender online. Configure a loja virtual dele e
> deixe-a **ativa e publicada** para receber pedidos."*

**Critério de sucesso:** o storefront do cliente passa de inativo para
**Ativo**. O operador deve descobrir sozinho os pré-requisitos de ativação
(ex.: ter pelo menos 1 item de cardápio visível, credencial de pagamento, 1
zona de frete) — **não** liste os pré-requisitos para ele.

**Planilha de coleta:**

| Operador | Concluiu s/ ajuda (S/N) | Tempo (s) | Becos / cliques errados | Pedidos de ajuda | Onde travou |
|---|---|---|---|---|---|
| Op. 1 | | | | | |
| Op. 2 | | | | | |
| Op. 3 | | | | | |

> Esta é a tarefa **mais estrutural** (várias telas, dependências escondidas).
> Anote com cuidado **qual pré-requisito** mais confunde — vira insumo direto
> para a escolha do piloto (#708).

### T4 — Responder e escalar um chamado de suporte

**Cenário (ler em voz alta):**
> *"Chegou um chamado de um cliente reclamando de um erro ao emitir nota.
> Responda ao cliente que a equipe vai investigar e **escale o chamado para o
> nível N2**."*

**Critério de sucesso:** o chamado recebeu uma resposta visível ao cliente e
teve o nível alterado para **N2**.

**Planilha de coleta:**

| Operador | Concluiu s/ ajuda (S/N) | Tempo (s) | Becos / cliques errados | Pedidos de ajuda | Onde travou |
|---|---|---|---|---|---|
| Op. 1 | | | | | |
| Op. 2 | | | | | |
| Op. 3 | | | | | |

---

## 3. Escala subjetiva pós-teste

Aplicar **logo após as tarefas**, com o operador ainda na frente do Admin. Duas
opções — use o **SUS** se quiser uma nota comparável a padrão de mercado; use os
**3 itens rápidos** se o tempo apertou. Não misture: escolha uma e use a mesma
com todos os operadores.

### Opção A — SUS (System Usability Scale, 10 itens)

Responder de **1 (discordo totalmente)** a **5 (concordo totalmente)**:

1. Eu usaria este painel com frequência sem problema.
2. Achei o painel desnecessariamente complicado.
3. Achei o painel fácil de usar.
4. Eu precisaria de ajuda de alguém para conseguir usar este painel.
5. As funções do painel estão bem integradas.
6. Achei o painel inconsistente (cada parte funciona de um jeito).
7. A maioria das pessoas aprenderia a usar este painel rapidamente.
8. Achei o painel travado / desajeitado de usar.
9. Me senti seguro(a) usando o painel.
10. Precisei aprender muita coisa antes de conseguir usar o painel.

> **Cálculo do SUS:** itens ímpares → (resposta − 1); itens pares → (5 − resposta).
> Some os 10 ajustados e multiplique por 2,5. Resultado de 0 a 100. Média de
> mercado ≈ 68. Anote a nota de cada operador na tabela do §5.

### Opção B — 3 itens rápidos (0–10)

Responder de **0 (muito difícil)** a **10 (muito fácil)**:

1. **Achar** — *"O quanto foi fácil **encontrar** onde fazer cada tarefa?"*
2. **Lembrar** — *"Se você precisasse refazer amanhã, o quanto seria fácil
   **lembrar** o caminho?"*
3. **Concluir** — *"No geral, o quanto foi fácil **concluir** o que pedi?"*

> Registre as 3 notas por operador. Baseline = média de cada item entre os
> operadores.

---

## 4. Sinal de suporte (dúvidas internas por semana)

O segundo número do baseline, além do teste com operadores, é **quanta dúvida o
Admin gera no dia a dia**. Precisa de uma fonte e uma contagem consistentes.

**Passo 1 — escolher a fonte (uma só, a que já existe hoje):**

- Grupo/conversa de WhatsApp da equipe onde operadores tiram dúvida; **ou**
- Mensagens diretas ao Felipe; **ou**
- Chamados de suporte com categoria **Dúvida** no próprio Admin (módulo de chamados).

> Decida **qual** é a fonte canônica e quem é a pessoa que hoje responde essas
> dúvidas (o "fielder"). Se houver mais de um canal, escolha o de maior volume —
> não tente somar canais diferentes (vira maçã com laranja).

**Passo 2 — definir o que conta como "dúvida de Admin":**

> Conta: qualquer pergunta de "como faço X no painel", "onde fica Y", "por que
> não consigo Z". **Não** conta: bug confirmado, pedido de feature, conversa
> sobre o negócio do cliente.

**Passo 3 — contar por semana:** registre, por **4 semanas** se possível, o nº de
dúvidas de Admin na fonte escolhida. O baseline é a **média semanal**. Anote
também os **2–3 temas mais repetidos** (ex.: "como ativar loja", "onde suspende
cliente") — isso confirma ou contradiz o que o teste de tarefas mostrou.

| Semana | Período | Nº de dúvidas de Admin | Temas mais repetidos |
|---|---|---|---|
| S1 | | | |
| S2 | | | |
| S3 | | | |
| S4 | | | |
| **Média semanal (baseline)** | | | |

---

## 5. Tabela de registro do baseline

Consolidação final dos números do "antes". Preencher após rodar §2, §3 e §4.
É **este** quadro que destrava as Fases 1–2.

### Por tarefa (consolidado dos operadores)

| Tarefa | % concluiu sem ajuda | Tempo médio (s) | Média de becos | Total pedidos de ajuda | Ponto de trava mais comum |
|---|---|---|---|---|---|
| T1 — Suspender cliente | | | | | |
| T2 — Fatura avulsa | | | | | |
| T3 — Ativar storefront | | | | | |
| T4 — Responder/escalar chamado | | | | | |

### Escala subjetiva

| Operador | SUS (0–100) | Achar (0–10) | Lembrar (0–10) | Concluir (0–10) |
|---|---|---|---|---|
| Op. 1 | | | | |
| Op. 2 | | | | |
| Op. 3 | | | | |
| **Média** | | | | |

### Sinal de suporte

| Métrica | Valor |
|---|---|
| Fonte escolhida | |
| Média de dúvidas de Admin / semana | |
| Temas mais repetidos | |

---

## 6. Gate go / no-go

O baseline está **completo** (pronto para destravar a comparação das Fases 1–2 de
#708) quando **todas** estas condições forem verdade:

- [ ] **Cobertura:** as tarefas **T1, T2 e T3** foram rodadas com **pelo menos
      2 operadores** (3 é o ideal), cada um com a planilha do §2 preenchida.
- [ ] **Números do "antes" registrados:** a tabela do §5 (por tarefa + escala
      subjetiva) está preenchida — não há célula em branco nas três tarefas
      obrigatórias.
- [ ] **Sinal de suporte definido:** a fonte está escolhida e há **pelo menos a
      média de 1 semana** contada (4 semanas é o alvo, 1 é o mínimo para o gate).
- [ ] **Piloto escolhido:** com os dados acima, ficou escolhido o
      **procedimento-piloto** — recomendação: o fluxo **mais citado** que também
      tenha **maior complexidade estrutural** (forte candidato: ativar
      storefront, T3).
- [ ] **Triagem do piloto feita:** os atritos do piloto foram classificados em
      **REMOVER** (passos a mais, telas redundantes) vs **EXPLICAR** (jargão,
      falta de orientação na tela).

**Decisão:**

- **GO** → todos os itens marcados. O baseline vira a linha de comparação. Toda
  melhoria posterior (o piloto da Fase 2) será medida **contra estes números**.
- **NO-GO** → algum item em aberto. Falta medição honesta do "antes" — sem ela,
  não há como provar ganho depois.
