# 23. Atendimento coerente: mensagens padrão locais e tela que diz quem é o cliente (#3203)

Spec curta, 22/11/2026. Origem: pedido do Felipe na sessão de 22/11, com nove prints do Console em produção.
Plano revisto por banca Opus sem contexto em 22/11 (aprovado com ajustes, aplicados).

## Problema

Na ponte WPPConnect (ADR-0062) a loja não tem modelo aprovado da Meta. Com a janela de 24 h fechada, o Console só
deixa enviar modelo; o catálogo exige Meta conectada (`CatalogoModelosWhatsApp.cs`) e o WPPConnect recusa modelo
(`WppConnectClient.cs`). A atendente não consegue responder. Medido em 22/11 na Casa da Baba: 0 modelos enviados
na história, webhook da Meta nunca verificado, 3 respostas prontas ativas, 26 mídias trocadas que a tela não mostra.

O resto da queixa é de coerência: modal no lugar de lista, lead com três caminhos de cadastro, cartão do cliente
sem telefone nem histórico, emoji curto e Assistente em modal fixa.

## Decisão

A loja segue o padrão da Meta (janela de 24 h e modelos) mesmo sem conta oficial. As mensagens padrão passam a
existir no EasyStok, já no formato que a Meta aprova, para uso hoje pela ponte e submissão futura.

| Spec | Issue | Tier | Entrega |
|---|---|---|---|
| S79 | #3204 | alto | Cadastro de mensagem padrão local: nome em snake_case, idioma, categoria UTILITY, cabeçalho, corpo com `{{n}}` em sequência, rodapé. Tabela `modelos_mensagem` com RLS, única por empresa, nome e idioma. `api/atendimento/modelos-locais`: Operador lê, Gerente escreve. |
| S80 | #3205 | alto | Origem (Meta ou Local) no modelo. O catálogo decide por empresa com a regra do `RoteadorWhatsAppClient`: sessão web lista os locais. Modelo local sai como texto renderizado e fica gravado como modelo. |
| S81 | #3206 | baixo | Console: com a janela fechada, o botão Respostas lista as mensagens padrão com prévia e campos; o envio acontece no compositor, sem modal. Gestão > Respostas cria, edita e arquiva, com sugestões prontas. |
| S82 | #3207 | baixo | Botão único "Cadastrar cliente" no cartão do lead, com formulário preenchido pelo que a conversa captou. |
| S83 | #3208 | alto | Respostas rápidas com categoria (coluna nova), busca e edição no próprio chat. |
| S84 | #3209 | baixo | Cartão "Quem é": telefone com copiar, pedidos, mídias trocadas e foto. Distância só com a coordenada da loja cadastrada. |
| S85 | #3210 | baixo | Seletor de emoji por categoria, com busca. |
| S86 | #3210 | baixo | Assistente da conversa em painel móvel (`PainelMovel`), no lugar da modal. |

Ordem: S79 → S80 → S81 → S82 → S83 → S84 → S85 e S86. Uma PR por issue, em sequência, porque `Composer.jsx` e
`CartaoDoCliente.jsx` são compartilhados.

### Travas da mensagem padrão local (S80)

- Só sai por ação humana, em conversa onde o cliente já escreveu (`UltimaMensagemEntradaEm` preenchido).
- Recusada no Chamar no WhatsApp (`IniciarConversaClienteUseCase`) e na mensagem programada.
- Só categoria UTILITY nesta rodada. MARKETING local fica fora.
- Lead sem cadastro recebe a mensagem local; com cliente, bloqueio e consentimento continuam valendo.
- Com a Meta conectada e sem sessão web, o catálogo volta a listar só os aprovados da Meta.

## Medida de resultado (fim)

Por entrega: teste que falha antes e passa depois, prova visual do Console rodando do worktree e print em produção.
Da rodada, em 14 dias: conversas com janela fechada respondidas por mensagem padrão (contagem de mensagens com
`ModeloJson` de origem local) e conversas de lead convertidas em cadastro pelo botão novo.
Parada: duas semanas sem nenhuma mensagem padrão local enviada reabrem a decisão da S80.

## Fora

Criar ou submeter modelo na Meta, disparo ativo ou em massa pela ponte (#2834), texto livre fora da janela,
redesenho geral de densidade visual do Atendimento.
