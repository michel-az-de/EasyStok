# Onda 9 — Impressos operacionais da Casa da Baba (S49–S51)

Issue: #1267 · Layout aprovado: [impressos/pedido-aprovado.html](impressos/pedido-aprovado.html) (abrir no navegador)

Objetivo: todo papel que sai da operação tem a identidade da Casa da Baba e cabe na impressora que vai
usá-lo. Hoje existem o canhoto de produção da S20 (80 mm, visual genérico) e o cupom do PWA (texto cru de
32 colunas na fonte interna da WTP05). Esta onda cria o motor de impressos e entrega o **Pedido** nos três
papéis. Os demais documentos seguem o mesmo ciclo, um por vez (ver Backlog).

## Decisões (aprovadas pelo Felipe em 30/09 e 01/10/2026)

| Tema | Decisão |
|---|---|
| Papéis | Etiqueta térmica **10×15 cm**, cupom térmico **58 mm** e **A4** jato de tinta. Cada documento escolhe quais papéis atende. |
| Ciclo | Mockup em tamanho real, aprovação, spec, código. O mockup aprovado é versionado em `impressos/` e é a referência do snapshot. |
| Térmica | Só preto. Marca só no nome "Casa da Baba" em Lora. Destaque por preto chapado (código do pedido, faixa do total). Impressa como **imagem** (raster), para sair com as fontes da marca e com acento. |
| Jato de tinta | Colorido e econômico: cor (cacau `#422814`, caramelo `#A25803`, linha `#E5D5BE`) só em linhas, contornos e texto, sem fundos cheios. Logo oficial no topo. Mediu cerca de 1/4 da tinta de um A4 com blocos cheios. |
| Grade | Linha 1 sem colunas (nome, CNPJ, site, WhatsApp). Linhas seguintes em colunas, com linhas separando as células, ocupando o papel de borda a borda. |
| Identificação | Código de barras CODE128 com o número curto do pedido (lido pelo sistema). Sem QR no Pedido; o QR do Instagram vai para a etiqueta de agradecimento. |
| Agendado | Selo AGENDADO, data da entrega, janela, **pronto até** (início da janela menos X minutos, X por loja, padrão 30) e solicitado em. |
| Imediato | Selo IMEDIATO, previsão de pronto e de saída no lugar da janela. |
| Cliente | Nome, **telefone completo** (o entregador liga), ID curto e endereço. CPF nunca sai no papel. |
| Total | Diz o que fazer: "Cobrar na entrega · {forma}" ou "Pago". |
| Rodapé | Hora da última alteração do pedido e hora da impressão, para a cozinha não produzir por uma etiqueta velha. |
| Fora | Ícone de produto; frase de agradecimento no Pedido. |

## Arquitetura

```
GET api/pedidos/{id}/impresso?modelo=etiqueta-10x15|cupom-58|a4
        │
MontarPedidoImpressoUseCase ──► PedidoImpressoDto (horários já em Brasília, textos prontos)
        │
PedidoImpressoHtml (Api) ── IRendererTemplate (Scriban, porta que já existe) ── modelo .sbn por papel
        │
HTML autocontido com @page do tamanho do papel + CSS da marca + CODE128 em SVG gerado no servidor
        │
Consumidor (S51): navegador do console imprime 10×15 e A4 pelo driver; 58 mm vira imagem e segue em ESC/POS raster
```

O canhoto da S20 continua como está: é o papel de **produção** (sem preço). O Pedido é o papel de
**expedição** (com preço, cliente e cobrança). Os dois leem a mesma fila de impressão.

---

### S49 · Motor de impressos e o Pedido nos três papéis

