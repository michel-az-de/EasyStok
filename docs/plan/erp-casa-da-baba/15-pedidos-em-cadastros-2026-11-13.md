# Pedidos no módulo Cadastros: banca de UX em ondas (2026-11-13)

Issue #2478. Retoma a fatia M3.2 de [04-m3-atendimento.md](04-m3-atendimento.md) ("Pedidos fora da conversa"),
que nunca foi construída, e muda o lugar da tela: Cadastros, ao lado de Clientes e Produtos, a pedido do Felipe.

## Onda 1. Problema e fatos

| Pergunta | Resposta medida |
|---|---|
| Qual a dor | Venda de balcão, pedido de outro dia e pedido sem conversa não têm onde ser achados no console. A Busca na Casa acha, mas mostra o status cru da API (`aguardando_pagamento`), sem pagamentos nem linha do tempo |
| Quem sente | A dona e a gerente (conferir, responder "cadê meu pedido?", reimprimir); a atendente (Operador) para achar o pedido de quem liga |
| A API já resolve? | Sim. `GET api/pedidos` pagina, busca (número, nome, telefone, apto, observação), filtra situação, cliente e período (`CriadoEm`, dia de Brasília) e ordena. `GET api/pedidos/{id}` traz itens, eventos e pagamentos |
| Quem pode ler | Rota com policy Operador (SuperAdmin, Admin, Gerente, Operador), módulo Atendimento e empresa do token. A matriz `me/modulos` não dizia isso: o Visualizador veria a tela e levaria 403 |
| O que falta na API | Filtro por canal (`Origem` é texto livre) e endereço no detalhe. Fora desta entrega |
| Premissa refutada | "Quem abre Cadastros talvez não abra Atendimento". Falso: `AcessoModulos.PermissaoDe(Cadastros)` é a mesma permissão de Atendimento (`AcessoModulos.cs:11`). O teste da matriz foi corrigido para dizer isso |
| A situação "Na fila" cabe no filtro? | Não cabia: a Cozinha chama de "Na fila" `aguardando` e `aprovado_baba`, e o filtro `status` era igualdade. O repositório passa a aceitar lista separada por vírgula (teste de integração com Postgres real) |

Decisão de escopo: leitura completa (lista, filtros, detalhe, imprimir, abrir conversa). Mudar situação,
cancelar, estornar e editar itens continuam na ficha da conversa (M3.3), onde estão o contexto e as travas.

## Onda 2. Proposta (arquitetura de informação e texto)

```
Cadastros › Pedidos
┌───────────────────────────────────────────────────────────────────┐
│ Pedidos                                                           │
│ Todos os pedidos da loja, de qualquer canal e de qualquer dia.    │
├───────────────────────────────────────────────────────────────────┤
│ [Buscar: número, nome, telefone ou observação] [Feito em ▾]       │
│ [Situação ▾] [Ordem ▾] [Atualizar]                                │
├──────────────┬──────────────────┬──────────────┬──────────┬───────┤
│ Pedido       │ Cliente          │ Situação     │ Total    │       │
│ Nº 12        │ Maria Souza      │ (Aguardando  │ R$ 75,00 │ Abrir │
│ 13/11 10:42  │ (11) 98225-4398  │  pagamento)  │ Falta 75 │       │
│ WhatsApp     │                  │              │          │       │
├──────────────┴──────────────────┴──────────────┴──────────┴───────┤
│ 134 pedidos                         [Anterior] Página 1 [Próxima] │
└───────────────────────────────────────────────────────────────────┘
```

