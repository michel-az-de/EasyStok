# QA do console web em produção (13/11/2026, #2491)

Alvo: `app.easystok.online`, build `68b583816`, tenant Casa da Baba, sessão real do Felipe. Somente leitura:
nenhum formulário enviado, nenhuma configuração alterada. Desktop 1568×711 no Chrome.
Skill de QA: `design:accessibility-review` (WCAG 2.1 AA) mais QA funcional, rede, console e borda HTTP.
Complementa a revisão geral de 12/11, a do console desktop de 12/11 e a de Cadastros de 13/11
(as três em `docs/dev/`), que foram feitas por leitura de código.

## Cobertura

| Lente | Como | Resultado |
|---|---|---|
| WCAG automático | Script no DOM em 41 telas (`#/` e todas as `#/m/<módulo>/<tela>` de `dominio/modulos.js`) | 0 controle sem nome, 0 campo sem rótulo, 0 imagem sem `alt`, 0 texto abaixo do contraste, 0 rolagem horizontal, 1 `h1` e 1 `main` por tela. 3 alertas eram falso positivo (interruptor com `<label>`) |
| Teclado | Modal "Novo prato": 40 Tabs, Esc | Foco preso no `<dialog>`, contorno de 3 px, Esc fecha e devolve o foco ao botão |
| Rede | `performance` em 212 chamadas `/api` | 0 resposta 4xx/5xx; mais lenta 596 ms (miniatura do acervo) |
| Console JS | Erros da aba | 0 erro do app (1 aviso do Google Sign-In, externo) |
| Borda HTTP | `curl -I` | `X-Frame-Options DENY`, `nosniff`, `Referrer-Policy`; CSP só em Report-Only; sem HSTS |
| Login | Página pública e retorno | `autocomplete` correto, rótulos, tema como `radiogroup`; o link profundo volta à tela pedida depois do login |
| Funcional | Leitura de Início, Caixa, Visão geral, Extrato, Relatórios, Atendimento, Cardápio, Produção, Integrações, Canais, Agenda, Usuários | Achados abaixo |

**Fora da cobertura:** celular e tablet (o redimensionamento da janela não pegou), tema escuro, leitor de tela real,
fluxos que gravam (só leitura em produção), PWA, loja online e Admin.

## Inventário

Gravidade: P0 dinheiro errado ou perdido; P1 fluxo quebrado, regra violada ou risco de segurança; P2 inconsistência
ou atrito que leva a erro; P3 cosmético. Estado: **N** novo, **K** já conhecido (com a referência).

