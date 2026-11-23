# APK de homologação e prevenção de massa de teste (#3114)

Fase de prevenção separada da #2813. A produção recebeu massa de homologação porque o APK só sabia falar com a produção, e a única cópia do histórico real estava no aparelho da operação.

## Fatias

| Fatia | Itens | Entrega | Prova |
|---|---|---|---|
| S1 | B9, P1 | Variante `homologacao` do APK, instalável ao lado do oficial e sem caminho para a produção | Varredura dos fontes, teste nativo de rede, empacotador real, APK montado e inspecionado |
| S2 | P2 | Trava na API contra massa de teste na produção | Teste de integração da API |
| S3 | P3 | Backup automático do aparelho e alerta de dado que só existe nele | Teste do PWA e do APK |

Cada fatia é uma PR. S1 não depende de S2 nem de S3.

## S1: variante de homologação (B9, P1)

### Desenho

- O Gradle ganha duas variantes de produto na dimensão `ambiente`:
  - `oficial` segue com `applicationId` `com.casadababa.piloto`, nome, ícone e Firebase atuais.
  - `homologacao` usa `com.casadababa.piloto.homologacao`, o nome "Casa da Baba Homologação" e o ícone com fundo índigo. Pacotes diferentes instalam lado a lado e não dividem dados.
- As origens da produção ficam no `buildConfigField` da variante oficial. `Ambiente.java` lê o `BuildConfig` e não tem endereço, então o APK de homologação não carrega nenhum host da produção. Login, pareamento, avisos, Workspace, OTA e página de download leem do `Ambiente`.
- A homologação não tem origem padrão, porque não existe homologação hospedada. O build exige `-PcdbHomologacaoApi` e `-PcdbHomologacaoApp`, os dois em HTTPS, sem caminho e fora de `easystok.online` e `casadababa.com`. O `Ambiente` repete a checagem na abertura do app e falha fechado.
- Na homologação ficam desligados o Firebase, o push de avisos, o OTA, o aviso de APK novo e a página de download. São canais da produção: o OTA traria de volta o conteúdo web com a configuração da produção.
- `scripts/ambiente.cjs` faz o mesmo papel no conteúdo web (`config.js`). É também a regra única, testada, que o build aplica: o Gradle chama `node scripts/ambiente.cjs conferir` antes de montar.
  - O conteúdo web fica em `src/main/assets`, comum às variantes, então o build confere que o `config.js` foi empacotado para a variante e para a API do `-P`.
  - Na mesma chamada, o build confere as origens do `-P`.
- O empacotador de OTA (`ota-package.cjs`) só aceita conteúdo web oficial na API da produção. Conteúdo de homologação nunca chega aos aparelhos da operação.
- `homologacaoRelease` não existe: a homologação só se monta em debug e nunca assina com a chave oficial. `build.ps1` passa a montar `assembleOficialRelease` com `CDB_AMBIENTE=oficial`.

### Como montar

```powershell
$env:CDB_AMBIENTE = 'homologacao'
$env:CDB_API_BASE_URL = 'https://<api-de-teste>'
node scripts/copy-web.cjs
npx cap sync android
cd android
./gradlew assembleHomologacaoDebug -PcdbHomologacaoApi=https://<api-de-teste> -PcdbHomologacaoApp=https://<console-de-teste>
cd ..
Remove-Item Env:CDB_AMBIENTE, Env:CDB_API_BASE_URL
```

Limpe as variáveis no fim: elas ficam na sessão do PowerShell. O `build.ps1` e o empacotador de OTA recusam ou sobrescrevem a homologação, mas um `copy-web` solto na mesma sessão geraria conteúdo de homologação.

O `build.gradle` ainda lê `android/app/google-services.json` em todo build, mas a homologação não usa esses valores.

### Fora de S1

- Ambiente de homologação hospedado (subdomínio, base e deploy próprios). Até lá, a origem de teste precisa ser HTTPS: o laboratório local de `scripts/homologacao` serve HTTP em `127.0.0.1`. Expor esse laboratório por túnel público expõe também a senha de admin semeada nele; só com base descartável e túnel fechado ao fim do teste.
- O Console de teste (`-PcdbHomologacaoApp`) precisa falar com a mesma API de teste, porque o Workspace entrega a sessão nativa a ele.
- Login com Google na homologação: exige cliente OAuth do pacote `com.casadababa.piloto.homologacao` com o SHA-1 da assinatura de depuração. Até lá, a homologação entra por código de pareamento.
- Push na homologação: exige cadastrar o pacote no Firebase.
- Filtro de saída nos WebViews da homologação (`shouldInterceptRequest` só para `appHost` e `apiHost`), defesa em profundidade contra um Console de teste mal configurado. Sugestão da revisão da PR #3247.
- A recusa da produção é uma lista de dois domínios. A lista precisa acompanhar os hosts do `Caddyfile` (`APP_HOST`, `API_HOST`, `CASA_HOST`).

## S2: trava contra massa de teste (P2)

A desenhar na PR da S2. Ponto de partida: a API recusa pareamento e sincronização de aparelho que se declare homologação quando ela roda como produção, e o APK envia o ambiente em todo pedido.

## S3: backup do aparelho (P3)

A desenhar na PR da S3. Ponto de partida: backup periódico automático no aparelho e alerta quando houver dado sem cópia no servidor.