| Decisão | Motivo |
|---|---|
| Período "Feito em" (Qualquer data, Hoje, 7 dias, 30 dias) e não "Data do pedido" | A API filtra a criação; encomenda feita ontem para amanhã aparece em "ontem". O rótulo diz o que o filtro faz |
| Padrão "Qualquer data", mais recentes primeiro | Cadastro não esconde dado atrás de filtro; a lista começa pelo que acabou de acontecer |
| Pílula só em Aguardando pagamento e Aguardando aprovação | Regra da `Pilula`: uma por linha e só quando o estado pede ação. O resto é texto |
| Situação com o vocabulário da Cozinha ("Na fila", "Em preparo", "Pronto", "Saiu para entrega") | Um nome por conceito (#1686) |
| Número do dia sempre com a data | O número volta a 1 todo dia; "Nº 12" sozinho é ambíguo |
| "Falta R$ x" no total | É a pergunta que a dona faz ao abrir um pedido; pedido cancelado não cobra |
| Detalhe em modal, com itens, totais, pagamentos e linha do tempo | Mesmo padrão do editor de Cadastros; a lista não perde a posição |
| A Busca na Casa abre o mesmo detalhe | Um detalhe de pedido só (DRY); some o status cru |
| Chave `verPedidos` na matriz, com a regra da rota | A API decide quem abre a tela (ADR-0046). Sem chave, sem tela: a API sobe antes do console no deploy, então não há janela em que a tela some por API antiga |

## Ondas 3 a 6. Revisões

Cada onda foi uma revisão independente (subagente que não viu as outras), com achado, decisão e prova.

| Onda | Lente | Resultado |
|---|---|---|
| 3 | Código e arquitetura (camadas, DRY, domínio testável) | 1 Major, 3 Minor/Nit, todos tratados |
| 4 | Navegador real: 1280 e 390 px, tema escuro, teclado e leitor de tela | 4 ajustes de UX aplicados |
| 5 | Adversarial: vazio, erro, 403, sessão trocada, fuso, paginação, dado sujo | 1 Major (o mesmo da 3), 3 Minor tratados; 6 ataques resistiram |
| 6 | Produção: tela no ar, versão conferida | registrada no fechamento da issue |

### Onda 3. Código

| Achado | Decisão | Prova |
|---|---|---|
| Major: o detalhe lia `entregEm`; o DTO manda `entreguEm`, então "Entregue em" nunca aparecia | Corrigido | Prova de navegador: o pedido entregue mostra "Entregue em" |
| Minor: "Nº 007 de 12/11" usava o dia em que o pedido foi feito; o número vale no dia de produção | `PedidoResult` ganha `DataNumero` (aditivo, opcional) e `numeroDoPedido` usa esse dia; sem número, vale o dia do pedido | `ListarPedidosDiaDoNumeroTests`; prova de domínio "Nº 007 de 15/11" |
| Minor: a rolagem grava estado a cada evento | Mantido: é o mesmo padrão da tela irmã (`PainelCadastros`), a memória é local e a tela não repinta por isso de forma perceptível | Decisão registrada aqui |
| Nit: faltava situação desconhecida no filtro por vírgula | `"aguardando,xyz"` traz só o pago | Teste de integração Postgres |

### Onda 4. Navegador

Saldo da lista vencia o cinza da base e ficou em destaque; "Atualizando…" em `role=status` para quem usa leitor
de tela; a data não se repete na linha (só a hora e o canal); nome vazio vira "Cliente sem nome" na lista e no
detalhe. A faixa azul no detalhe em celular era realce de toque do navegador; a prova confere que não há fundo.

### Onda 5. Adversarial

| Achado | Decisão |
|---|---|
| "Só leitura" não é literal: imprimir registra a impressão | Texto passa a dizer "sem alterar o pedido" |
| Recebido a mais aparecia como "Pago"; cancelado com valor recebido não dizia nada | `situacaoDoPagamento` devolve `sobra`: "Recebido R$ 5,00 a mais" e "Cancelado com R$ 30,00 recebido", na lista e no detalhe |
| Lista antiga fica visível durante a nova consulta | Já atenuada: `aria-busy` com opacidade, rodapé "Atualizando…" e paginação travada; abrir uma linha antiga abre um pedido válido |

Resistiram: troca de empresa ou usuário (remonta e aborta), fuso da loja nos períodos, os 10 status e as origens
sem rótulo, permissão (front mais estrito que a API), regressões da Busca e do Financeiro, paginação pelo `total`.
