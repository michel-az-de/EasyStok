# Voz e Copy — EasyStock.Admin

> Guia canônico de escrita para o painel administrativo (back-office SaaS).
> Vincula todo texto exibível: rótulos, botões, mensagens de erro, estados vazios,
> ajuda inline e microcopy. Quando houver dúvida de redação, este documento decide.
>
> Público: time interno do EasyStock e donos de pequenos negócios (massas artesanais,
> padarias, lanchonetes) que operam o painel sem treinamento formal.

---

## 1. Princípios de voz

O Admin fala como um **colega experiente que senta do seu lado** — não como um manual
técnico nem como um robô. Cinco princípios, em ordem de prioridade:

1. **Clara antes de esperta.** A pessoa do outro lado está resolvendo um problema real
   (uma nota que não saiu, um cliente esperando). Não há espaço para piada, trocadilho
   ou tom "divertido". Frase curta, voz ativa, sujeito explícito.
   - Ruim: "Ops! Algo deu errado por aqui."
   - Bom: "Não foi possível salvar o cliente. Revise o telefone e tente de novo."

2. **Pedagógica, não condescendente.** Explique o *porquê* quando ajuda a pessoa a agir,
   sem soar como aula. Uma frase de contexto vale mais que um manual.
   - Ruim: "Habilitação negada."
   - Bom: "Para emitir nota, primeiro envie o certificado A1 e configure o CSC."

3. **Sem jargão não explicado.** Termos de SaaS e fiscais (MRR, churn, CSC, dunning,
   tenant) são opacos para o dono da loja. Ou troque pelo equivalente do dia a dia,
   ou explique na primeira aparição com um `<es-help>`. Sigla nova SEMPRE expande na
   estreia: "CSC (Código de Segurança do Contribuinte)".

4. **Tom para pequenos negócios.** Trate por "você". Fale de "sua loja", "seus clientes",
   "sua nota". Nada de "o usuário", "a entidade", "o registro" na superfície visível —
   esses são termos de bastidor (ver seção 2). Reserve o "nós" para quando o sistema
   age: "Enviamos o convite", "Geramos a fatura".

5. **Honesta sobre o estado.** Se algo está carregando, vazio, bloqueado ou falhou,
   diga exatamente o que é — sem mascarar com otimismo vago. "Erro de conexão" não
   diz nada; "Não conseguimos falar com o servidor" diz, e "Tente de novo em instantes"
   dá a saída.

**Pessoa e tempo verbal.** Imperativo para o que a pessoa faz ("Salve", "Revise",
"Tente de novo"); presente para o que o sistema faz ("Enviamos", "Geramos").
Evite gerúndio empilhado e voz passiva sem agente ("foi processado" → "processamos").

**O que NÃO fazer:**
- Não use emoji em copy de produto.
- Não use ALL CAPS para ênfase (parece grito; quebra leitores de tela).
- Não termine erro em "..." reticências de suspense. Reticência só em estado de
  progresso ("Enviando…").
- Não culpe a pessoa ("Você esqueceu de…"). Descreva o que falta ("Falta o telefone").

---

## 2. Terminologia canônica

Uma entidade, vários nomes. A regra: **o que aparece na tela** (coluna UI) é o que a
pessoa lê; **o termo de bastidor** vive em código, logs, rotas de API e nomes de
entidade — nunca vaza para a superfície. Fonte: glossário de domínio do EasyStock
(`EasyStock.Admin/Glossario/GlossarioTermos.cs`).

