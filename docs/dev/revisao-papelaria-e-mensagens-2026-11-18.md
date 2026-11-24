# Revisão: papelaria, e-mail e mensagens prontas (18/11/2026)

Base: `master` local (3 commits atrás de `origin/master`, nenhum toca estes arquivos). Somente leitura.

## Veredito

A identidade não é única. Os e-mails usam 3 cabeçalhos diferentes. Não existe assinatura nem rodapé. Não existe papel timbrado A4. As respostas automáticas têm 2 fontes de verdade, e os textos divergem para o mesmo gatilho. O script de atendimento do assistente está implementado e bate com a spec.

## 1. O que existe e o que falta

| Ativo | Situação | Onde |
|---|---|---|
| Guia de marca / design system | Parcial: tokens de cor e o DS do cardápio web. Não há guia de papelaria | `design/tokens.colors.js`, `EasyStock.Console/src/estilos/tokens.css`, `docs/design/cardapio-web-design-system.md` |
| Assinatura de e-mail | NÃO EXISTE | 0 de 29 templates |
| Rodapé de e-mail (contato, CNPJ, "por que recebeu") | NÃO EXISTE | 0 de 29 templates |
| Papel timbrado A4 (orçamento, recibo, declaração) | NÃO EXISTE | Só cupom térmico em `Data/Templates/Impressao/*.sbn` |
| Cupom impresso (cabeçalho + rodapé) | OK, com 1 problema (item 3) | `Impressao/pedido.sbn` |
| Respostas rápidas | 6 de fábrica, editáveis | `EasyStock.Console/src/infra/catalogo.js` |
| Textos internos | 6 | idem |
| Automáticas | 6 no console + 5 padrões no servidor | `dominio/automacao.js`, `Domain/Entities/Atendimento/*`, `ExpedienteLoja.cs` |
| Modelos Meta (janela fechada) | 2 | `catalogo.js` (`MODELOS`) |
| Script de atendimento (assistente) | OK, 8 etapas | `Application/Services/Atendimento/RoteiroAtendimento.cs` |

## 2. E-mails transacionais (29 em `EasyStock.Api/Data/Templates/Email/`)

| Achado | Gravidade | Detalhe |
|---|---|---|
| 3 identidades no cabeçalho | ALTA | 7 com "Casa da Baba" marrom `#5B3A1E`; 9 com wordmark "EasyStok" azul `#0E2A6E`; 13 sem cabeçalho |
| Sem logo em imagem | MÉDIA | Só texto. As 2 imagens existentes são QR de Pix |
| Sem rodapé e sem assinatura | ALTA | Nenhum diz quem enviou, contato, CNPJ, "não responda" ou por que a pessoa recebeu |
| 3 laranjas para o mesmo botão | MÉDIA | `#C2410C`, `#E85814`, `#F26B25`. Cores escritas à mão, sem token |
| Texto sem acento | MÉDIA | "esta disponivel", "Nao conseguimos", "Se voce nao solicitou", "detalhes tecnicos", "atribuido", "solicitacao", "acao" em fatura_*, pagamento_falhou, reset_senha, sla_*, ticket_*, bug_fix |
| "email" e "e-mail" misturados | BAIXA | reset_senha/confirmacao usam "email"; convite usa "e-mail" |
| Template sem casca HTML | BAIXA | `alerta_estoque_critico`, `produto_vencido`, `sla_*`, `ticket_*` sem `max-width` nem fundo |
| Template legado fora da paleta | MÉDIA | `Application/Resources/Email/cadastro_usuario_confirmacao.html` usa índigo `#4f46e5` e duplica `confirmacao_email_email_v1` |

Pergunta de decisão: cabeçalho por destinatário. Proposta: e-mail para cliente da loja leva a marca da loja (tenant); e-mail da plataforma (fatura, senha, ticket, incidente) leva EasyStok. Hoje a divisão é aleatória.

## 3. Impressão (papel timbrado mais próximo)

