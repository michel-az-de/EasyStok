# Casa da Baba · EasyStok: levantamento e desenvolvimento em ondas

Data: 09/10/2026. Solicitante: Felipe. Escopo: nova versão dedicada à Casa da Baba.
Base do levantamento: master 6763af349510f84db1c9c9bb7434be039588f164, igual ao origin/master na consulta. Árvore de trabalho limpa antes desta documentação.

## 1. Conclusão e alcance da verificação

Existe uma base ampla de regras, APIs e fluxos operacionais. A nova versão ainda não está fechada como produto: partes estão conectadas ao console, outras continuam no Web antigo, outras são demonstração, e várias correções estão em PR aberta. O principal trabalho é concluir jornadas completas, com UX consistente e integrações comprovadas.

Este levantamento cobre as áreas do produto, os planos M0–M8, o histórico recente, as ações conectadas e não conectadas do console, as PRs abertas e verificações locais. Não é uma certificação linha a linha de todo o ERP, nem homologação de produção. Não foram criados pedidos reais, realizadas cobranças, chamados entregadores ou acionadas impressoras.

Classificação usada:

- **Código integrado:** encontrado no master; não significa publicação nem aceite operacional.
- **Parcial:** existe backend, tela ou parte da jornada, mas o conjunto não está fechado.
- **Em PR:** trabalho localizado fora do master; precisa de revisão e validação na base atual.
- **Pendente:** não encontrado na superfície examinada ou explicitamente bloqueado pelo código/plano. A indicação informa qual superfície foi examinada.
- **Operação não comprovada:** depende de conta, dispositivo ou uso real que não foi exercitado nesta demanda.

Não atribuí percentual de conclusão: contar endpoints ou commits produziria uma medida enganosa do funcionamento da loja.

## 2. Direção já decidida

Fontes: [plano M0–M8](README.md), [ADR-0056](../../adr/0056-erp-da-casa-da-baba-front-unico.md), [ADR-0059](../../adr/0059-web-sai-pwa-continua-e-merge-de-conflitos.md) e [homologação de 07/10](../atendimento-whatsapp/homologacao-2026-10-07.md).

1. Produto voltado somente à Casa da Baba; a pessoa não escolhe empresa para operar. Manter proteção por EmpresaId e RLS enquanto existir essa estrutura.
2. EasyStock.Console é o novo frontend, com hall, módulos e menus próprios. Nome decidido: Casa da Baba · EasyStok.
3. Toda área do EasyStock.Web deve ganhar equivalente no console. Inclui estoque, compras, fornecedores, financeiro, etiquetas e relatórios. O Web só sai de cada área depois da substituição comprovada.
4. PWA de produção e cadastro continua como backup offline. O console permanece online.
5. Edição concorrente PWA/console deve gerar conflito resolvido pela dona. Até a resolução, vale o servidor; não sobrescrever silenciosamente.
6. Perfis pretendidos: Dona, Atendimento e Cozinha. Permissões por módulo ainda precisam ser concluídas.
7. Preservar claro/escuro e as interações já aprovadas. Aplicar identidade Casa da Baba a partir dos ativos válidos; o plano M0 registra que o DS de impressos ainda tinha status de proposta, portanto essa referência não equivale a aprovação visual de todas as telas.
8. Pix e cartão de pedidos pelo link do Checkout Pro; maquininha e vale com registro manual, conforme decisão registrada no plano de pagamentos.

O pedido atual é de levantamento e plano. As ondas abaixo são proposta de execução, não autorização automática para excluir dados, publicar alterações ou efetuar transações.

## 3. Código disponível versus ambiente publicado

Consultas públicas, somente leitura, realizadas em 09/10:

| Superfície | Resultado observado | Consequência |
|---|---|---|
| origin/master | 6763af34 | Referência deste inventário |
| app.easystok.online | HTTP 200; metadado easystok-console-sha = 888eeab7 | Console servido anterior ao master |
| api.easystok.online/health/version | HTTP 200; apiVersion 1.10.0; buildSha = 701dbf4b | API servida em outro commit, também anterior ao master |
| casadababa.com | HTTP 200 | A informação antiga de HTTP 503 não descreve esta consulta; checkout não foi exercitado |

Código mergeado não é necessariamente o código que a dona está usando. Comparar SHA do console e da API é o primeiro passo antes de classificar uma funcionalidade como quebrada ou ausente.

Há documentação desatualizada: AGENTS e arquivos da .knowledge ainda citam diferentes infraestruturas, MAUI e .NET 9. O projeto [EasyStock.Api.csproj](../../../EasyStock.Api/EasyStock.Api.csproj) hoje aponta net10.0, confirmado pela compilação dos testes. O [script do console](../../../scripts/deploy/console-deploy.sh) aponta app.easystok.online e VPS hostinger. Os documentos antigos ficam como histórico, não como evidência operacional atual. Não foi feita auditoria interna da VPS nesta demanda.

## 4. Inventário por área

