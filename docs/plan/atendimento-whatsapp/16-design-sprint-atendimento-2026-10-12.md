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
