# Sprint de design e atendimento, 12/10/2026

Solicitação do Felipe e issue #1670. Abrange API, Console e comanda pública do repositório `casa-da-baba`.

## Resultado esperado e personagens

Cliente da sprint: contato sintético, dois pratos, endereço completo com complemento. Atendente da sprint: conta de teste com permissão de Atendimento. A mesma seleção deve aparecer para ambos, sem duplicação do pedido ou cobrança.

Todo cliente pode iniciar e continuar o atendimento. Cobertura automática determina se o sistema consegue cotar a entrega, não se a Casa atende a pessoa. Fora da cobertura, preservar seleção e endereço, encaminhar à equipe e combinar frete antes de cobrar. Valor zero só com decisão humana explícita ou cotação válida.

## Matriz de cenários

| ID | Cenário | Aceite verificável | Evidência/estado |
|---|---|---|---|
| C01 | Cliente cadastrado abre comanda nova | Endereço padrão já preenchido | Regressão PostgreSQL aprovada |
| C02 | Rascunho anterior já tem itens | Endereço ausente recebe o padrão sem apagar itens | Regressão PostgreSQL aprovada |
| C03 | Lead sem cadastro abre o link | Consegue selecionar; nome, telefone e endereço são solicitados na entrega | API e navegador aprovados |
| C04 | Lead confirma dentro da área | Cadastro e endereço persistidos; dois itens de R$ 10 + frete R$ 5 = R$ 25 | Regressão PostgreSQL aprovada |
| C05 | CEP fora da área automática | Seleção e endereço preservados; fila humana; nenhuma cobrança ou frete grátis inventado | Regressão PostgreSQL aprovada |
| C06 | Equipe combina frete de R$ 13,50 | Total R$ 33,50; cliente confirma; uma cobrança | Regressões aprovadas |
| C07 | Pedido automático | Ferramentas executam; resposta reflete pedido e pagamento reais | Aplicação e navegador com ferramentas controladas aprovados |
| C08 | Semiautomático | Sugestão revisada; envio só após ação da atendente | Provas do Console e testes da aplicação aprovados |
| C09 | Chat interno: “assuma o atendimento” | Ação executável retoma automático e agenda entrada pendente | Tradução, executor do Console e enfileiramento aprovados |
| C10 | Link no meio da conversa | Não reiniciar com “Oi”; número da comanda acompanha o link | Prova do Console aprovada |
| C11 | Mudança após frete combinado | Valor anterior não autoriza outra revisão; equipe precisa conferir novamente | Regressões aprovadas |
| C12 | Repetição, conflito, outro cliente/empresa | Sem cobrança duplicada, sem sobrescrever edição e sem acesso alheio | Regressões, concorrência e replay no navegador aprovados |

## Achados confirmados e correções

1. **Cadastro bloqueava a abertura.** `EstadoAsync` recusava conversa sem ClienteId. Permitir leitura e seleção do lead; validar nome, telefone e endereço na confirmação; criar o cadastro do lead sem vincular uma identidade anterior apenas pelo telefone informado em link público.
2. **Rascunho com itens não recebia endereço.** O preenchimento ocorria apenas quando não havia rascunho. Completar endereço ausente com o padrão do cadastro, preservando seleção e endereço digitado.
3. **CEP sem cobertura bloqueava a compra.** A página exigia cotação coberta e o núcleo lançava `CepSemCoberturaException`. A confirmação passa a preservar a comanda e encaminhar à equipe, sem pedido financeiro ou cobrança prematuros.
4. **Não havia como combinar frete na comanda.** Acrescentar campo no Atendimento e ação autenticada. Exigir valor explícito, inclusive para frete zero. Guardar valor ligado ao link e à revisão; o cliente precisa confirmar o total. Repetir a mesma ação não duplica nota; outra revisão invalida o valor.
5. **Endereço manual perdia cidade/UF.** O cadastro da conversa não transportava esses campos ao validador. Preservar os campos fornecidos quando o serviço de CEP estiver indisponível.
6. **IA interna não tinha ação de assumir.** Acrescentar ferramenta de proposta e botão “IA assumir atendimento”, executando a retomada na conversa selecionada. Não anunciar ação executada antes da confirmação.
7. **Retomar automático não processava entrada já recebida.** Enfileirar o turno após persistir a transição. A regra existente de entradas pendentes continua evitando respostas repetidas.
8. **Convite recomeçava a conversa.** Remover saudação no envio durante o atendimento; manter variante de abertura explicitamente separada.
9. **Agente tratava fora da área como recusa.** Atualizar orientação para acolher, confirmar e preservar o endereço, encaminhando entrega e frete à equipe.