| Área | O que já existe no código examinado | Falta para a nova versão | Situação |
|---|---|---|---|
| Fundação e navegação | Hall M1–M8, rotas por módulo, menus, login senha/Google e tema claro/escuro | Matriz perfil × módulo, UX de navegação, consistência entre telas de operação e ajuste, entrada por perfil e sessão da cozinha | Parcial |
| Design system | Tokens centrais, componentes comuns, CSS por área, temas e regras de movimento | Identidade Casa da Baba aplicada e aprovada; tipografia, estados, formulários, tabelas, acessibilidade e responsividade uniformes | Parcial; tokens ainda trazem a paleta do protótipo |
| M1 Cardápio | Lista e gestão via API; disponibilidade do dia/site, ordem no servidor, arquivamento, item novo em validação, novidade com prazo e categorias | Fechar ficha de produto, variações/porções, adicionais, combos, alergênicos e coerência do preço/saldo entre canais | M1.1–M1.3 integradas; restante parcial |
| M2 Produção | Backend de produção/lotes/estoque e receitas; ajustes recentes no sync da PWA; lote de papel e cardápio do dia ligados no balcão | Módulo próprio completo, insumos, fichas técnicas, planejamento, peso real, perdas, embalagens, etiquetas e conflitos offline | No master o módulo tem telas vazias; M2.1 em PR; M2.2 em issue |
| M3 Balcão | Conversas, assumir/devolver ao automático, texto, reenvio, cadastro/ficha, tags, notas, respostas, galeria, áudio WhatsApp, sugestão e ações confirmadas do assistente | Fechar alterações do pedido na ficha, ocorrências, reabertura, mudança de endereço/janela, lembretes, modelos aprovados, nova conversa e recuperação de falhas | Ampla implementação, jornada incompleta |
| Pedido e cobrança manual | Comanda por API, geração de pedido, cobrança, troca de forma, registro/desfazimento manual e máquina de estados | Unificar a interação no balcão, pagamentos parciais/acréscimos conforme regra definida, cancelamento/estorno e consistência com cozinha, caixa e estoque | Parcial; provas locais passaram |
| Mercado Pago | Cliente HTTP Checkout Pro, consulta de pagamento, webhook, assinatura, confirmação, estorno e testes | Configuração efetiva, eliminar divergência de chave global/por loja, sandbox completo, retorno à conversa/site, conciliação e teste operacional | Código integrado; operação real não comprovada |
| M4 Cozinha | KDS no modo API, fila, comanda/canhoto, integração de atualização e fila de impressão | Sessão por perfil, prioridades/prazos, expedição e homologação no dispositivo real | Parcial; prova da cozinha passou |
| Impressão | Modelos de pedido/comanda, fila persistida, retorno de sucesso/falha e reimpressão; PDF/canhoto no console | Consumidor da fila conectado ao equipamento, escolha por dispositivo, recuperação de falha, corte/tamanho/acentos e prova no papel | Geração implementada; impressão física não comprovada |
| M5 Caixa | Abrir loja com caixa, movimentos, fechamento, justificativa ao fechar no horário e pagamento manual | Venda avulsa no console, gaveta versus pagamentos eletrônicos, reconciliação de totais, reabertura, histórico e relatórios completos | Parcial; prova do caixa passou |
| Financeiro gerencial | Casos de uso de contas a pagar/receber, parcelas, baixas, estornos, categorias, centros de custo, dashboard e integração com pedidos/compras | UX no console, regras conciliadas com o caixa e tratamento do legado de gateways | Backend reaproveitável; módulo novo incompleto |
| Compras e fornecedores | Casos de uso de fornecedor, listas/pedidos de compra, recebimento e entrada de estoque; histórico de testes de recebimento | Jornada completa no console: necessidade → compra → recebimento → estoque → obrigação financeira | Backend/legado; UX nova pendente |
| M8 Entregas | Janelas, zonas, capacidade, entregadores, viagens, painel e roteiro impresso; geocodificação Google/Nominatim no backend | Configuração utilizável de origem/raio/frete, tratamento de endereço inválido, mapa/rota e conexão real com transportadora | Parcial; TomTom em PR; Lalamove em issue |
| Transportadoras | Fachada de demonstração no frontend para cotar/chamar/cancelar/consultar | Adapter real, credencial, cotação válida, criação, acompanhamento, cancelamento e fallback manual | Simulação não conta como integração |
| Automático e assistente | Processamento do agente, catálogo/caderno, transcrição, sugestão, execução com confirmação e transferência humano/automático | Homologar ciclo completo, coleta de dados, limites das ações, primeira resposta, transferência, erros de canal, horário e reconexão | Parcial; desligar automático transfere à dona no master |
| Notificações | Motor/outbox, tipos/canais, rotinas e avisos; trabalhos recentes de acesso por convite e recuperação | Sino, lembretes e Web Push conectados no console; comprovar entrega dos canais configurados | Backend; PRs de frontend abertas |
| M6 CRM/campanhas | Cadastro/dossiê, tags/consentimento/interesses e backend de campanhas com público e ondas | Listagem CRM, editor de campanha, resultados por venda paga, promoções, fidelidade e cupons de loja | Motor parcial; fidelidade/integrações desabilitadas no modo API |
| M7 Configurações | Horários/respostas/canais distribuídos; usuários e parâmetros no backend | Gestão única de equipe, perfis, chaves, testes de conexão, impressão, loja, backup e operação | Parcial; gestão de chaves em PR |
| Site | Vitrine/checkout e ligação ao atendimento registrados no código/histórico | Homologar catálogo, frete, pagamento, identificação do visitante e retorno do pedido com o console | Integração parcial; lead do chat em PR; repo do site não auditado nesta demanda |
| Continuidade operacional | PWA offline com sync; correções recentes de lotes, descarte, custos e estoque mínimo | Conflitos entre dispositivos, precache/reconexão comprovados, restauração de backup, alerta e recuperação | Parcial; módulo de conflitos ainda é decisão/spec pendente |

### Evidências de código para navegar