| Termo na UI (visível)      | Termo de bastidor (código/log/API)              | Quando usar / nota |
|----------------------------|-------------------------------------------------|--------------------|
| **Cliente**                | `Empresa` / `tenant` / `EmpresaId`              | A loja que assina a plataforma. "Tenant" só em logs e rotas (`api/admin/tenants`). Na tela: **Cliente**. |
| **Comprador** / **Cliente da loja** | `Cliente` (entidade ERP)               | Quem compra do nosso cliente (no ERP/storefront). Desambigue do anterior pelo contexto da tela; se houver risco de confusão, use "comprador". |
| **Plano**                  | `Plano`                                         | Produto SaaS com preço e limites. Igual em UI e código. |
| **Assinatura**             | `AssinaturaEmpresa`                             | Contrato Cliente + Plano. Status visível: **Ativa, Suspensa, Cancelada, Expirada**. |
| **Teste grátis**           | `Trial` / `TrialFim`                            | Na tela escreva **teste grátis** ou "período de teste". "Trial" fica no código. |
| **Suspensa (por falta de pagamento)** | dunning / `Suspender()`              | Nunca exiba "dunning". Diga o motivo: "Suspensa por falta de pagamento". |
| **Fatura**                 | `Fatura`                                        | Documento de cobrança. Igual em UI e código. |
| **Cobrança**               | `CobrancaAssinatura` (legado)                   | Evite expor a distinção legado/novo na UI; para a pessoa é só "cobrança" ou "fatura". |
| **Cupom**                  | `Cupom`                                         | Código de desconto. Igual. |
| **Chamado**                | `AdminTicket` / `Ticket`                        | Ver decisão abaixo. **Na UI: Chamado.** |
| **Prazo de atendimento**   | `SLA` / `SlaConfiguracao`                       | "SLA" só em telas técnicas de configuração interna; na operação diária prefira **prazo de atendimento / prazo de resposta**. |
| **Nível de atendimento**   | `NivelAtendimento` (N1–N4)                      | N1–N4 são níveis de escalação, **não** prioridades. Não confunda com Prioridade do chamado. |
| **Loja virtual** / **Vitrine** | `Storefront`                              | A loja pública do cliente. Na tela: **loja virtual** ou **vitrine**. "Storefront" só no código. |
| **Cardápio** / **Item**    | `CardapioItem`                                  | Lista pública de itens vendáveis. Igual. |
| **Dispositivo**            | `MobileDevice`                                  | App móvel pareado. "Dispositivo" na UI. |
| **Acessar como** / **Entrar como cliente** | impersonação / `AdminImpersonationLog` | Nunca exiba "impersonação" (jargão). Use **Acessar como** ou "Entrar como este cliente". |
| **Nota fiscal (NFC-e)**    | `NFC-e` / `EmpresaConfiguracaoFiscal`           | Na estreia expanda: "NFC-e (nota fiscal do consumidor)". Depois pode abreviar. |
| **Código de segurança (CSC)** | `CSC` / `CscToken`                           | Expanda na estreia: "CSC (Código de Segurança do Contribuinte, fornecido pela SEFAZ)". |

### Decisão: "Chamado" vs "Ticket"

**Veredito: na UI, sempre "Chamado". "Ticket" fica restrito a código, logs e nomes de
entidade (`AdminTicket`, `TicketStatus`).**

Justificativa:
- **Público.** O dono de uma fábrica de massas abre um "chamado" com o suporte — é a
  palavra do português corrente para pedido de suporte. "Ticket" é anglicismo de
  software; soa a sistema de TI, não a atendimento.
- **Consistência com o princípio 3** (sem jargão não explicado): "chamado" não precisa
  de glossário; "ticket" precisaria.
- **Custo zero no código.** A entidade segue `AdminTicket`; só a camada de exibição muda.
  Não há renomeação de tabela, rota ou enum — apenas rótulos e copy.
- **Coerência interna.** Status já são em pt-BR (Aberto, Em atendimento, Resolvido);
  "Chamado" fecha o conjunto. "Ticket Aberto" mistura idiomas.

Uso: "Abrir chamado", "Chamados em aberto", "Este chamado foi resolvido". O badge de
contagem do dashboard conta "chamados em aberto".

---

## 3. Padrão de erro: causa + próximo passo

Todo erro visível responde duas perguntas, nesta ordem: **o que aconteceu** (causa, em
linguagem da pessoa) e **o que fazer agora** (próximo passo acionável). Sem a segunda
parte, o erro é um beco sem saída.

**Fórmula:** `[Causa concreta]. [Ação que a pessoa pode tomar].`

Regras:
- Nada de código de stack, exceção crua ou `ex.Message` jogado na cara. Logue o detalhe
  técnico; mostre a versão humana. (Hoje há páginas que concatenam `{ex.Message}` no
  texto visível — isso é débito a corrigir, não padrão a copiar.)
- Se a ação é "tentar de novo", diga *quando* faz sentido ("em instantes") e o que fazer
  se persistir ("contate o suporte").
- O tom não acusa. Foco no que destrava, não em quem errou.

### Exemplos reescritos (a partir de strings reais do Admin)

**1. "Falha ao carregar" (genérico, ex.: lista de administradores, config fiscal)**

- Antes: `Não foi possível carregar a lista de administradores.`
- Depois: **"Não conseguimos carregar os administradores agora. Atualize a página; se continuar, tente em alguns minutos."**
- Por quê: a versão original já é decente (sem jargão), mas para no diagnóstico. A nova
  dá o próximo passo (atualizar) e a saída se persistir.

**2. "Erro de conexão" / falha ao falar com a API**