10. **Falha da retomada podia aparecer como sucesso.** A camada do Console ocultava o erro HTTP. Devolver confirmação explícita da API e manter a ação em falha quando a retomada não for confirmada; prova com erro HTTP real na camada do Console.
11. **Resposta pública ainda recusava o CEP.** A checagem de produção encontrou “Não entregamos neste CEP.” no detalhe HTTP 422. Substituir pela orientação de combinar entrega e frete; preservar o status e o contrato de cotação, que não confirma uma tarifa automática inexistente.
12. **Frete automático sem configuração em produção.** Consulta somente leitura em 12/10/2026 confirmou cozinha sem coordenadas, fator de rota, distância grátis, raio máximo e faixas nulos. Existe apenas a zona ativa “TESTE SANDBOX (temporaria)”, CEPs 05500000–05500999, R$ 8. Os CEPs 01310100 e 20040002 retornam 422. A tarifa real e a origem dependem dos dados da operação, solicitados ao Felipe; não foram inventados nem alterados. Fora do limite automático, manter o fluxo humano já publicado.

## Retorno visual: rolagem e hierarquia do pedido

O retorno do Felipe mostrou o resumo do cliente excedendo a tela e a seleção compartilhada tomando o lugar do pedido no Atendimento. Pedido confirmado e seleção em montagem passam a ter apresentações distintas:

- A coluna direita volta a se chamar **Pedido**. O pedido confirmado aparece primeiro; o histórico do link fica recolhido. Uma alteração em edição ou aguardando conferência continua aberta.
- Antes da confirmação, o bloco se chama **Pedido em montagem**, com número da comanda como referência, total antes dos itens, quantidade explícita e observações recolhidas com prévia do texto existente.
- O painel do Atendimento usa uma única rolagem. A edição local usa a memória da visita autenticada já existente, preservando o rascunho ao trocar de conversa ou de tamanho de tela.
- No link do cliente, o resumo no computador cabe na área visível, com cabeçalho e total/ação acessíveis enquanto os pratos rolam. No celular e em telas baixas, mantém a rolagem normal da página. “Conferir entrega” leva o foco ao endereço.

Validação do retorno: seis execuções da regressão com 12 itens (desktop, iPhone SE e Pixel 7, duas vezes cada); navegação por teclado, total e ação visíveis, endereço alcançável e ausência de overflow horizontal. No Atendimento, navegador com quatro pratos de nomes longos comprovou uma rolagem, observações recolhidas e edição preservada entre desktop e celular. Qualidade do Console, prova de comanda compartilhada, 25 cenários de UX, 39 testes unitários da página, lint/build do site e gate com 42 testes de arquitetura aprovados. A prova visual do painel usa seleção controlada; as jornadas com API/PostgreSQL locais são verificadas separadamente.

## Rodada complementar desktop com banca, issue #1705

Solicitada pelo Felipe após o retorno visual. A PR #1702 foi preservada e integrada por outra sessão durante a validação; esta rodada foi atualizada sobre o master que a contém. Não altera larguras das colunas, posição do cardápio, regras de pedido, pagamento, frete ou atendimento. CSS condicionado a largura desktop e ponteiro preciso, com proteção do comportamento mobile.