| Id | Grav. | Tela | Fato observado | Recomendação | Estado |
|---|---|---|---|---|---|
| Q01 | P1 | Configurações › Usuários | 3 contas "Homologação PR2143" (Atendimento, Cozinha e Dona) seguem ativas no tenant de produção, último acesso há 11 dias. A de perfil Dona tem acesso total à loja | Desativar as 3 (decisão do Felipe) e dar validade a conta de homologação criada em produção | N |
| Q02 | P1 | Integrações × Canais | Integrações diz WhatsApp "Ligado · Chave da FMA" com o número `+1 555-189-8702 (Test Number)`; Canais diz "WhatsApp conectado por QR", conexão provisória. As duas telas descrevem canais diferentes como o ativo, e o Atendimento mostra "3 mensagens não entregues" | Uma fonte da verdade para o canal que envia; número de teste da Meta aparece como alerta, não como "Ok" | K parcial (#2071) |
| Q03 | P2 | Caixa × Extrato | Caixa: "Há R$ 19,00 a devolver de 1 pedido cancelado". Extrato: "Há R$ 282,00 a devolver de 2 pedidos cancelados" (90 dias). A mesma pendência aparece com dois números e o Caixa não diz o recorte | Mesmo recorte nas duas telas, ou o Caixa diz "nesta sessão" e leva ao total | N (tema de #1724) |
| Q04 | P2 | Caixa do dia | Caixa aberto desde 28/10; o lançamento "Pagamento do pedido nº 001" mostra só "21:39", e o pagamento é de 05/11 | Data junto da hora quando a sessão passa de um dia | N |
| Q05 | P2 | Caixa › Últimos fechamentos | Fechamento de 11/10 mostra a observação gravada sem acento e em caixa alta: "Atencao: ha lancamentos entre 12/10/2026 e 27/10/2026 … NAO foram incluidos … (revisar/reconciliar manualmente)". Texto técnico, sem ação | Traduzir em aviso do produto com ação; o texto vem do dado (o `CaixaController.cs:57` do Web detecta essa frase) | N |
| Q06 | P3 | Caixa › Últimos fechamentos | Redação diferente para a mesma conta: "Sobram R$ 699,97" e "Diferença: -R$ 299,92"; o fechamento de 08/10 não tem nome de quem fechou | Uma redação ("Sobrou"/"Faltou"); "sem registro de quem fechou" quando faltar | N |
| Q07 | P3 | Caixa do dia | Botão "Fechar o caixa de 28/10" duas vezes na mesma vista (cabeçalho e faixa) | Um só | N |
| Q08 | P2 | Caixa do dia | "Pagamentos de pedidos R$ 19,00" fica na fileira da gaveta (Saldo inicial, Outras entradas, Outras saídas) e é Pix. A conta da gaveta fica ambígua | Agrupar: gaveta (dinheiro) e fora da gaveta, com o pagamento de pedido no grupo certo | N |
| Q09 | P3 | Financeiro › Relatórios | Legenda "de 2026-11-01 a 2026-11-30" em formato ISO | dd/mm/aaaa | N |
| Q10 | P3 | Entregas › Agenda | Às 20:27, as janelas de hoje 09:00–12:00 e 14:00–17:00 aparecem como "0 de 3 vagas", sem "encerrada"; a janela chamada "12h00 às 14h00" repete o horário "12:00–14:00" | Marcar janela passada; não repetir o horário no nome | N |
| Q11 | P2 | Navegação | Dois trilhos laterais diferentes. No Atendimento: Loja aberta, Atendimento, Entregas, Cozinha, Respostas automáticas, Gestão, Buscar, Avisos, Lembretes, Ajuda, Disposição. Na gestão: Início, Atendimento, Cozinha, Entregas, Cardápio, Produção, Financeiro, Cadastros, Configurações, Respostas automáticas. Cozinha e Entregas trocam de ordem | Mesma ordem nos itens comuns; diferença só no que é do modo | N (tema de #2140) |
| Q12 | P3 | Trilho da gestão | Rótulo "Configura-ções" quebrado com hífen em duas linhas | Rótulo curto ou hifenação desligada | N |
| Q13 | P3 | Atendimento › lista | Chips de motivo cortados ("Contato encaminhado à…", "Contato da equipe que recebe…") | Texto curto no chip, frase inteira na conversa (padrão do L9 da banca #2232) | A conferir |
| Q14 | P3 | Cardápio › Novo prato | O `<label>` envolve dica e controle: o texto do rótulo lido pelo DOM é "Descrição para o clienteAté 24…" e "Linha de produtoPara servirPre…" | Dica em `aria-describedby`, fora do `<label>`; conferir com leitor de tela | A conferir |
| Q15 | P3 | Sessão | Abrir o console em nova aba pede login de novo. É por desenho: a sessão fica no `sessionStorage` salvo quando o token traz `sessaoPersistente` (`infra/api/sessao.js`) | Decidir se a gestão no desktop deve oferecer "manter conectado" | Por desenho |
| Q16 | P2 | Borda HTTP | CSP só em `Report-Only` e sem `report-uri`/`report-to`: ninguém coleta as violações, então não há dado para ligar o modo que bloqueia. Sem `Strict-Transport-Security` | Coletar relatórios, depois ligar a CSP; HSTS no Caddy | K parcial (revisão de 12/11, item 4.1) |
| Q17 | P3 | Cardápio › Banco de imagens | A tela tem 53 mil caracteres de texto e carrega uma miniatura por imagem (470 a 600 ms cada) | Medir o total e paginar ou carregar sob demanda | A medir |
| Q18 | P3 | Financeiro › Visão geral | Links "Ver lista ›" com 59×20 px, abaixo do alvo de 24 px (WCAG 2.2, 2.5.8) | Área de toque de 24 px ou mais | N |

## Banca revisora

Três revisores independentes, só leitura, sem navegador: **R1** verificação no código (`origin/master` efc107517),
**R2** refutação adversarial (ADRs, issues e revisões anteriores), **R3** impacto no negócio e prioridade.
Regra do veredito: vale a maioria; quando R1 traz causa no código, a evidência dele decide. Custo medido dos
revisores: 315 mil tokens (R1 147k, R2 92k, R3 77k).

### Veredito

De 18 achados, **12 confirmados**, **3 refutados** (Q12, Q15, Q18), **2 reformulados** (Q09, Q17) e **1 confirmado
com gravidade reduzida** por ser dado legado (Q05). A banca acrescentou **3 achados novos**; o N1 foi reconferido
no código pelo revisor principal. Lista final: 2 P1, 7 P2, 8 P3.

### Parecer item a item

| Id | R1 | R2 | R3 | Final | Grav. final | Causa ou motivo |
|---|---|---|---|---|---|---|
| Q01 | Inconclusivo, P1 | Reclassificado, P2 | P1 | **Confirmado** | **P1** | Dado de produção; o código não dá validade a conta de homologação. R2 lembra que o risco depende da senha, que não foi medida |
| Q02 | Confirmado, P1 | Reclassificado, P2 | P1 | **Confirmado** | **P1** | As telas leem fontes diferentes: Integrações mostra `item.ativo` de `/api/integracoes` (credencial cadastrada); Canais mostra `/whatsapp/status` (quem envia). O número de teste é só impresso (`AbaIntegracoesApi.jsx:45,59-60`) |
| Q03 | Confirmado com ressalva, P2 | Inconclusivo, P2 | P1 | **Confirmado** | P2 | Não é erro de soma: o Caixa conta a sessão (`caixa.js:61-71`), o Extrato 90 dias (`useDevolucoesPendentes.js:19`). O defeito é o Caixa não dizer o recorte |
| Q04 | Confirmado, P2 | Mantido, P3 | P2 | **Confirmado** | P2 | `horaCurta` só devolve HH:MM (`formato.js:35-40`), mesmo em sessão de vários dias |
| Q05 | Explicado diferente, P3 | Reclassificado, P3 | P2 | **Confirmado, dado legado** | P3 | A frase sem acento é de 11/10, antes da #2256; o texto atual já sai certo. O console imprime `observacoes` cru (`AbaCaixaApi.jsx:308,574`) |
| Q06 | Explicado diferente, P3 | Mantido, P3 | P3 | **Confirmado em parte** | P3 | "Diferença: -R$" é texto anterior à #2078. Procede só a falta do nome de quem fechou |
| Q07 | Confirmado, P3 | Inconclusivo, P3 | P3 | **Confirmado** | P3 | Dois botões no caixa esquecido aberto (`AbaCaixaApi.jsx:544` e `:565`) |
| Q08 | Confirmado, P2 | Inconclusivo, P3 | P1 | **Confirmado** | P2 | "Pagamentos de pedidos" soma todos os meios e fica na fileira da gaveta; o mesmo Pix aparece de novo em "Fora da gaveta" (`AbaCaixaApi.jsx:331-341`, `caixa.js:368-387`). Absorve o N2 do R1 |
| Q09 | Explicado diferente, P3 | Mantido, P3 | P3 | **Reformulado** | P3 | A data ISO está num `<caption class="sr">` (`PainelFinanceiro.jsx:302`): só o leitor de tela ouve "2026-11-01". Vira item de acessibilidade |
| Q10 | Confirmado, P3 | Mantido, P3 | P2 | **Confirmado** | P3 | Sem lógica de janela passada (`AgendaEncomendasApi.jsx:47-49`); "0 de 3 vagas" quer dizer 0 ocupadas de 3 e confunde |
| Q11 | Confirmado, P2 | Reclassificado, P3 | P3 | **Confirmado** | P3 | Duas listas independentes: ordem fixa no JSX do Atendimento (`Moldura.jsx:233-282`) e `emOrdemDosModulos` na gestão. Tema da #2140 |
| Q12 | Confirmado por desenho | Refutado | P3 | **Refutado** | N/A | `hyphens: auto` com `lang="pt-BR"` é decisão da #2326, homologada a 200% de zoom |
| Q13 | Confirmado, P2 | Inconclusivo | P3 | **Confirmado** | P2 | `motivoDeEscalada.js:10-23` só conhece 2 padrões; os motivos novos da #2249 entram crus no chip. Todo motivo novo da API repete o defeito. Absorve o N5 do R1 |
| Q14 | Confirmado, P3 | Mantido, P3 | P2/P3 | **Confirmado** | P3 | Marcação manual em `EditorItemCardapio.jsx:256`; o `Campo.jsx` comum já põe a dica em `aria-describedby`. Nome acessível verboso, não falha WCAG |
| Q15 | Confirmado por desenho | Refutado | Não é defeito | **Refutado** | N/A | `sessaoPersistente` vale só para perfis da Casa da Baba (`AutenticarUsuarioUseCase.cs:186`). Fica como decisão de produto, se o Felipe quiser |
| Q16 | Confirmado com ressalva, P2 | Mantido, P2 | P2 | **Confirmado** | P2 | CSP sem `report-uri`/`report-to`. O `nginx.conf` diz que o HSTS fica com o Caddy, mas o `Caddyfile` não tem diretiva `header`; a #2407 fechou sem o HSTS chegar ao `app.` |
| Q17 | Refutado, P3 | Inconclusivo | P3 | **Reformulado** | P3 | Pagina de 60 em 60 (`AbaAcervo.jsx:24-46`). O custo real: miniaturas sem `loading="lazy"` (`AbaAcervo.jsx:17`, `SugestoesDoAcervo.jsx:11`) |
| Q18 | Explicado diferente | Refutado | P3 | **Refutado** | N/A | `.verLista::after { inset: 0 }` torna o bloco inteiro clicável; a medida pegou só o texto |

### Achados novos da banca

| Id | Grav. | Fato | Onde |
|---|---|---|---|
| N1 | P2 | O aviso de fechamento do Web ficou cego. `CaixaController.cs:57` procura "nao foram incluidos", mas desde a #2256 o back-end grava "que não entraram neste fechamento". O toast de aviso nunca dispara e o fechamento sempre aparece como sucesso. Nenhum teste liga as duas pontas. Reconferido pelo revisor principal | `EasyStock.Web/Controllers/CaixaController.cs:57`, `FecharCaixaUseCase.cs:104` |
| N3 | P3 | A grade de indicadores do Caixa troca de layout quando `linhas.length !== 6`, e as linhas são condicionais; o comentário fala em sete itens | `AbaCaixaApi.jsx:345` |
| N4 | P3 | A observação do fechamento junta nota do operador e aviso com `\n`, e a tela mostra sem `white-space: pre-line`: as duas frases se fundem | `FecharCaixaUseCase.cs:108`, `AbaCaixaApi.jsx:308,574` |

(N2 entrou no Q08 e N5 no Q13.)

### Pacotes de correção, em ordem

| # | Pacote | Ids | Quem decide | Tier |
|---|---|---|---|---|
| 1 | Higiene de produção: desativar as 3 contas "Homologação PR2143" e dar validade a conta de homologação | Q01 | **Felipe** (mudança em produção) | N/A |
| 2 | Canal do WhatsApp: fonte única do canal que envia, alerta de número de teste, tratar as 3 mensagens não entregues | Q02 | **Felipe** escolhe o canal oficial; depois o agente | baixo (fix) |
| 3 | Caixa honesto: recorte da devolução escrito, Pix num grupo só, data junto da hora, um botão de fechar, nome de quem fechou, grade e quebra de linha | Q03, Q08, Q04, Q07, Q06, N3, N4 | **Felipe** confirma que Pix fica fora da gaveta; o resto é do agente | baixo (fix) |
| 4 | Aviso de fechamento: Web reconhece o texto atual, com teste que liga as pontas; aviso legado vira mensagem com ação | N1, Q05 | Agente | baixo (fix) |
| 5 | Atendimento: mapear os motivos da #2249 e limitar o chip a texto curto | Q13 | Agente | baixo (fix) |
| 6 | Agenda e acessibilidade: janela encerrada, "ocupadas de", data por extenso na legenda do relatório, dica fora do `<label>`, `loading="lazy"` | Q10, Q09, Q14, Q17 | Agente | baixo (fix) |
| 7 | Navegação: mesma ordem dos itens comuns nos dois trilhos | Q11 | Agente, dentro da #2140 | spec |
| 8 | Borda HTTP: HSTS no Caddy do `app.`, depois coleta da CSP antes de ligar o bloqueio | Q16 | **Felipe** (infra; HSTS é difícil de desfazer, confirmar que todos os subdomínios só usam HTTPS) | alto |

### Lacunas de método (R2 e R3)

- Q14 e Q18 mediram o DOM e não o resultado (nome acessível computado, área clicável real). Medir pela árvore de
  acessibilidade na próxima rodada.
- Achados de dado de produção (Q01, Q05, Q06) não separam defeito do sistema de dado mal alimentado sem uma base limpa.
- Celular, tablet, PWA e fluxos que gravam ficaram de fora; é onde a operadora trabalha no balcão, então esta lista
  provavelmente subestima o impacto real.