- Antes: `Erro ao verificar credenciais. Tente novamente.`
- Depois: **"Não conseguimos confirmar suas credenciais — pode ter sido a conexão. Tente entrar de novo em instantes."**
- Por quê: nomeia a causa provável (conexão) sem inventar certeza, e a ação é específica
  ("entrar de novo"), não um "tente novamente" solto.

**3. "Falha ao enviar certificado" (config fiscal, NFC-e)**

- Antes: `Falha ao enviar certificado. Verifique o arquivo e a senha.`
- Depois: **"O certificado não foi aceito. Confira se o arquivo é .pfx/.p12 e se a senha está correta, depois envie de novo."**
- Por quê: já trazia ação, mas "o arquivo" era vago. A nova diz *o que* conferir no
  arquivo (extensão) — torna o próximo passo executável sem adivinhação.

**4. Conflito 409 (registro já existe / em uso)**

- Antes: `Conflito de dados. O registro já existe ou está em uso.`
- Depois: **"Já existe um cadastro com esses dados. Verifique se o cliente não foi criado antes ou use um e-mail/documento diferente."**
- Por quê: "conflito de dados" e "registro" são bastidor. A nova fala em "cadastro" e
  "cliente", e o próximo passo oferece as duas saídas reais (já existe vs. trocar o dado).