| Achado | Correção e comprovação |
|---|---|
| Observações vazias ocupavam espaço demais | Resumos fechados sem conteúdo passam de 44 para 32 px. Prévia de observação existente e edição continuam disponíveis. Enter abre o campo e Tab chega ao input. |
| Link duplicado no cabeçalho e no painel | No desktop, permanece a ação do cabeçalho da conversa. A duplicata do painel fica oculta. |
| Histórico aberto parecia parte do pedido atual | Recuo e borda agrupam os dados sob o título do histórico. |
| Fila de impressão competia com o pedido e ocultava o contador | Superfície clara e quebra dos comandos quando falta largura. Na coluna de 320 px o rodapé cresce de 57 para 104 px, mantendo contador e ações legíveis. |
| Bordas dos campos públicos pouco distinguíveis | Borda desktop `#9a856f`, contraste de 3,27:1 e 3,47:1 contra os fundos utilizados. |
| Foco do resumo cortado pelo contêiner | Contorno de foco voltado para dentro, visível nos quatro lados; rolagem por teclado e acesso à entrega conferidos. |

Banca independente: hierarquia, fluxo e acessibilidade aprovaram o escopo selecionado, sem bloqueadores. Aceite técnico restrito a este polimento, não substitui o aceite humano em uso real. A copy global do histórico e da cobrança foi registrada como oportunidade fora desta rodada desktop. Os possíveis problemas de foco após confirmação e ARIA do redimensionamento não foram reproduzidos e não são declarados corrigidos.

Evidência: componentes reais `ComandaDoCliente`, `FilaCanhotos` e `Botao` em navegador, com seleção controlada de quatro pratos longos e histórico pago, antes/depois em 1280, 1440 e 1920 px (colunas 320, 420 e 520 px). Sem overflow horizontal ou erros de página. Em 1440, o excesso de rolagem da montagem caiu de 129 para 74 px; em 1280, de 299 para 238 px. O caso de 375 px/ponteiro de toque manteve as métricas. As capturas comprovam composição e estilos, não a execução de operações pela API.

Validação: qualidade do Console (lint, camadas, 264 pares de contraste e build), prova de comanda compartilhada, 16 verificações da rodada #1697; site com lint/build, regressão existente de rolagem nos três viewports e prova desktop adicional de borda/foco. Gate e estado de publicação finais ficam no recibo da [issue #1705](https://github.com/michel-az-de/EasyStok/issues/1705).

Evidências locais desta rodada: `C:/rep/casa-da-baba/test-results/banca-desktop/` (capturas e `metricas.json`), `banca-scroll/` e `banca-foco/`. Alterações do site limitadas ao CSS da comanda; WIP prévio de documentação/configuração preservado.

## Copy por situação

| Situação | Texto/direção |
|---|---|
| Primeiro contato | Saudação inicial existente; apresentar a Casa sem pressupor compra anterior |
| Conversa em andamento | “Aqui está sua comanda: [link]. Escolha os pratos, confira a entrega e acompanhe por aqui.” |
| Lead | “Informe seus dados para a Casa acompanhar seu pedido. Seu endereço será salvo no cadastro.” |
| Fora da cobertura | “Vamos atender você. Envie sua seleção e a Casa combina a entrega e o frete na conversa.” |
| Encaminhado | “Sua seleção e seu endereço chegaram à Casa. Vamos combinar a entrega e o frete na conversa. Ainda não há cobrança.” |
| Frete combinado | Informar o valor real e pedir confirmação do total; não prometer prazo que ainda não foi combinado |
| Semiautomático | Mostrar que é sugestão e que ainda não foi enviada |
| Falha de cotação | Mostrar falha e permitir nova tentativa; não confundir indisponibilidade técnica com CEP fora da área |

## Validação e publicação