**Problema.** Não existe impresso do pedido com preço, cobrança e cliente para expedição, e o que o PWA
imprime não tem a identidade da casa. O layout dos três papéis está aprovado em
`impressos/pedido-aprovado.html`.
**Abordagem.** DTO montado na Application, modelos Scriban por papel embutidos na Api, renderização pela
porta `IRendererTemplate` que as notificações já usam. Saída HTML autocontida; o código de barras é SVG
gerado no servidor, sem script, para o snapshot ser determinístico.
**Escopo.**
- `App/UseCases/Operacao/Impressao/PedidoImpressoDto.cs`:
  - `Casa {Nome, Documento (CNPJ formatado), Site, WhatsApp, LogoUrl}`: `Storefront.TituloPublico`, `Empresa.Documento`, `Storefront.DominioCustom` (sem domínio: omitir), `Storefront.WhatsappPedidos`, `Storefront.LogoUrl`.
  - `Pedido {Numero (8 hex maiúsculos, igual ao canhoto), SolicitadoEm, AlteradoEm, ImpressoEm}`.
  - `Prazo`: agendado `{Data, Inicio, Fim, ProntoAte}` pela mesma leitura do KDS (`VagaOcupada → JanelaEntrega`, vaga não liberada); sem vaga e com `AgendadoParaEm`, `Inicio = AgendadoParaEm` e `Fim = null`. Imediato `{ProntoPrevisto, SaidaPrevista}`: `ProntoPrevisto = (último pagamento ou CriadoEm) + ConfiguracaoAtendimento.TempoPreparoPadraoMinutos`; `SaidaPrevista = ProntoPrevisto + MinutosProntoAntesDaJanela`. `ProntoAte = Inicio − MinutosProntoAntesDaJanela`, constante `30` nesta spec (S50 torna configurável).
  - `Cliente {IdCurto (6 primeiros hex de ClienteId), Nome, Telefone completo, Endereco}`: nome e telefone do pedido com fallback no cadastro; endereço com a mesma montagem do `MontarCanhotoUseCase` (extrair para helper compartilhado, sem mudar o canhoto).
  - `Entrega {Modo, Responsavel}`: da `ParadaViagem` do pedido (tipo do entregador: Motoboy, Plataforma, Próprio; `EntregadorNome`); sem parada, omitir.
  - `Itens [{Quantidade, Nome (+ variação), Observacao, Unitario, Subtotal}]`, na ordem de entrada; frete e taxa entram como linha comum.
  - `Observacao` (do pedido), `Nota` (texto curto opcional passado na impressão, `?nota=`, até 80 caracteres).
  - `Cobranca {Total, Pago (soma dos pagamentos ≥ total), Forma}`: forma da última `CobrancaPedido.MetodoPagamento`; rótulo "Pago" ou "Cobrar na entrega · {forma}".
- `App/UseCases/Operacao/Impressao/MontarPedidoImpressoUseCase.cs`: devolve `null` para pedido inexistente ou de outra empresa (mesmo contrato do canhoto). `TimeProvider` para `ImpressoEm`.
- `Api/Data/Templates/Impressao/pedido.etiqueta-10x15.sbn`, `pedido.cupom-58.sbn`, `pedido.a4.sbn` e `impressos.css` (tokens da marca, `@page` por papel), embutidos como o `canhoto.css`.
- `Api/Services/Impressao/Code128Svg.cs`: CODE128 (conjunto B) em SVG, com zona de silêncio de 10 módulos.
- `Api/Services/Impressao/PedidoImpressoHtml.cs`: escolhe o modelo, monta o dicionário para o Scriban, devolve a página. Textos sempre escapados (`html.escape`).
- Fontes Lora e Nunito Sans (OFL, licenças junto) em `Api/wwwroot/impressao/fontes/`, referenciadas por URL relativa; fallback `Georgia, serif` e `system-ui, sans-serif`.
- `ImpressaoController`: `GET api/pedidos/{id}/impresso?modelo=&nota=` com a policy `ImpressaoFila` (igual ao canhoto). Modelo inválido → 400.
**Fora.** Consumidor e raster (S51). Configuração do "pronto até" (S50). Mudança no canhoto da S20.
**Aceite.**
- [ ] Os três modelos renderizam o pedido de exemplo igual ao snapshot aprovado (estrutura, rótulos e ordem das células de `impressos/pedido-aprovado.html`).
- [ ] Agendado mostra AGENDADO, data, janela, pronto até e solicitado; imediato mostra IMEDIATO, previsão de pronto e de saída.
- [ ] Pago mostra "Pago"; pendente mostra "Cobrar na entrega · {forma}".
- [ ] Telefone completo; CPF nunca aparece; texto do cliente com `<script>` sai escapado.
- [ ] Térmicos sem nenhuma cor além de preto e branco; A4 sem fundo preenchido (só `border`, `color`).
- [ ] CODE128 do número decodifica para o próprio número (teste decodifica as barras geradas).
- [ ] Pedido de outra empresa → 404; modelo inválido → 400; sem credencial → 401.
**Testes (Red).** `MontarPedidoImpressoUseCaseTests.AgendadoCalculaProntoAte`, `...ImediatoPreveProntoESaida`, `...PagoVersusCobrarNaEntrega`, `...OutraEmpresaNull`; `Code128SvgTests.DecodificaONumero`; `PedidoImpressoHtmlTests.SnapshotEtiqueta10x15`, `...SnapshotCupom58`, `...SnapshotA4`, `...EscapaTextoDoCliente`, `...TermicoSoPreto`; `ImpressaoControllerTests.ImpressoModeloInvalido400`.
Snapshots em `EasyStock.Api.UnitTests/Impressao/Snapshots/*.html`; regravar com `IMPRESSAO_ATUALIZAR_SNAPSHOT=1`.
**Rollback.** Remover o endpoint; nada persiste.
**Depende de.** S20 (fila e controller), S21 (janela), S44 (entregador e viagem).
**Leitura mínima.** `App/UseCases/Operacao/Impressao/MontarCanhotoUseCase.cs`; `Api/Services/Impressao/CanhotoHtml.cs`; `Api/Controllers/ImpressaoController.cs`; `Infra.Postgre/Repositories/KdsPedidoQueries.cs` (linhas 40-60, janela); `App/Ports/Output/Notifications/IRendererTemplate.cs`; `impressos/pedido-aprovado.html`.
**Tamanho.** M. **Tier.** baixo (spec de plano aprovado, sem migração).

