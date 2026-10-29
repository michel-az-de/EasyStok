# Balcão que se entende sozinho: plano de UX

Data: 28/10/2026. Base: master `c59b696e5`. Status: APROVADO pelo Felipe em 28/10 com as 6 recomendações da seção 9. Issue #2130.
Banca: 4 agentes (inventário no código, tela real medida, pesquisa de mercado, IA). Custo medido: 1,18 milhão de tokens.
Relatórios completos da banca em `banca/` (A inventário, B tela real, C pesquisa, D IA); as capturas PNG da B ficaram fora do repo. Este plano absorve a #1766 e a parte do Balcão da #1777.

## 1. Diagnóstico em números

O Balcão FUNCIONA. Ele só não fala uma língua só: cada tela, ícone e botão foi nomeado na hora em que nasceu.

| Problema | Número medido | Fonte |
|---|---|---|
| A mesma coisa com vários nomes | "Balcão" tem 4 sentidos, "Resumo" 4 e "Atendimento" 4. A cesta do cliente tem 6 nomes. A IA tem 6 a 8 nomes | A |
| Um ícone com vários sentidos | `relogio` tem 7 sentidos, `check` 6 e `log-out` serve a Encerrar e a Sair da conta. Não existe engrenagem: Configurações usa `automacao` | A, conferido |
| Dois sistemas de ícone | `Icone` (95 chaves, metade em português e metade com nome Lucide) e `MarcaIcone` (34). O mesmo módulo tem ícone diferente no trilho e no Hall | A |
| Sem tooltip de verdade | Não existe componente; 37 de 54 botões só-ícone não têm dica | A |
| Ação com nome variável | 14 grupos de ação com nomes diferentes; dispensar tem 4 rótulos (Fechar 19, Voltar 20, Cancelar 15, Manter 11) | A |
| Primitivos ignorados | 58 `<button>` crus, 2 famílias de chip à mão, cerca de 25 selos à mão, 6 mecanismos de feedback, 36 modais com 14 larguras | A |
| Ruído | 42 a 52 controles visíveis ao mesmo tempo no desktop, 6 animações infinitas, 12 cores com significado | B |
| Gestos demais | Montar e cobrar leva 6 gestos no desktop e 8 no celular. Abrir a ficha leva 3 gestos, uma rolagem de 317 px e 2 fechamentos. Enviar o cardápio leva 3 gestos e se chama "Link de compra" | B |
| Celular | 10 de 21 alvos abaixo de 44 px na conversa; Voltar sai do app em vez de voltar à lista; as mensagens ficam com 56% da altura | B |

O que já está bom e FICA: 100% dos botões só-ícone têm nome acessível, a menor fonte é de 13 px, não há rolagem horizontal, o CSS não tem hex fora de `tokens.css`, o `prefers-reduced-motion` é respeitado e já existem tokens de movimento (90/150/220/320 ms). O motor de IA é bom e seguro por desenho; a casca é que parece beta.

## 2. Princípios (a régua de toda PR deste plano)

1. **Copiar o que a atendente já sabe.** O WhatsApp é a referência de conversa, o Gmail a de lista e ações, o Android a de navegação. Só inventamos o que nenhum deles tem: pedido, cobrança e cozinha.
2. **Um conceito, um nome, um ícone.** Isso vale para todo o console e é guardado por teste, não por boa vontade.
3. **Uma ação primária por tela.** O resto fica no menu ⋮ ou aparece só quando faz sentido (divulgação progressiva).
4. **A IA propõe, a pessoa confirma com 1 toque.** Nada com efeito sai sem confirmação.
5. **Desfazer em vez de "tem certeza?"** Confirmação fica só para o irreversível, com o botão nomeado pela ação.
6. **Medir antes e depois.** O roteiro Playwright da banca B vira ferramenta versionada, e cada onda fecha com os números da seção 7.

## 3. Gramática do console (Onda 1)

### 3.1 Vocabulário canônico (o que a tela diz)