- Fundação e identidade: [modulos.js](../../../EasyStock.Console/src/dominio/modulos.js), [tokens.css](../../../EasyStock.Console/src/estilos/tokens.css), [App.jsx](../../../EasyStock.Console/src/app/App.jsx).
- Conexão com backend: [acoesApi.js](../../../EasyStock.Console/src/aplicacao/acoesApi.js), [naoLigadas.js](../../../EasyStock.Console/src/aplicacao/api/naoLigadas.js), [ModalGestao.jsx](../../../EasyStock.Console/src/features/gestao/ModalGestao.jsx), [comandaApi.js](../../../EasyStock.Console/src/infra/api/comandaApi.js).
- Pedido: [PedidoStateMachine.cs](../../../EasyStock.Domain/Sales/PedidoStateMachine.cs).
- Pagamento: [MercadoPagoClient.cs](../../../EasyStock.Infra.Integrations/Pagamentos/MercadoPago/MercadoPagoClient.cs), [MercadoPagoWebhookProcessor.cs](../../../EasyStock.Infra.Async/Pagamentos/Webhooks/MercadoPagoWebhookProcessor.cs).
- Impressão: [ImpressaoController.cs](../../../EasyStock.Api/Controllers/ImpressaoController.cs), [casos de uso de impressão](../../../EasyStock.Application/UseCases/Operacao/Impressao).
- Entregas: [casos de uso](../../../EasyStock.Application/UseCases/Atendimento/Entregas), [provedoresDeEntrega.js](../../../EasyStock.Console/src/infra/provedoresDeEntrega.js), [Geocoding](../../../EasyStock.Infra.Integrations/Geocoding).
- Bastidor: [Financeiro](../../../EasyStock.Application/UseCases/Financeiro), [Fornecedor](../../../EasyStock.Application/UseCases/Fornecedor), [Campanhas](../../../EasyStock.Application/UseCases/Campanhas).
- Backup offline e migração: [ADR-0059](../../adr/0059-web-sai-pwa-continua-e-merge-de-conflitos.md).

### Ações com aviso explícito de não conectadas

A lista naoLigadas.js registra reabrir conversa, alterar janela/endereço do pedido, lembretes, bloqueio de cliente, recebido na entrega, refazer cobrança, avaliações, parte das ocorrências, venda avulsa, integrações, fidelidade e cupons. Também há envio de modelo aprovado e automática da biblioteca ainda bloqueados.

Essa lista é por caminho de interface: janelas e entregas, por exemplo, já têm telas próprias conectadas; reemitir cobrança tem chamada real em comandaApi.js. Portanto uma ação bloqueada na ficha não significa ausência de toda a funcionalidade no ERP. Fechar essa diferença entre caminhos é parte central da UX do balcão.

## 5. Trabalho em andamento a reaproveitar

Snapshot do GitHub de 09/10. PR aberta não é entrega incorporada. Checks aprovados não substituem teste visual ou operação real.

