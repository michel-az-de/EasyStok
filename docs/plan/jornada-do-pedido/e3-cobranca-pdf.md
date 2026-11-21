# E3: cobrança Pix em 2 balões com PDF do pedido e comprovante (#2962)

- Data: 2026-11-20
- Tier: baixo (sem migração, sem auth, sem `Program.cs`, Dockerfile ou `.csproj`)
- Decisões do Felipe (2026-11-20): produção usa WAHA (`sendFile`); o comprovante PDF substitui o canhoto em imagem; o link do PDF é público com nome aleatório, como o canhoto (o link assinado local dava 404, #3000).
- Estado em 2026-11-21: o balão 1 leva a imagem do impresso `pedido.sbn` no lugar do PDF do pedido (#3035, fonte única da #3017), e o desenho do pedido em PDF saiu do código (#3049). Só o comprovante segue em PDF.

## 1. Problema

A cobrança Pix direto chega ao WhatsApp como um bloco único: resumo, copia e cola e instrução no mesmo balão. A cliente não copia só o código e a mensagem parece crua. No pagamento, o canhoto sai como imagem, sem arquivo para guardar.

## 2. Abordagem

| Balão | Conteúdo |
|---|---|
| 1 | PDF do pedido anexado, legenda curta: `Pedido {código} · {loja}`, itens e total, entrega, `Pague em até 30 min pelo Pix abaixo` |
| 2 | SÓ o copia e cola, sem nenhum texto em volta (toque longo copia tudo) |
| Pagamento confirmado | PDF "Pago" (comprovante) no lugar do canhoto em imagem, com a mesma idempotência (`canhoto_enviado`) e a mesma janela de 24 h |

Texto e PDF nascem na API (`TextoCobrancaPix`, `IRenderizadorPdfPedido`). O Console só espelha o formato na prévia.

## 3. Contratos novos

- `ICanalMensageria.EnviarDocumentoAsync(contato, urlPublica, nomeArquivo, legenda)`. Padrão da interface: legenda e link em texto (canal sem documento cai no fallback de link).
- `IWhatsAppCloudClient.EnviarDocumentoAsync(waId, urlPublica, nomeArquivo, legenda)`: Meta (`type: document`), WAHA (`api/sendFile`), WPPConnect (`send-file-base64`), roteador (delega), stub (registra `documento`).
- `IRenderizadorPdfPedido`: `RenderizarComprovanteAsync`, PDF com QuestPDF. O desenho do pedido com o QR Pix saiu na #3049; o pedido vai como imagem do impresso (#3035).
- `CobrancaPixConversa`: envia os balões, grava uma `Mensagem` por balão e aplica a regra de falha parcial.
- `PdfCobrancaOpcoes` (`Atendimento:Cobranca:*`): `EnviarPdf` (padrão `true`) e `ValidadeLinkHoras` (padrão 24).

## 4. Falha parcial

| Situação | Resultado |
|---|---|
| Balão 1 falha (PDF e legenda em texto) | Nada sai. Grava 1 `Mensagem` com falha SEM o código. Sem texto único (fallback) |
| Balão 1 sai, balão 2 falha | Balão 1 fica gravado. Grava falha do balão 2, sem repetir o código (o canal pode ter aceitado) e sem reenvio automático. Devolve `false` |
| PDF não renderiza ou não sobe | Balão 1 vai como legenda em texto; o balão 2 segue igual. Sem texto único |
| Canal sem documento | Legenda e link assinado em texto (padrão da porta), depois o código |

Modelo: `TextoAposImagemFalhouException` (Meta). Aqui a regra vive no serviço, não no provedor.

## 5. Fallback e rollback

- Fora da janela de 24 h: sem PDF; segue o fallback atual (`ResumoPedidoForaDaJanela`, modelo).
- `Atendimento:Cobranca:EnviarPdf=false` volta ao texto único atual e ao canhoto em imagem. Sem migração, sem dados a desfazer.

## 6. Segurança

- O copia e cola NUNCA vai para log, exceção nem `Mensagem.Texto` de falha. Teste de varredura nos três.
- O PDF vai para storage privado, nome aleatório, URL assinada com validade (24 h, configurável). O arquivo não é público.
- O comprovante não leva o copia e cola.

## 7. Aceite

- [ ] Texto dos 2 balões testado (balão 2 é exatamente o código).
- [ ] PDF do pedido válido (`%PDF`) com o código embutido e QR; comprovante com "Pago".
- [ ] Envio de documento testado em WAHA e Meta (payload).
- [ ] Falha parcial, fallback sem PDF e código fora de log/falha testados.
- [ ] Comprovante substitui o canhoto e respeita idempotência e janela.
- [ ] Console: prévia alinhada ao formato novo; provas existentes seguem verdes.

## 8. Fora desta entrega

- Alteração de comanda (`ConfirmarAlteracaoComandaUseCase`): sai pela fila de notificações com modelo; segue o texto atual.
- Cobrança online (link Mercado Pago): sem copia e cola; segue o texto atual.
- O horário vem de `AgendadoParaEm`: o pedido não guarda a janela (início e fim).