| Conceito | Nome na tela | Sai da tela |
|---|---|---|
| Módulo | Atendimento | "Balcão" como nome de coluna e de aba |
| Lista de conversas | Conversas | Balcão |
| Uma conversa | Conversa (cabeçalho com o nome do cliente) | Atendimento (aba do celular) |
| Painel lateral do cliente | Cliente | Ficha, Resumo, "Resumo de Fulana" |
| Cesta em montagem | Pedido (Comanda N, como na #1883) | Seleção do atendimento, Seleção da comanda, Comanda compartilhada, Pedido atual, Link do cliente |
| Mandar o cardápio | Enviar cardápio | Link de compra, Fotos como segunda rota |
| Mandar a cobrança | Cobrar | Gerar cobrança, Link do pedido |
| Fechar o atendimento | Concluir conversa | Encerrar (4 botões) |
| Passar para outra pessoa | Passar conversa | Transferir, ícone sem rótulo |
| A IA | Assistente | Agente, Automático, Automáticas, IA assumir atendimento, agente do EasyStok |
| Quem responde | "Assistente respondendo" / "Você respondendo" | Modo, Automático |
| Regra fixa (regex) | Dica | cartões de regra dentro do balão da IA |
| Termos internos | proibidos na tela | Canhoto, Lead, Peça, esteira, Console, Hall, Trilho, "a dona" (13 ocorrências) |

### 3.2 Dicionário de ícones (uma biblioteca, um sentido)

Adotar **`lucide-react`**: é o mesmo traço que já está na tela, mais de 40 chaves já são caminhos copiados dele, e cada ícone tem nome canônico. `Icone` vira uma fachada fina sobre ele, as chaves passam a ser o NOME DO CONCEITO e o `MarcaIcone` é absorvido.

| Conceito | Lucide | Material Symbol (referência) | Hoje |
|---|---|---|---|
| Configurações | `settings` (engrenagem) | settings | `automacao` (sliders) |
| Buscar | `search` | search | `lupa` e `search`, duplicados |
| Filtrar | `list-filter` | filter_list | não existe |
| Mais opções | `ellipsis-vertical` (⋮) | more_vert | `ellipsis` horizontal |
| Voltar | `arrow-left` | arrow_back | sem cabeçalho no celular |
| Fechar | `x` | close | `fechar` e `x`, duplicados |
| Anexar | `paperclip` | attach_file | `anexo` |
| Enviar | `send-horizontal` | send | 2 ícones no composer |
| Áudio | `mic` | mic | ok |
| Fixar / Arquivar | `pin` / `archive` | push_pin / archive | fixar não existe |
| Passar conversa | `user-round-plus` | person_add | `user-plus` sem rótulo |
| Nota interna | `sticky-note` | sticky_note_2 | `nota` |
| Cliente | `user-round` | person | `ficha` |
| Pedido | `shopping-bag` | shopping_bag | `shopping-cart` e `package` |
| Cobrar | símbolo oficial do Pix (BCB) + `credit-card` | não existe `pix` | `dollar-sign` |
| Cardápio | `book-open` | menu_book | `cardapio` e o ícone do módulo |
| Entrega | `bike` | two_wheeler | `moto` |
| Concluir conversa | `circle-check` | check_circle | `log-out` |
| Sair da conta | `log-out` (só no menu da conta) | logout | `log-out` (14 usos) |
| Lembrete / Programar | `alarm-clock` / `calendar-clock` | alarm / schedule_send | `relogio` (7 sentidos) |
| Assistente (IA) | `sparkles` | auto_awesome | `agente` (robô) |
| Ajuda | `circle-help` | help | `ajuda` |

### 3.3 Componentes que passam a ser obrigatórios

| Componente | Regra | Substitui |
|---|---|---|
| `Tooltip` | hover e foco, atraso de 500 ms, "Verbo + objeto" e o atalho; nunca em toque | 100 `title` nativos |
| `BotaoIcone` | exige `rotulo`; gera o aria-label e o Tooltip sozinho; alvo de 44 px no toque | 54 botões só-ícone |
| `Aviso` (snackbar) | 3 tons, com "Desfazer" opcional, 1 por vez, no pé da tela | 6 mecanismos de feedback |
| `Selo` | status = cor + ícone + texto; no máximo 4 estados de conversa | cerca de 25 selos à mão |
| `Folha` | painel lateral no desktop e bottom sheet no celular | 5 overlays próprios, modal sobre modal |
| `Modal` | 3 larguras (P, M, G), rodapé "Cancelar" + primário à direita | 14 larguras |
| `Vazio` | frase + próxima ação | estados vazios mistos |

**Guardas (testes de teto, padrão que o repo já usa):** nenhum `<button>` cru em `features/atendimento` e `features/caixa-de-entrada`; o `Icone` só aceita chave do dicionário; nenhum termo proibido em JSX; nenhuma duração ou easing literal em CSS. O teto começa no número atual e só desce.

### 3.4 Movimento

| Uso | Transição | Token |
|---|---|---|
| Trocar de conversa | fade through | 150 ms padrão |
| Lista → conversa (celular) | eixo compartilhado horizontal | 220 ms ênfase |
| Abrir Folha ou Modal | entrar subindo, SAIR descendo (hoje não há saída) | 220 / 150 ms |
| Aviso | fade | 150 ms |
| Urgência | 1 pulso ao chegar, depois estático | zera as 6 animações infinitas |

### 3.5 Copy

- Botão = verbo + objeto ("Cobrar R$ 84,00", "Enviar cardápio"). "Tentar de novo" é a forma única.
- Dispensar: **Cancelar** desiste do diálogo, **Fechar** sai de painel sem mudança, **Voltar** é só navegação. Em diálogo cujo assunto é cancelar, a dispensa vira "Manter pedido".
- Erro sem culpa e sem "Ops!": o que houve e o que fazer. Estado vazio sempre com a próxima ação.
- A voz da Casa vale só no que o cliente vê; o console fala em operação.

## 4. O Balcão estilo WhatsApp (Onda 2)

```
DESKTOP (>= 1280)                                      CELULAR (APK, < 600 dp)
+-----------+-------------------------+----------+     Conversas  ->  Conversa  ->  Cliente
| Conversas | Fulana  · respondendo ⋮ | Cliente  |     (lista)       (Voltar)      (folha)
| [busca]   |-------------------------|  Pedido  |     barra inferior: Conversas · Pedidos · Mais
| Todas     |  mensagens              |  Entrega |
| Não lidas |                         |  Cobrar  |
| Precisa   |-------------------------|          |
| Aguard.$  | 📎 [ / respostas  ] 🎤➤ |          |
+-----------+-------------------------+----------+
```

| # | Mudança | Hoje (medido) | Meta |
|---|---|---|---|
| 1 | Títulos visíveis nas 3 colunas: Conversas, a conversa e Cliente | nenhuma coluna tem título no desktop | 3 títulos |
| 2 | Chips de filtro: Todas, Não lidas, Precisa de você, Aguardando pagamento | chips cortados em 1280 e "Precisa de você 11" com 8 destacados | 4 chips, contador = destaque |
| 3 | Cartão de conversa como no WhatsApp: avatar, nome, prévia, hora, contador e 1 selo | 8 ou mais elementos por cartão | 5 elementos |
| 4 | Clicar no nome ou no avatar abre o Cliente | 3 gestos, 317 px de rolagem e 2 fechamentos | 1 toque |
| 5 | Composer: clipe, "/" para respostas (lista do tamanho da tela), áudio que vira enviar | 2 ícones de enviar e respostas num popover de 148 px | 1 enviar, "/" filtrando |
| 6 | Enviar cardápio no clipe e no "/cardapio" | 3 gestos e 2 rotas | 1 gesto |
| 7 | Pedido numa Folha sobre o Cliente, sem trocar a lista; ordem fixa Itens → Entrega → Pagamento → Cliente (#1766 itens 2, 4 e 5) | o cardápio troca a lista e a conversa encolhe 27% | conversa intacta |
| 8 | Cobrar com prévia do que o cliente vai receber | 6 gestos no desktop e 8 no celular, link sem prévia | 3 e 4 gestos |
| 9 | ⋮ do cabeçalho com Passar, Lembrete, Nota, Concluir e Fixar; 1 só "Concluir" com Desfazer (#1766 item 3) | 2 a 4 "Encerrar", modal tipo PDF | 1 ação + Desfazer |
| 10 | Celular: a pilha lista → conversa → Cliente usa o histórico, e o Voltar do Android volta | Voltar sai do app | Voltar volta |
| 11 | Celular: alvos de 44 px, cabeçalho de 56 px, sem subcabeçalho fixo | 10 de 21 alvos pequenos, cabeçalho de 132 a 176 px | 0 pequenos, mensagens com 75% ou mais |
| 12 | Configurações: uma porta, ⚙ no menu da conta, com a seção Atendimento dentro | 4 portas | 1 |

## 5. Assistente único (Onda 3)

Modelo mental: **um nome (Assistente), um ícone (✨) e um lugar.** O selo ✨ aparece só em texto que um modelo escreveu.

| # | O que | Já existe | Esforço |
|---|---|---|---|
| 1 | Resumo da conversa no topo: "quer 2 bolos para sábado, falta o endereço" | molde em `ResumirClienteUseCase` | P |
| 2 | Rascunho fantasma no composer, aceito com Tab, regerar ou descartar em 1 toque; funde "Sugerir resposta" e o completar | os dois existem separados | P/M |
| 3 | Chips de próxima ação (Enviar cardápio, Cobrar, Pedir endereço) | na mesma chamada do resumo | M |
| 4 | Extrair o pedido da conversa para a comanda, com os campos destacados e confirmação | `adicionarItem` existe; falta a proposta `propor_itens_no_pedido` | M |
| 5 | Cartões de regra viram "Dica", sem selo de IA; sai o cartão "Exemplo de análise de foto" | regex locais | P |
| 6 | Linha do tempo do que o Assistente fez por turno, com teto de custo diário | só `UsoIa` soma | M |

Fica como está: o atendimento automático segue desligado por padrão (#1889). Ligar o modo copiloto (o Assistente escreve, a pessoa aprova) é uma decisão posterior, depois do item 2 medido.

## 6. Consertos já (Onda 0: fixes de tier baixo, antes de tudo)

| # | Defeito | Evidência | Risco |
|---|---|---|---|
| 1 | O campo envia como pessoa com o Assistente atendendo, e o aviso "Assumir" fica fora da tela (y = -228 px) | B-02 | ALTO: duas vozes falando com o cliente |
| 2 | Falha no turno do agente só vai para o log e a conversa não escala (`ProcessarTurnoAgenteUseCase.cs:26-30`) | D | ALTO: cliente sem resposta |
| 3 | Dois classificadores de "precisa de você" (servidor e regex): o contador dá 11 e só 8 ficam destacados | B-61, D | médio |
| 4 | A tela abre numa conversa fora da fila e o foco não vai para o campo | B-01, B-04 | +1 clique por resposta |
| 5 | Loja no topo do celular: alvo de 32 x 44 px só-ícone; fora do horário fecha num toque (dentro do horário pede justificativa, conferido em `expediente.js:102`) | B-62 | médio |
| 6 | O completar com Tab manda a conversa ao provedor sem chave para desligar | D | privacidade |
| 7 | Botão fechar do cardápio sem nome acessível | B | baixo |

## 7. Como saber que deu certo

| Métrica | Hoje | Meta | Onda |
|---|---|---|---|
| Gestos para montar e cobrar (desktop / celular) | 6 / 8 | 3 / 4 | 2 |
| Gestos para abrir o Cliente | 3 + 2 fechamentos | 1 | 2 |
| Gestos para enviar o cardápio | 3 | 1 | 2 |
| Controles visíveis em 1280 | 42 a 45 | 30 ou menos | 2 |
| Alvos abaixo de 44 px no celular (conversa) | 10 | 0 | 2 |
| Animações infinitas | 6 | 0 | 1 |
| Nomes da IA na tela | 6 a 8 | 1 | 1/3 |
| Ícones com mais de 1 sentido | relógio 7, check 6, log-out 2 | 0 | 1 |
| Botões só-ícone sem Tooltip | 37 | 0 | 1 |
| SUS com atendentes (3 rodadas de 5, tarefas cronometradas) | não medido | 80 ou mais; responder em 15 s ou menos | 5 |

A meta de SUS e de tempo é proposta minha, não vem de fonte. A média de mercado do SUS é 68.

## 8. Ordem, PRs e tier

| Onda | Entrega | PRs | Tier |
|---|---|---|---|
| 0 | Consertos da seção 6 | 4 fixes (1 por tema) | baixo |
| 1 | ADR "Linguagem do console" + este plano versionado em `docs/plan/balcao-ux/` | 1 | ALTO (precisa de `aprovado`) |
| 1 | `lucide-react`, dicionário, Tooltip, BotaoIcone, Aviso, Selo, tokens de movimento e guardas | 2 | ALTO (`package.json`); depois baixo |
| 1 | Vocabulário e copy aplicados no Atendimento | 1 | baixo (spec aprovada) |
| 2 | Estrutura WhatsApp, itens 1 a 12 | 4 (lista, conversa e composer, Cliente e Pedido, celular) | baixo (spec aprovada) |
| 3 | Assistente, itens 1 a 6 | 3 (front, back do resumo e rascunho, extração) | baixo, exceto migração se houver |
| 4 | Mesma gramática nos outros módulos (#1777) | por módulo | depois da validação |
| 5 | Teste com atendentes + `scripts/ux/medir-balcao` versionado | 1 | baixo |

Com o plano aprovado e versionado, as PRs das ondas 1 a 3 viram "spec de plano aprovado" e saem do tier alto. O roteiro de medição da banca B entra na Onda 1 para que todo antes e depois seja comparável.

**Fora do escopo:** o APK piloto (#2129, outra frente, que consome a mesma gramática depois), os módulos congelados, ligar o atendimento automático e qualquer biblioteca nova além de `lucide-react`.

## 9. Decisões que só o Felipe toma

| # | Decisão | Recomendação | Alternativa |
|---|---|---|---|
| 1 | Nome na tela | **Atendimento → Conversas / Conversa / Cliente**, como no WhatsApp | manter "Balcão" como nome da lista |
| 2 | Biblioteca de ícones | **lucide-react** (mesmo traço, troca invisível) | Material Symbols Rounded (cara de Android e Gmail, troca o visual inteiro) |
| 3 | Ícone da IA | **✨ sparkles** (convenção de mercado) | manter o robô |
| 4 | Pedido no desktop | **Folha sobre o painel Cliente**, com a lista sempre visível | manter o cardápio no lugar da lista |
| 5 | #1766 (6 itens de 13/10) | **aceitar todos dentro da Onda 2** | decidir item a item |
| 6 | Completar com Tab | **manter, com chave de desligar e aviso de privacidade** | fundir com o rascunho só sob demanda |

## 10. Sprint transversal de linguagem e controles (#2140)

Pedido do Felipe em 28/10/2026: revisar a harmonia do sistema inteiro com banca e contrabanca, tomando Produtos e Pedidos do piloto PWA como referência. Esta execução não encerra as ondas anteriores nem substitui seus aceites.

### Decisões aplicadas

- Transferir a clareza e a hierarquia do piloto, sem normalizar rótulos automaticamente. Ações ambíguas conservam verbo e objeto; valores, consequências e permissões permanecem explícitos.
- Usar Conversas, Conversa e Cliente na navegação; Enviar cardápio e Concluir conversa nas ações; Assistente para quem responde. Contato substitui o jargão Lead sem prometer recência do cadastro.
- O componente `Botao` compartilha altura mínima de 40 px no desktop e 48 px em telas estreitas ou ponteiro de toque. O Composer deixou de impor exceções de 36/40/44 px. Campos, foco, temas e estados desabilitados seguem os tokens existentes.
- Secundários tonais, campos sem sombra interna e modais sem desfoque decorativo. Mantidos contraste, foco por teclado, proteção de rascunho e cores semânticas.
- Configurações usa engrenagem; Assistente usa sparkles; opções usa reticências verticais; concluir conversa deixa de usar o ícone de sair da conta. Sem nova dependência.
- Prazo de resposta vencido mantém trilho vermelho, motivo e texto acessível, com seleção e hover distinguíveis. Removido o pisca contínuo.
- No celular, o cabeçalho global fica em Conversas. Loja, sino e Mais continuam acessíveis ao voltar à lista pela barra inferior; deixam de ocupar espaço permanente em Conversa e Cliente. A troca de abas conserva conversa e rascunho.
- Concluir conversa mostra mensagem e consequências primeiro; avaliação, anotações e relatório ficam em Ver resumo da conversa. Na leitura de uma conversa anterior, o resumo começa aberto. Imprimir e os dados do relatório foram preservados.

### Banca e contrabanca

A banca revisou linguagem, componentes e fontes dos módulos. A contrabanca leu o diff e as capturas, contestando as propostas antes do fechamento:

| Achado | Correção |
|---|---|
| Compactação do piloto esconderia objeto e risco da ação | Não importar o normalizador; manter nomes explícitos |
| Remover pedido sugeria cancelamento, mas a operação apenas retirava da viagem | Retirar da viagem |
| Mensagem de permissão prometia consulta que o ramo não renderiza | Orientação à administração sem promessa falsa |
| Engrenagem extrapolava viewBox | Vetor corrigido dentro de 2..22 |
| Em 320 px, Enviar quebrava para terceira linha e Assumir para segunda | Composer e faixa ajustados preservando alvos de 48 px |
| Cabeçalho e prévia de relatório consumiam área de trabalho | Cabeçalho contextual e resumo expansível |

### Cobertura e limites

| Área | Revisão desta rodada |
|---|---|
| Componentes, navegação, Atendimento, Cliente e conclusão | Código, build e navegador local em modo demonstração |
| Início, Caixa, Configurações, Cozinha e Entregas | Código e navegação local de demonstração; capturas apenas após conteúdo montar |
| Gestão: cardápio, categorias, produção, insumos, receitas, calculadora, planejamento, perdas, impressão, integrações, respostas e avisos | Trechos alterados revisados no fonte; compilação e verificações do Console |
| Login | Leitura do fonte; sem alteração prioritária |
| API, perfis reais, dispositivos físicos e produção publicada | Não homologados nesta rodada; dependem de ambiente/sessão identificados |

As evidências locais ficam em `.build/harmonia-ux/`, fora do versionamento. A medição por viewport no navegador não equivale a prova em aparelho Android físico.

### Validação

- `npm run qualidade`: lint, camadas, verificações de moeda, 264 pares de contraste nos dois temas e build.
- Gate do repositório: build .NET e 60 testes de arquitetura aprovados.
- Regressões de sincronização, clareza, SLA, acessibilidade e composição visual executadas. Após as falhas de nomenclatura no primeiro CI, executada toda a bateria do workflow do Console: 97 scripts aprovados localmente. Expectativas de nomes atualizadas sem alterar contratos funcionais, revisadas pela contrabanca.
- A prova antiga #1870 conserva duas falhas de asserção já reproduzidas no baseline `f60107af8`: composição da faixa e implementação de Escape no menu da conta. Nenhuma foi introduzida por este sprint.
- No Vite de desenvolvimento, os diálogos abriram e desmontaram; no build de produção local, abriram normalmente. A hipótese é o evento close enfileirado durante a dupla montagem do StrictMode; a lógica de Modal não foi alterada nesta rodada.

A revisão integral por perfil e a aceitação estética do Felipe permanecem abertas. As ondas de Tooltip, BotaoIcone, Folha do pedido e navegação Android do plano continuam com seus próprios aceites.