---

### S50 · "Pronto até" configurável por loja

**Problema.** A cozinha precisa de um prazo antes da janela; o Felipe decidiu que ele é automático e
configurável por loja (padrão 30 min), sobrescrevível no pedido.
**Escopo.**
- `ConfiguracaoAtendimento.MinutosProntoAntesDaJanela` (int, padrão 30, > 0) e `Pedido.ProntoAteEm` (DateTime?, sobrescrita manual). Migration `AddProntoAteImpressos`.
- `MontarPedidoImpressoUseCase`: `ProntoAteEm` do pedido, senão `Inicio − MinutosProntoAntesDaJanela`.
- `PATCH api/pedidos/{id}/pronto-ate {prontoAteEm | null}` (policy `Operador`).
**Aceite.**
- [ ] Loja com 45 min e janela 10:00 → pronto até 09:15.
- [ ] Sobrescrita no pedido vence a regra da loja; `null` volta à regra.
**Testes (Red).** `ConfiguracaoAtendimentoTests.MinutosProntoPositivo`, `MontarPedidoImpressoUseCaseTests.SobrescritaVence`.
**Rollback.** Migration `Down`; o use case volta à constante.
**Depende de.** S49.
**Tamanho.** P. **Tier.** alto (migração).

---

### S51 · Consumidores: navegador e cupom 58 mm em imagem

**Problema.** A 10×15 e o A4 imprimem pelo driver; a WTP05 de 58 mm recebe bytes ESC/POS pelo
Bluetooth do PWA, e em modo texto ela usa a fonte interna (sem acento, sem a marca).
**Abordagem.**
- Console: botão "Imprimir pedido" abre `impresso?modelo=` e chama `window.print()`; o papel padrão de cada documento fica salvo por dispositivo.
- PWA 58 mm: desenhar a página do `cupom-58` num `canvas` de 384 px de largura (203 dpi), binarizar e enviar com `GS v 0` em faixas de 24 linhas pelo `ThermalPrinterPlugin` que já existe. O texto atual (`escPosOrderLabel`) fica como alternativa quando a imagem falhar.
**Aceite.**
- [ ] Cupom sai na WTP05 com Lora/Nunito e acento, em até 8 s para um pedido de 6 itens.
- [ ] Falha na imagem cai no texto sem perder a impressão.
**Depende de.** S49. **Tamanho.** M. **Tier.** baixo.

---

## Backlog (um documento por vez, mesmo ciclo)

| Ordem | Documento | Papéis prováveis | Observação |
|---|---|---|---|
| 1 | Comanda | 58 mm, 10×15 | Derivada do Pedido, sem preço, observações em destaque |
| 2 | Resumo do pedido | 10×15, A4 | Para o cliente |
| 3 | Recibo de pagamento | 58 mm, A4 | |
| 4 | Recibo de envio | 10×15 | Formato natural da etiqueta de envio |
| 5 | Etiqueta de agradecimento | 10×15 | QR do Instagram (`Storefront.InstagramUrl`) |
| 6 | Caixa do dia | 58 mm, A4 | Hoje o PWA só compartilha o texto (`printCashClosing`) |
| 7 | Lista de compras | 58 mm, A4 | Hoje existe em HTML 58 mm no PWA (`shoppingListPrintHtml`) |
| 8 | Relatório do cliente | A4 | Pode passar de uma página |

Cada item vira uma spec S nova depois que o mockup dele for aprovado.