| PR / issue | Trabalho existente | Destino no plano |
|---|---|---|
| [PR 1480](https://github.com/michel-az-de/EasyStok/pull/1480) / #1474 | Revisão visual do pedido e PWA | Revisar na onda 0; absorver nas ondas 1 e 2 |
| [PR 1492](https://github.com/michel-az-de/EasyStok/pull/1492) / #1490 | M2.1 estoque do dia | Onda 7; não iniciar implementação duplicada |
| [Issue 1491](https://github.com/michel-az-de/EasyStok/issues/1491) | M2.2 produção por item com peso real | Onda 7 |
| [PR 1304](https://github.com/michel-az-de/EasyStok/pull/1304) / #1246 | Chaves por loja, teste e vigia | Fundação de integração da onda 3 e reuso na 4 |
| [PR 1323](https://github.com/michel-az-de/EasyStok/pull/1323) / #1322 | Geocodificação e rotas TomTom | Onda 4; conferir provider selecionado |
| [Issue 1247](https://github.com/michel-az-de/EasyStok/issues/1247) | Lalamove e rota | Onda 4 |
| [PR 1401](https://github.com/michel-az-de/EasyStok/pull/1401) / #1399 | Estorno de estoque, descoberto e lote do pedido | Corrigir antes de homologar cancelamento na onda 2; regressão na 7 |
| [PR 1293](https://github.com/michel-az-de/EasyStok/pull/1293) / #1283 | Número do pedido do dia e congelado na comanda | Onda 5 |
| [PR 1425](https://github.com/michel-az-de/EasyStok/pull/1425) / #1424 | Mensagem programada no compositor | Onda 2 e validação do automático na 9 |
| [PR 1428](https://github.com/michel-az-de/EasyStok/pull/1428) / #1426 | Sino, lembretes e Web Push | Onda 2; avisos reutilizados nos demais módulos |
| [PR 1429](https://github.com/michel-az-de/EasyStok/pull/1429) / #1427 | SLA de primeira resposta | Onda 2 e homologação na 9 |
| [PR 1431](https://github.com/michel-az-de/EasyStok/pull/1431) / #1430 | Identificação do lead do site | Onda 2 |
| [PR 1433](https://github.com/michel-az-de/EasyStok/pull/1433) / #1432 | Atendimento por e-mail | Onda 9, após fechar WhatsApp/site |
| [Issues 1407](https://github.com/michel-az-de/EasyStok/issues/1407) e [1408](https://github.com/michel-az-de/EasyStok/issues/1408) | Modelo aprovado e iniciar conversa | Onda 2 para operação manual; onda 9 para automação |
| [Issue 1245](https://github.com/michel-az-de/EasyStok/issues/1245) | Fidelidade e cupons de loja | Onda 10 |
| [PR 871](https://github.com/michel-az-de/EasyStok/pull/871) | Testes de integração no CI | Onda 0: investigar; estava draft com falha em Build + dotnet test |

A PR 1492 tinha checks em andamento na consulta. Diversas outras tinham checks verdes e homologação HTTP manual ignorada. Revisar diferenças contra o master antes de qualquer merge. As issues antigas #1237, #1239, #1240, #1242, #1243 e #1244 continuam abertas apesar de entregas posteriores; reconciliar seus aceites em vez de assumir que tudo falta.

## 6. Plano de execução por ondas

Recomendação: uma onda operacional por vez, dividida em pequenas entregas verticais. Cada fatia entrega tela, regra/API, persistência, permissões, erros e prova da jornada correspondente. Design e UX entram no aceite de toda onda.

### Onda 0. Estabelecer uma base confiável

**Resultado:** saber exatamente qual versão está sendo testada, usada e planejada.

1. Reconciliar este inventário com PRs e issues; registrar cada item como integrado, publicado ou homologado separadamente.
2. Revisar as PRs corretivas aplicáveis, principalmente #1480 e #1401; não juntar todas as PRs em lote.
3. Preparar ambiente de homologação com dados controlados e configuração real de API, sem demonstração disfarçada.
4. Alinhar a versão testada do console/API; registrar a versão pública antes e depois de uma futura publicação.
5. Revisar CI efetivo, instruções desatualizadas, backup e processo de restauração. Estabelecer baseline de falhas e avisos.

**Aceite:** um roteiro reproduzível abre loja/caixa, cria pedido manual, acompanha cozinha, conclui ou cancela e confere os efeitos. Falhas encontradas têm evidência e destino. Nenhum defeito impeditivo sem solução/fallback impede seguir.

**Dependência:** nenhuma. O acesso ao ambiente é necessário para homologação autenticada. Este levantamento entrega o inventário inicial, não conclui a onda.

### Onda 1. Navegação, design system e acessos

**Resultado:** a dona e a equipe encontram suas tarefas numa interface coerente.

1. Fechar mapa de módulos e telas; menu próprio, voltar ao hall, links entre pedido/cozinha/entrega e manutenção de contexto.
2. Aplicar tokens e componentes Casa da Baba: cores, fontes, espaçamento, estados, foco, contraste, campos, filtros, tabelas, diálogos e mensagens.
3. Implementar perfil × módulo na API e no frontend; entrada por perfil, convites e sessão da cozinha.
4. Tratar tela vazia, carregamento, erro, sem permissão e ação ainda indisponível. Evitar botão que promete operação inexistente.
5. Validar balcão desktop, cozinha tablet e consulta móvel; claro/escuro e teclado. Propagar o padrão a cada módulo quando ele for entregue.

**Aceite:** Dona, Atendimento e Cozinha executam seus acessos permitidos; URL direta não contorna a API; refresh e voltar preservam navegação; o mesmo controle tem comportamento visual consistente. A dona aprova telas representativas antes da aplicação geral.

**Dependência:** onda 0. Base: M0.1–M0.3 e M7 de usuários/perfis. A poda destrutiva M0.5 não é pré-requisito da navegação.

### Onda 2. Balcão e ciclo manual do pedido completos

**Resultado:** atender e resolver um pedido inteiro a partir do balcão, com cobrança manual e despacho próprio já disponível.

1. Consolidar conversa, cliente, comanda, resumo do pedido e próxima ação. Preservar o rascunho quando mudar de tela.
2. Cadastrar/editar cliente; tratar identificação do site, endereço, retirada/entrega, janela, observações e alterações antes/depois do pagamento conforme regra explícita.
3. Criar, revisar, enviar, registrar recebimento manual, preparar, concluir, cancelar e tratar ocorrência com confirmação e histórico.
4. Fechar os caminhos ainda não conectados da ficha: reabertura, troca de janela/endereço, recebimento na entrega, bloqueios e ocorrências, reaproveitando APIs existentes.
5. Integrar lembretes, SLA, notificações e mensagens programadas das PRs existentes; fechar iniciar conversa e modelos aprovados para o operador.
6. Unificar status e falhas de texto, áudio/foto, reenvio e transferência entre pessoas/automático. Imprimir via navegador o documento já disponível como fallback até a onda 5.

**Aceite:** cenários de retirada, entrega, pagamento na entrega, cancelamento e erro de envio passam sem alterar banco manualmente. Após recarregar e abrir em outra sessão, pedido, pagamento, estoque e caixa continuam coerentes. Duplo clique e reenvio não duplicam efeitos.

**Dependência:** ondas 0–1; correção de estorno de estoque quando aplicável. Integração paga de frete e pagamento online chegam nas ondas seguintes.

### Onda 3. Mercado Pago e conciliação do pedido

**Resultado:** gerar cobrança no balcão/site e enxergar a confirmação real no mesmo pedido.

1. Revisar #1304, definir uma fonte efetiva das credenciais e mostrar o estado da conexão sem revelar segredos.
2. Validar conta/aplicação/ambientes e configuração de entrega mínima para o checkout, usando retirada quando o cenário permitir.
3. Fechar jornada com Checkout Pro: gerar/reemitir link, enviar, pagar, receber webhook e atualizar conversa, pedido, caixa e autorização de preparo.
4. Tratar recusa, pendência, expiração, abandono, indisponibilidade, pagamento duplicado/tardio, cancelamento e estorno. Revisar #1302, sobre liberação de vaga.
5. Conferir reconciliação quando o webhook atrasar ou não chegar; status de pagamento vem do provedor/backend, nunca de um retorno visual do site.

**Aceite:** sandbox comprova sucesso, recusa, expiração, repetição e estorno sem baixa duplicada. Depois, transação de produção controlada com responsável autorizado, referência rastreável e conferência financeira. Cada ambiente tem evidência própria.

**Dependência:** onda 2; acesso à conta e credenciais pelo responsável. Não criar outra integração de pedidos se a existente puder atender.

### Onda 4. Entrega e geolocalização completas

**Resultado:** do endereço informado à entrega confirmada, com preço e acompanhamento compreensíveis no balcão.

1. Definir/configurar endereço e coordenadas da cozinha, áreas, faixas de frete, capacidade, janelas e exceções.
2. Diferenciar localização do cliente, busca de endereço/CEP, cálculo de cobertura e rota rodoviária. Permissão de localização negada deve permitir digitação manual.
3. Rever #1323 antes de escolher/manter o provedor de mapas. Conferir quotas, configuração e tratamento de endereço ambíguo ou não localizado.
4. Fechar primeiro o despacho próprio: atribuir entregador, roteiro, saída, acompanhamento, entregue, falha/devolução e pagamento recebido.
5. Integrar uma transportadora por vez, começando pela escolhida pela operação: cotar → confirmar custo → solicitar → acompanhar → concluir/cancelar. Lalamove tem issue #1247; 99/Uber dependem de viabilidade comercial/técnica confirmada, sem supor API disponível.
6. Em falha do provedor, exibir motivo e permitir despacho manual registrado. Não confundir frete cobrado do cliente com custo da transportadora.

**Aceite:** endereço dentro/fora da área, janela cheia, localização negada e falha de cotação têm resposta clara. Uma entrega própria e uma da transportadora escolhida fecham com referências reais e retorno ao pedido, sem duplicar chamada.

**Dependência:** ondas 2–3 e contas/configurações. A parte de mapas/entrega própria pode avançar se a credencial da transportadora estiver pendente; a integração externa continua sem aceite até a prova real.

### Onda 5. Cozinha, expedição e impressão física

**Resultado:** um pedido apto entra na cozinha, gera papel legível, é preparado e expedido com conferência.

1. Fechar fila, prioridades, prazos, encomendas e atualização em tempo real; cancelamento deve aparecer claramente para a cozinha.
2. Separar preparo, conferência/embalagem e expedição; ligar retirada/entrega ao balcão. Rever #1293 para número do dia e identificação de congelado.
3. Conectar o consumidor da fila à Oasis OIA-8381-B informada por Felipe e ao computador/tablet que a usará. Proposta inicial: validar driver e impressão via USB no Windows; depois testar Bluetooth no dispositivo escolhido. Definir padrão por dispositivo, sem presumir impressão direta do navegador por Bluetooth.
4. Distinguir recibo não fiscal do cliente, comanda da cozinha, etiqueta de lote e roteiro de entrega; usar cada modelo no lugar correto.
5. Registrar tentativas, falha, sucesso e reimpressão. Tratar impressora desconectada, sem papel e reinício; retorno de software não comprova sozinho que saiu papel.

**Aceite:** pedido real controlado gera recibo e comanda físicos com número, itens, observações, status do pagamento e acentos corretos, sem corte indevido. Desconexão e reconexão preservam fila; reimpressão é explícita e rastreável.

**Dependência:** onda 2 e acesso ao equipamento; testar os meios da onda 3 e a expedição da 4 no aceite conjunto. Equipamento informado: Oasis OIA-8381-B, USB e Bluetooth, 203 dpi, área de impressão anunciada de 100 × 150 mm, sem Wi-Fi nem Ethernet. São dados do anúncio fornecido por Felipe, ainda sem validação física. O anúncio descreve uma etiquetadora: usar pedido/comanda 100 × 150 mm como primeiro cenário; confirmar mídia, sensor, margens, protocolo e suporte a papel contínuo antes de prometer recibo em rolo. Não assumir ESC/POS nem reutilizar automaticamente o caminho da WTP05.

### Onda 6. Cardápio comercial completo

**Resultado:** a dona cadastra e altera a oferta uma vez e os canais vendem a mesma oferta.

1. Partir de M1.1–M1.3 já integradas; não reimplementar lista/categorias.
2. Concluir edição de produto, fotos, porções/variações, disponibilidade, preços, observações e alergênicos.
3. Definir e implementar adicionais/combos com efeito claro no preço, preparo e estoque. Decisões pendentes de M1 viram regra antes da respectiva fatia.
4. Exibir a mesma oferta no balcão, site e agente; preservar dados históricos de pedidos quando o cardápio mudar.

**Aceite:** cadastrar, alterar, pausar e arquivar item reflete nos canais; pedido usa preço correto, não vende oferta indisponível por erro de cache e mantém sua composição histórica. Combo/adicional chega corretamente à cozinha.

**Dependência:** ondas 1–2; integrações das ondas 3–5 usadas nos cenários. Base: [M1](02-m1-cardapio.md).

### Onda 7. Produção, estoque, etiquetas e backup offline

**Resultado:** produzir e vender atualiza saldo, lotes e custos sem divergência entre console e PWA.

1. Incorporar M2.1 (#1492) e desenvolver M2.2 (#1491) na sequência, com UX do módulo próprio.
2. Fechar insumos, fichas técnicas, rendimento/peso real, planejamento, produção do dia, perdas, embalagens e etiquetas de lote.
3. Definir quando baixar insumo/embalagem, como contar porções e como tratar saldo descoberto. Reaproveitar a regra existente de venda sem bloqueio por saldo, salvo nova decisão explícita.
4. Validar entradas/saídas, validade, ajustes com motivo, cancelamento e estorno de lote; conferir as correções recentes do PWA no aparelho.
5. Implementar versão-base no sync, fila de conflitos, aviso e resolução pela dona conforme ADR-0059. Proteger reenvio, reconexão e duplicação.

**Aceite:** insumo → produção → lote/etiqueta → venda → baixa/perda/estorno bate nos dois clientes. Editar a mesma entidade offline e online gera conflito visível; a decisão da dona propaga sem perder o registro original nem duplicar saldo.

**Dependência:** ondas 1, 2 e 6; impressão de etiquetas validada com o dispositivo correspondente. Base: [M2](03-m2-producao.md) e ADR-0059.

### Onda 8. Compras, fornecedores e financeiro gerencial

**Resultado:** comprar, receber, pagar e fechar a operação pelo novo console.

1. Colocar fornecedores, listas/pedidos de compra e recebimento parcial/total no console, integrados ao estoque da onda 7.
2. Fechar contas a pagar/receber, parcelas, vencimentos, baixas, estornos, categorias e centros de custo necessários à Casa da Baba.
3. Unificar fonte dos totais de caixa e distinguir dinheiro físico, cartão/Pix, contas pendentes, taxa e custo de entrega.
4. Entregar venda avulsa, conferência da gaveta, fechamento/reabertura com motivo, histórico, extrato e relatórios de vendas/canais, custos/perdas e caixa.
5. Resolver o destino do caixa da PWA antes de retirar qualquer função; o ADR-0059 preservou produção/cadastro e deixou essa parte pendente.

**Aceite:** compra recebida movimenta estoque e obrigação financeira; pagamento baixa uma vez; venda recebida e estornada reconcilia pedido/caixa/contas. Fechamento explica diferenças, e relatório rastreia valores até as operações de origem.

**Dependência:** ondas 3 e 7, com despacho da 4 para custos de entrega. Base: [M5](06-m5-caixa.md), revisado conforme ADR-0059. O menu pode acomodar compras/estoque em M2 e financeiro em M5 sem inventar módulos novos antes de validar a navegação.

### Onda 9. Atendimento automático de ponta a ponta

**Resultado:** o automático resolve cenários autorizados e transfere o restante com contexto preservado.

1. Usar catálogo, horários, entrega e pagamentos que já passaram pelos aceites anteriores; não automatizar um fluxo ainda inconsistente.
2. Fechar identificação do cliente, entendimento de texto/áudio, montagem/revisão da comanda, cobrança e acompanhamento.
3. Definir quais ações são sugestões, quais exigem confirmação e quais podem ocorrer automaticamente. Preservar as decisões de controle da dona.
4. Homologar primeira resposta, ausência de operador, fora de horário, mensagens em rajada, retomada, limite do canal, falha da IA e transferência humano/automático.
5. Revisar notificações e a PR de e-mail #1433; fechar WhatsApp e site antes de expandir canais. Histórico para RAG exige decisão específica sobre fonte e uso; não tratar como já autorizado/implementado.

**Aceite:** conjunto acordado de conversas realistas cobre sucesso, dúvida, indisponibilidade, mudança de pedido, recusa de pagamento, falha de entrega e intervenção humana. O agente não inventa preço/prazo/saldo nem executa ação fora da permissão; desligar automático entrega a conversa à pessoa.

**Dependência:** ondas 2–7. Melhorias locais do agente e transferência não precisam esperar esta onda; o que fica para cá é a homologação da autonomia sobre o ciclo completo.

### Onda 10. CRM, campanhas, promoções e fidelidade

**Resultado:** selecionar público e executar campanhas mensuráveis dentro da operação da loja.

1. Entregar lista CRM com busca, tags, histórico, consentimentos e interesses.
2. Ligar tela de campanhas ao motor existente: público, exclusões, conteúdo, aprovação, agenda, ondas e cancelamento.
3. Implementar promoções com validade, cupons e fidelidade por loja; definir acumulação, resgate, cancelamento e devolução dos benefícios.
4. Medir envio, falha, resposta e pedido pago atribuível; impedir que pedido apenas criado seja apresentado como receita.

**Aceite:** campanha pequena autorizada percorre seleção, revisão, envio e resultado; exclusões são respeitadas; cupom/resgate altera o pedido corretamente, inclusive no estorno.

**Dependência:** ondas 3, 6, 8 e 9 para os canais automatizados. Base: [M6](07-m6-campanhas.md) e #1245. A execução de campanhas reais precisa de autorização específica.

### Onda 11. Encerrar a transição e operar só na nova versão

**Resultado:** todas as tarefas acordadas da Casa da Baba são executadas no console e no backup offline previsto.

1. Conferir matriz Web → console por área: catálogo, clientes, produção, estoque, compras, fornecedores, caixa, financeiro, etiquetas, relatórios e configurações.
2. Homologar jornada completa com dona/equipe: abrir loja → produzir → atender/vender → cobrar → preparar/imprimir → entregar → fechar caixa → conferir relatórios.
3. Exercitar restauração de backup em ambiente isolado, reconexão da PWA, impressora indisponível, queda de provedor, monitoramento e retorno a uma versão anterior.
4. Desativar cada área antiga só depois do uso aprovado do equivalente. Poda de dados/tabelas exige inventário, backup recuperável e escopo confirmado; não fazer limpeza só para o menu ficar simples.
5. Registrar versão publicada, evidência de aceite, responsáveis e procedimento de suporte. Confirmar os acessos de Configurações e o estado dos canais.

**Aceite:** ciclo operacional acordado concluído pela equipe sem recurso às telas antigas, sem divergência não explicada de pedido/pagamento/estoque/caixa; recuperação demonstrada. Pendências residuais têm impacto, responsável e prioridade explícitos.

**Dependência:** ondas operacionais anteriores. Homologação e retirada parcial do Web acontecem ao longo delas; esta onda fecha o conjunto.

## 7. Ordem e marcos

Sequência sugerida: 0 → 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → 11.

As dependências técnicas permitem adiantar trabalho já iniciado: M2.1/#1492 pode continuar sem esperar todas as integrações; verificar driver/mídia da Oasis, credenciais e condições de entrega deve começar na onda 0. Isso não muda o critério de concluir cada jornada com provas.

| Marco | Evidência de resultado |
|---|---|
| Após 2 | Balcão manual utilizável, com pedido e efeitos consistentes |
| Após 5 | Operação vende, cobra online, despacha, prepara e imprime fisicamente |
| Após 8 | Oferta, produção, estoque, compras e financeiro operados no console |
| Após 9 | Atendimento automático homologado sobre processos confiáveis |
| Após 11 | Transição concluída com backup offline e recuperação demonstrada |

Não há prazo ou orçamento fechado: faltam capacidade da equipe, decisões de negócio e validação das integrações. Estimar cada fatia depois do roteiro e das dependências definidos, usando a velocidade real de entrega. Prazo de credencial/aprovação externa fica separado de esforço de código.

## 8. Definição de concluído para toda fatia

1. Cenário e regra claros, incluindo quem pode agir e quais dados serão persistidos.
2. UX com carregamento, vazio, erro, sucesso, confirmação quando necessária e recuperação.
3. Backend e frontend conectados em modo API; sem simulação apresentada como gravação.
4. Testes adequados ao risco: unitário/integração para dinheiro, estoque e permissões; percurso visual para navegação; dispositivo real para impressão/offline.
5. Recarregar/outra sessão confirma persistência; repetição e falha não duplicam efeitos.
6. Revisão e merge separados da publicação. Publicação separada de homologação. Registrar SHA e evidência de cada estado.
7. Aceite operacional da dona/equipe no ambiente correto e procedimento de recuperação aplicável.

Campos mínimos do acompanhamento: onda, fatia, resultado, dependência, responsável, issue/PR, SHA integrado, SHA publicado, prova e pendência. Estados sugeridos: planejada → em execução → em revisão → integrada → publicada → homologada. Dependência externa é uma marca adicional, não equivale a conclusão.

## 9. Decisões e dependências que não devem ser inventadas

| Informação | Necessária para | Encaminhamento |
|---|---|---|
| Oasis OIA-8381-B: USB/Bluetooth, 203 dpi, área anunciada 100 × 150 mm | Onda 5 | Modelo informado por Felipe em 09/10; falta verificar driver/protocolo, mídia carregada e dispositivo hospedeiro. USB/Windows é proposta inicial |
| Motoboy próprio e transportadora prioritária; conta/acesso técnico | Onda 4 | Perguntado a Felipe; não presumir APIs de 99/Uber |
| Endereço/origem, raio, valores, horários e capacidade | Ondas 3–4 | Conferir cadastro com a dona; snapshot antigo não prova estado atual |
| Conta e acesso Mercado Pago em teste/produção | Onda 3 | Responsável configura em canal seguro; sem segredos na documentação |
| Matriz de permissões e exceções de operação | Ondas 1–2 | Partir dos três perfis já decididos; confirmar apenas exceções |
| Regras de combos, adicionais, insumos e embalagens | Ondas 6–7 | Resolver as D-M1/D-M2 correspondentes antes da fatia |
| Destino do caixa offline da PWA | Onda 8 | Decisão pendente explicitamente no ADR-0059 |
| Identidade de tela e ativos aprovados | Onda 1 | Validar aplicação visual; não refazer marca por conta própria |
| Fonte/uso de histórico para RAG e limites da autonomia | Onda 9 | Definir caso de uso e confirmação das ações |

Dependência externa bloqueia o aceite correspondente, não o restante do levantamento ou as frentes independentes.

## 10. Validação executada nesta demanda

| Verificação | Resultado | Limite da evidência |
|---|---|---|
| npm run qualidade no console | Passou: lint, camadas e build; build com 392 módulos | Lint emitiu avisos de acessibilidade, chaves duplicadas e outros; não representa aceite visual |
| 14 scripts de prova do console | Todos saíram com código 0 | Provas de funções/contratos e chamadas simuladas, não E2E de produção |
| dotnet test Api.UnitTests com filtro MercadoPago ou Impressao | 103 aprovados, 0 falhas, 0 ignorados; net10.0 | Não chama Mercado Pago real nem imprime em dispositivo |
| Git local/remoto e PRs | Base identificada e PRs consultadas ao vivo | Snapshot de 09/10; pode mudar depois |
| HTTP público e metadados de versão | Console/API/site responderam 200; divergência de SHA registrada | Disponibilidade HTTP não comprova login, checkout ou rotina da loja |

Provas do console executadas: prova-1447-hall-modulos, prova-f06-honestidade, prova-f03-pedido-api, prova-1255-pagamento-manual-api, prova-1446-cozinha-viva, prova-1440-janelas-e-roteiro, prova-1443-loja-abre-caixa, prova-1441-respostas-tags-notas, prova-1481-gestao-cardapio, prova-1482-item-cardapio, prova-1483-categorias-cardapio, prova-f11-lote-papel, prova-1445-assistente-acoes e prova-f07-sincronizacao, todos em EasyStock.Console/ferramentas com extensão .mjs.

Não executados: suíte completa de backend, integração com banco real, teste visual autenticado, pagamento real, cotação/corrida real, impressão física e validação no aparelho offline. São aceites previstos nas ondas, não entregas desta análise.

## 11. Primeira execução recomendada

Começar pela onda 0, revisar a PR visual #1480 e a correção #1401 e preparar o roteiro único do pedido. Na sequência, fechar navegação/DS/perfis da onda 1 e a operação manual da onda 2. Em paralelo apenas no planejamento, confirmar driver/mídia da Oasis, conta de pagamentos e entregas para não descobrir esses bloqueios no fim.

Não abrir outro conjunto de implementações duplicadas: atualizar as issues existentes com a onda e o aceite, abrindo novas somente para lacunas confirmadas. Este documento não cria nem modifica issues e não realiza merge ou deploy das PRs listadas.

## 12. Execução autorizada da onda 0, 09/10/2026

Felipe aprovou a continuação após o levantamento. As seções anteriores preservam o snapshot da análise inicial; esta seção registra o trabalho de implementação posterior. Desenvolvimento em worktree isolado, mantendo os demais trabalhos locais.

### Correções e conciliação

- PR #1401 revisada e atualizada para `425324a34fb48876ada782a982db3523f835137a`: estornos de estoque/caixa e consumo FEFO. A revisão reproduziu uma corrida em que o saldo diminuía entre a consulta inicial e o lock; agora o serviço recusa descoberto quando a política não permite estoque negativo. Novos testes cobrem saldo concorrente, quantidades fracionadas e devolução aos lotes originais.
- PR #1480 revisada e atualizada para `d7a21b59aacb71fbff76a1bd6d46f41dcf201eb4`: três conflitos resolvidos preservando as entregas atuais do cardápio e do controle de atendimento. A revisão corrigiu itens avulsos excluídos da cozinha/PWA, pareamento cruzado entre empresas e seleção de cliente homônimo por nome. Bloqueio agora usa a conversa escolhida.
- Sync PWA: erro de gravação mantém o cursor para reenvio; timeout não pula dados antigos ainda não recebidos. Duas regressões foram reproduzidas antes da correção. Isso não implementa a resolução de conflitos entre edições offline, que continua na onda 7.
- Provas da cozinha e da revisão #1474 incluídas no workflow do console. Avisos adicionados à entrada da `.knowledge` para distinguir os snapshots antigos da base atual.
- PR #1401 integrada em `master` como `96516cb2` após build/testes, análise CodeQL e secret-scan aprovados. O job manual de homologação HTTP do GitHub foi ignorado; a prova HTTP desta seção foi executada localmente.
- Durante esta execução, outro trabalho integrou #1492 e #1495: estoque do dia e produção por prato com peso real/etiquetas. Essas entregas foram conciliadas, preservadas e revalidadas aqui. Não são implementações feitas por esta revisão; o inventário inicial as listava antes do merge. A seção de M2 em `03-m2-producao.md` contém seu escopo atualizado.

### Evidência local

| Verificação | Resultado |
|---|---|
| Correção de estoque, Application | 63 aprovados, sem ignorados |
| ItemEstoque, Domain | 14 aprovados, sem ignorados |
| Estoque, pedido, caixa, PostgreSQL real | 18 aprovados, sem ignorados |
| Criação concorrente de pedido por HTTP, PostgreSQL real | 1 aprovado, sem ignorados |
| Atendimento, checkout e cardápio, Application | 630 aprovados, sem ignorados |
| Controllers, pareamento, calculadora e sync, API | 105 aprovados, sem ignorados |
| Cozinha/KDS e atendimento, PostgreSQL real | 9 aprovados, sem ignorados |
| Console | lint/camadas/build aprovados; 47 scripts de prova aprovados; ficha novamente aprovada com 23 verificações após correção de homônimos |
| PWA | 167 aprovados, 1 ignorado por IndexedDB ausente no sandbox; 2 scripts ESM não executados pelo runner |
| Gate de commit | build aprovado e 36 testes de arquitetura aprovados |
| Verificador HTTP | 8 testes aprovados e console/API locais respondendo com o mesmo SHA `d7a21b59` |
| Conciliação com #1492/#1495 | 242 testes de aplicação aprovados; qualidade do console e provas 1490, 1491, 1447 e ambas 1474 aprovadas novamente |

Jornada executada por `scripts/homologacao/onda0-http.ps1` em API real com autenticação JWT e PostgreSQL 17 descartável, sem usar os bancos das outras stacks. Abriu loja/caixa, criou pedido avulso de 2,5 unidades a R$ 10, confirmou a cozinha, recebeu R$ 25, percorreu preparando/pronto/entregue e releu cada estado. Recebeu/cancelou outro pedido de R$ 15 e conferiu fechamento de R$ 125, incluindo R$ 100 de abertura. Identificadores de teste: entregue `9520d7ca-59be-4a2a-808a-d0016ac02c3a`; cancelado `c281d1ba-d3f7-490a-bf7a-95e7a7326412`.

Backup local: `pg_dump -Fc` e `pg_restore --exit-on-error` para outra base descartável. Quantidade e hash do conteúdo de pedidos (2), itens (2), pagamentos (2) e fechamentos (1) iguais ao original. SHA-256 do dump de teste: `CA7D215D0AE30FCA1513678D2380FF8ADF8D617E44B0777A543B12D0F788CC4C`. Logs/TRX/dump ficam no worktree em `.build/onda0-tests`, fora do Git.

### Limites e próximos aceites

- Revisão e teste local não são publicação. Os SHAs públicos registrados na seção 3 não foram substituídos por um deploy nesta execução.
- A abertura do navegador automatizado falhou no início por `windows sandbox failed: apply deny-read ACLs`. O aceite visual autenticado permanece pendente; as provas JavaScript e HTTP não o substituem.
- Scripts de backup externo da VPS foram lidos, mas a disponibilidade e a restauração do backup real da produção não foram verificadas. A prova acima usa somente massa sintética local.
- Não há workflow `build-casa-da-baba-apk.yml` na árvore atual, nem cópia MAUI ativa para sincronizar. O workflow PWA vigente roda `node tests/pwa/run.js`; não foi gerado APK nem testada a Oasis.
- Não fechar a onda 0 como homologada visualmente. A próxima fatia é completar esse aceite, verificar recuperação da VPS antes de publicar e então apresentar navegação/DS/perfis da onda 1 para aprovação das telas representativas.