Testes com PostgreSQL descartável e provedores controlados. `SEED_DEMO_DATA=false` evita a carga de demonstração grande na bateria. Houve uma disputa de arquivos entre execuções no Windows; a execução antiga foi encerrada antes do reteste sequencial.

Não confundir ferramentas controladas com entrega real por WhatsApp, pagamento real ou qualidade de um modelo estocástico. Conferir separadamente navegador, gate, CI/review, merge, backup, SHA servido da API, Console e site. O estado da publicação e os SHAs servidos são registrados no fechamento da [issue #1670](https://github.com/michel-az-de/EasyStok/issues/1670) e na PR vinculada; este relatório não atesta deploy por si só.

Evidências locais:
- Aplicação: 721 testes aprovados em Atendimento e CheckoutCoreService.
- Gate: build e 40 testes de arquitetura aprovados.
- Integração com o master antes do merge: build e 42 testes de arquitetura aprovados; árvore local idêntica à árvore do squash publicado.
- Site: 683 testes unitários, lint e build aprovados; comanda em desktop, Pixel 7 e iPhone SE aprovada com mocks.
- Navegador com API/PostgreSQL reais locais: lead fora da área, cadastro e endereço, combinação humana do frete, confirmação R$ 33,50; jornada existente de automático, pagamento, acréscimo, cupom, estorno e mesma comanda aprovadas. Provedores externos controlados.
- Console: qualidade, prova de ações do assistente, comanda compartilhada e 25 cenários de UX aprovados.
- Ajustes na regressão de navegador: abrir observação recolhida antes de digitar; aguardar o ciclo real de atualização do painel (até 20 segundos, polling de 15 segundos).
- Alterar a seleção invalida a prévia do frete combinado no site e a autorização da revisão na API.
- Suíte ampla PostgreSQL: 31 testes de comanda aprovados, incluindo quatro clientes/dois atendentes, isolamento por empresa, concorrência, replay e respostas verificadas; 1 fixture opt-in ignorada.
- CI do site b376794: 683 testes unitários e 168 testes de navegador aprovados; 6 fixtures opt-in ignoradas. As duas jornadas com API real local foram executadas separadamente.
- Endpoint de frete: campo omitido retorna 400; valor negativo retorna 400; revisão antiga retorna 409. Reteste HTTP após tornar o valor obrigatório: 2 cenários aprovados.

### Recibo de publicação da correção principal

Publicação em 12/10/2026, após a PR #1679: API e Console servindo `fa307f987bc2dbbb046319136a9078c5874073c7`; site servindo `b37679442d30b1e46646040665c6e596f4b14669`. Conferidos externamente em `/health/version`, meta `easystok-console-sha` e `/version.txt`. Os três scripts canônicos concluíram com código zero.

Backup PostgreSQL anterior ao deploy: `/home/felipe/backups/easystock-predeploy-20261012T231456Z.dump`, 722.679 bytes, tamanho confirmado na VPS. Backup do Console: `/opt/stacks/shared/docs/easystok-console/index.html.antes-20261012T232144Z`. Site com rollback `casadababa-storefront:vps-prev-20261012T232135Z`.

O código de atendimento e frete combinado está publicado. A configuração operacional do frete automático permanece pendente dos dados da cozinha e das tarifas. Testes de provedores controlados não atestam envio real de WhatsApp ou pagamento real em produção.

## Correção funcional após a banca desktop (#1712)

Base conferida no início da rodada: API e Console `c614920b0d759fc5a0e6a1ccf33e87bc97c6d684`; site `fd288ed91873ece914865bab04bbef166a4730e1`. O polimento anterior não comprovava os caminhos sem confirmação. A prova antiga de CEP externo escolhia uma janela válida antes de encaminhar; o teste unitário verificava apenas a cotação e a mensagem.

| Cenário que faltava | Correção e critério de aceite |
|---|---|
| Lead pede ajuda sem itens, endereço ou pedido | `POST pedir-ajuda` sem body registra mensagem interna, escala a conversa e atualiza o retrato por SSE. Não salva nem substitui o rascunho. Repetições simultâneas são idempotentes; uma nova solicitação é possível após retomar o automático. |
| CEP distante sem janela | `POST encaminhar-entrega {revisao}` reaproveita cadastro/endereço e encaminha a seleção, sem criar pedido ou cobrança. O horário é opcional nessa etapa; dados incompletos não impedem pedir ajuda. |
| Encaminhamento registrado | Cliente vê que aguarda atendimento, pode rever endereço e continuar editando. Alteração efetiva da seleção permite novo envio. |
| Resumo depois de encaminhar | A inspeção visual revelou rolagem interna retida e apenas 20,9 px para os pratos. O resumo agora reserva espaço útil além do cabeçalho/rodapé e volta ao primeiro item. Teste exige nome integralmente visível e pelo menos 150 px de conteúdo. |
| Casa escolhe horário e combina frete enquanto cliente mantém página aberta | Site sincroniza o horário, recarrega as opções e limpa a orientação antiga. Frete combinado não se transforma em frete grátis nem permanece como entrega pendente. |
| Atendente precisa concluir o primeiro pedido | Edita a mesma seleção, escolhe janela, salva e combina frete explícito, inclusive zero. A confirmação assistida exibe itens, endereço, horário, frete e total. API protege revisão, empresa/conversa, consumo do link e repetição. Cobrança fica para ação posterior. |
| Localizar Lalamove antes de existir pedido | Acesso ao [web app oficial](https://web.lalamove.com/) junto à entrega. Consulta e contratação são externas; depois de criar o pedido e montar a viagem, `Registrar corrida` guarda empresa, custo e motorista. |
| Empresa real abre comparação de transportes | Contexto informa contratação externa com ação direta e acesso a Entregas, sem terminar em erro de laboratório. Empresas de demonstração continuam explicitamente simuladas; guards de contratação não foram removidos. |

Contratos novos: o retrato da comanda acrescenta `ajudaSolicitada` e `ajudaSolicitadaEm`; atendimento autenticado ganha `/{linkId}/encaminhar-entrega` e `/{linkId}/confirmar-pedido`, ambos com `{revisao}`. Contexto de logística acrescenta `indisponibilidade`. Sem migração, nova dependência ou mudança nas permissões existentes.

Validação local concluída em 13/10/2026: 690 testes do site (46 focados na comanda), seis cenários de navegador de seleção/rolagem, qualidade completa do Console, oito provas de domínio, dez de renderização do painel e gate com build e 42 testes de arquitetura. Testes de integração cobrem empresa, revisão, repetição concorrente, ajuda sem rascunho e confirmação assistida; 19 testes de logística e a prova de navegador verificam o contexto real sem acionar prestadores.

Duas jornadas de navegador passaram com API/PostgreSQL reais descartáveis: lead pede ajuda sem itens, informa CEP distante sem janela, atendente combina horário e frete de R$ 13,50 e confirma o mesmo pedido de R$ 33,50 sem gerar cobrança; a regressão independente cobre automático, pagamento, acréscimo, cupom e estorno. Banca de fluxo, hierarquia e acessibilidade aprovada após corrigir a preservação do horário em conflitos, a duplicidade da seleção vazia e a reativação indevida da ajuda ao reassumir a conversa.

Última conferência visual: ajuda resumida em uma linha, Lalamove visível junto ao endereço e pratos/horário antes do formulário de frete. Removidas instruções repetidas que empurravam os itens para fora da primeira tela.

Pagamento, IA, WhatsApp e prestadores externos foram controlados. Nenhuma corrida ou cobrança real foi contratada, e a jornada autenticada não foi exercitada em produção. Recibo de CI e publicação fica na [issue #1712](https://github.com/michel-az-de/EasyStok/issues/1712).