| Achado | Gravidade |
|---|---|
| Lema "Da nossa casa para a sua mesa" fixo no template, vale para todo tenant | MÉDIA (vira texto da loja) |
| Cabeçalho com logo ou nome e rodapé com contatos | OK |
| Não há modelo A4 timbrado para orçamento, recibo ou declaração | Lacuna |

## 4. Respostas automáticas: 2 fontes, textos divergentes

| Gatilho | Console (`automacao.js`) | Servidor (padrão) |
|---|---|---|
| Primeiro contato | "Olá! Aqui é a Casa da Baba, massa artesanal com entrega agendada. Segue nosso cardápio..." | "Oi! Seja bem-vindo(a) à Casa da Baba 🍝" (`ConfiguracaoAtendimento`) |
| Fora do horário | "Oi! Hoje já fechamos por aqui, voltamos {abre}. Se quiser já deixar o pedido anotado..." | "Oi! No momento estamos fechados. Abrimos {abre} e te respondemos assim que der." (`ExpedienteLoja`) |
| Loja fechada | "...que eu separo para o próximo horário, **sem prometer prazo ainda**." | "Oi! Hoje não estamos atendendo..." |
| Pagamento confirmado | igual ao servidor (copiado de propósito, #2734) | OK |

Inferência a confirmar: qual dos dois textos chega ao cliente depende de qual caminho dispara. Não medi em runtime.

## 5. Copy das mensagens prontas

| # | Texto | Problema | Gravidade |
|---|---|---|---|
| 1 | Loja fechada: "sem prometer prazo ainda" | Instrução interna vazando para o cliente | ALTA |
| 2 | Rápida "Enviar cobrança": "Segue o link de pagamento" | Promete link sem variável de link. É o mesmo defeito que tirou a regra "Link de pagamento" (comentário em `automacao.js`) | ALTA |
| 3 | Rápida "Enviar cardápio": "Segue nosso cardápio de hoje" | Sem link. `{linkCardapio}` só vale na Primeira resposta (achado P0.1 corrigido só na regra) | ALTA |
| 4 | Rápida "Janelas do dia": "12h30 às 13h30 e 18h30 às 19h30" | Horário fixo no texto; fica errado quando a janela mudar | MÉDIA |
| 5 | Voz | Mistura "eu" (separo, anoto) e "nós" (fechamos, recebemos, estamos). Saudação alterna "Oi!", "Olá!", "Oi, {nome}!" | MÉDIA |
| 6 | "Obrigada" fixo no feminino | Vale para a Casa da Baba, não para todo tenant | BAIXA |
| 7 | "Casa da Baba" fixo em padrão de domínio e nos 2 modelos Meta | Outro tenant herdaria a marca errada | MÉDIA |
| 8 | Rápida "Agradecer" = regra "Agradecimento" | Texto duplicado; editar um não muda o outro | BAIXA |
| 9 | "bem-vindo(a)" | Forma com parênteses; "Que bom te ver por aqui" evita o gênero | BAIXA |

## 6. Script de atendimento

`RoteiroAtendimento.Padrao` tem as 8 etapas da spec `docs/plan/atendimento-whatsapp/20-processo-de-atendimento.md` (Acolher, Entender, Mostrar, Montar, Entrega, Confirmar, Pagar, Acompanhar). Sem achado. Lacuna: o script é do assistente; não há roteiro escrito para a atendente humana.

## 7. Proposta de pacote (não iniciado)

| Ordem | Entrega | Tier |
|---|---|---|
| 1 | Corrigir copy: itens 5.1, 5.2, 5.3, 5.4 e acentos dos e-mails | Baixo (fix) |
| 2 | Uma fonte só para os textos automáticos (servidor), console lê dele | Alto (refactor sem spec) |
| 3 | Casca única de e-mail: cabeçalho por destinatário, rodapé, assinatura, cores por token | Alto (feat sem spec) |
| 4 | Papel timbrado A4 e guia de papelaria (`docs/design/papelaria.md`) | Alto (feat sem spec) |

Não verificado: guia de marca no repo `casa-da-baba-conhecimento` (fora deste clone) e status real dos modelos na Meta.