**Boa referência interna a seguir:** o mapa `FallbackMessageForStatus` do `AdminApiClient`
já segue o padrão (ex.: 401 → "Sessão expirada. Faça login novamente."; 503 → "Serviço
temporariamente indisponível. Tente novamente em instantes."). Use-o como gabarito de
tom; só evite os genéricos quando você tiver causa mais específica em mãos.

---

## 4. Padrão de estado vazio (empty state)

Tela vazia não é erro — é uma oportunidade de orientar. Todo empty state responde três
perguntas: **o que é** esta área, **por que está vazia** agora, e **qual o próximo passo**
(de preferência um botão). É o contrato do `<es-empty-state>` (`title` + `description` +
slot de ação).

**Fórmula:**
- `title`: o que falta, em uma frase nominal curta. ("Nenhum cliente ainda")
- `description`: por que está vazio + o que acontece quando preencher. ("Cadastre seu
  primeiro cliente para começar a faturar.")
- ação (slot): um CTA verbo+objeto. ("Novo cliente")

Distinga dois casos — eles pedem copy diferente:
- **Vazio de origem** (nunca houve nada): tom de boas-vindas + convite para criar.
- **Vazio de filtro/busca** (há dados, mas nada bateu): explique que é o filtro e
  ofereça limpar a busca. Ícone `search`, não `inbox`.

### Exemplos

**1. Lista de administradores (vazio de origem):**
```html
<es-empty-state icon="users"
                title="Nenhum administrador ainda"
                description="Cadastre admins para que sua equipe acesse este painel.">
  <es-button variant="primary" icon="plus">Novo admin</es-button>
</es-empty-state>
```

**2. Busca de logs de auditoria sem resultado (vazio de filtro):**
```html
<es-empty-state icon="search"
                title="Nenhum registro com esses filtros"
                description="Tente ampliar o período ou limpar a busca para ver mais resultados.">
  <es-button variant="secondary" icon="x">Limpar filtros</es-button>
</es-empty-state>
```

**3. Lista de cupons (vazio de origem, com contexto de valor):**
```html
<es-empty-state icon="tag"
                title="Nenhum cupom criado"
                description="Crie cupons de desconto para atrair e reter clientes nas assinaturas.">
  <es-button variant="primary" icon="plus">Novo cupom</es-button>
</es-empty-state>
```

**O que evitar no empty state:**
- Só o título ("Nada por aqui") sem o porquê nem a saída — deixa a pessoa parada.
- Descrição que repete o título ("Nenhum cupom. Não há cupons.").
- CTA genérico ("Adicionar") sem o objeto — ver seção 5.

---

## 5. Rótulos de botão e CTA

**Regra de ouro: verbo no imperativo + objeto.** O rótulo diz exatamente o que vai
acontecer ao clicar. A pessoa nunca deve precisar adivinhar.

| Faça (verbo + objeto)     | Evite (vago/ambíguo)     |
|---------------------------|--------------------------|
| Novo cliente              | Adicionar                |
| Salvar alterações         | OK / Enviar              |
| Emitir fatura             | Confirmar                |
| Suspender assinatura      | Aplicar                  |
| Acessar como cliente      | Entrar                   |
| Enviar certificado        | Upload                   |
| Limpar filtros            | Reset                    |
| Reabrir chamado           | Reabrir                  |

Regras finas:
- **Botão primário = a ação principal da tela**, uma só por contexto. Os demais são
  `secondary`/`ghost`. (Casa com `variant="primary"` do `<es-button>`.)
- **Ação destrutiva nomeia o que destrói** e usa `variant="danger"`: "Excluir cupom",
  não "Excluir". No modal de confirmação, repita o objeto e o efeito.
- **Estado de carregando** vira gerúndio com reticência, no mesmo verbo: "Salvar" →
  "Salvando…" (`loading="true"`). Não troque o verbo no meio do caminho.
- **Cancelar/voltar** ficam neutros e curtos: "Cancelar", "Voltar". Nunca "Não" sozinho.
- **Par de confirmação** mantém paralelismo: a ação positiva nomeia o efeito, a negativa
  é a saída. Ex.: ["Suspender assinatura"] / ["Cancelar"].
- **Ícone reforça, não substitui** o texto — exceto botão `icon-only`, que exige
  `aria-label` com verbo+objeto ("Mais ações").

Exemplos de pares em modal:
- Excluir: **"Excluir cupom"** (danger) / **"Cancelar"**
- Suspender: **"Suspender assinatura"** (danger) / **"Manter ativa"**
- Reenviar convite: **"Reenviar convite"** (primary) / **"Cancelar"**

---

## 6. Microcopy de `<es-help>`: como escrever um bom "Curto"

`<es-help>` é a ajuda inline ao lado de um campo, rótulo ou termo técnico — um ícone de
interrogação que revela uma explicação curta. O texto dela é o **"Curto"** (campo
`Curto` do `TermoGlossario`): uma a duas frases que desfazem a dúvida no exato ponto em
que ela nasce, sem mandar a pessoa para uma página de documentação.

**O que faz um "Curto" bom:**

1. **Responde "o que é isto e por que me importa", nessa ordem.** Primeira frase define;
   segunda (opcional) diz a consequência prática.
2. **Cabe em ~1–2 linhas** (até ~160 caracteres). Se precisa de mais, o campo está
   pedindo um link "Saiba mais" (para a Central de Ajuda), não só um `<es-help>`.
3. **Linguagem da pessoa, não da entidade.** Nada de nomes de classe, coluna ou enum.
4. **Concreto e acionável quando o campo exige uma decisão** — diga o efeito da escolha,
   não só a definição.
5. **Expande a sigla na primeira menção** e dá a fonte quando vem de fora (SEFAZ, banco).

**Estrutura recomendada:** `[Definição em 1 frase]. [Consequência ou dica prática.]`

### Exemplos (termos reais do domínio)

| Termo / campo        | "Curto" ruim                         | "Curto" bom |
|----------------------|--------------------------------------|-------------|
| CSC                  | "Código de Segurança do Contribuinte." | "CSC: código que a SEFAZ fornece para validar o QR Code da NFC-e. Sem ele, a nota não é aceita." |
| Teste grátis (trial) | "Período de trial da assinatura."     | "Dias em que o cliente usa a plataforma de graça. Ao acabar sem plano pago, a conta fica Expirada." |
| Suspender assinatura | "Muda o status para Suspensa."        | "Bloqueia o acesso do cliente sem cancelar o contrato. Use para falta de pagamento; dá para reativar depois." |
| MRR                  | "Monthly Recurring Revenue."          | "Receita recorrente do mês: a soma dos planos das assinaturas ativas. É a base do faturamento previsível." |
| Nível (N1–N4)        | "Nível de atendimento do chamado."    | "Etapa de escalação do chamado (N1 a N4). Não é prioridade — é quem cuida: N1 atende, N4 é o time mais técnico." |
| Acessar como         | "Impersonação do tenant."             | "Entra na conta do cliente para diagnosticar um problema. Tudo fica registrado com seu nome e o horário." |

**Não use `<es-help>` para:**
- Mensagem de erro (isso é seção 3).
- Texto longo de manual (use link "Saiba mais" para a Central de Ajuda).
- Repetir o rótulo do campo com outras palavras.
- Avisos críticos que a pessoa precisa ver sem clicar (use um aviso visível, não
  escondido atrás do ícone).

---

## Apêndice: checklist rápido antes de mandar copy

- [ ] Usa o **termo da UI** (Cliente, Chamado, Loja virtual), não o de bastidor?
- [ ] Toda sigla nova foi **expandida na estreia**?
- [ ] Erro tem **causa + próximo passo**? Nada de `ex.Message` cru na tela?
- [ ] Empty state diz **o que é, por que vazio, e a saída**?
- [ ] Botão é **verbo + objeto** no imperativo?
- [ ] Sem emoji, sem ALL CAPS, sem jargão não explicado?
- [ ] Trata por **"você"**; fala de **"sua loja / seus clientes"**?
