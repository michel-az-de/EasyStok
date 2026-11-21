# A correção de vínculo vale para as próximas conversas do telefone (#2492)

Status: aprovada pelo Felipe em 13/11/2026 (D1 a D5 como propostos). M2 da revisão R4 de Cadastros (#2395).

## Problema

Hoje a dona corrige o cliente de uma conversa pelo `POST /api/atendimento/conversas/{id}/vinculo-cliente`
(ações `retirar`, `restaurar` e `vincular`, em `VinculoClienteConversaUseCase`). A correção fica só na
`Conversa` e na trilha `alteracoes_vinculo_conversa`.

Quando a conversa fecha, a próxima mensagem do mesmo telefone abre uma `Conversa` nova, sem cliente
(`ProcessarEventoWhatsAppUseCase.cs:154` e `:293`). O `IdentificarClientePorTelefoneUseCase` procura pelo
telefone na seguinte ordem:

1. telefone compartilhado, que deixa a identidade pendente;
2. `TelefoneHash`;
3. `FindByTelefoneAsync`.

Ele não tem nenhum filtro de exclusão, então religa a mesma pessoa errada. Hoje não existe no banco
nenhum registro do tipo "este telefone não é este cliente".

Efeito: a dona corrige toda vez. Enquanto não corrige, o robô trata a pessoa pelo nome, pelo histórico e
pelos pedidos de outra pessoa.

## Decisão

Uma **decisão de vínculo por telefone**, por empresa, gravada no mesmo instante da correção. Ela diz qual
cliente o telefone NÃO é e, quando a dona escolheu outro, qual cliente ele É.

| Ação da dona na conversa | O que é gravado | Próxima conversa do telefone |
|---|---|---|
| `retirar` (cliente Y) | Recusa: o telefone não é Y | Lookup normal, pulando Y. Se só sobrar Y, fica sem vínculo e com a identidade pendente |
| `vincular` (de Y para Z) | Recusa de Y e escolha de Z | Vincula Z direto, antes do lookup |
| `restaurar` (volta para Y) | Revoga a decisão ativa | Lookup normal, como hoje |

Regras:

- **Há uma decisão ativa por telefone, e a última vence.** Uma correção nova revoga a anterior e grava a
  dela. Nada é apagado: a revogação marca `RevogadaEm` e quem revogou, e o histórico fica.
- **A decisão não expira.** Ela sai por `restaurar`, por uma correção nova, ou quando o cliente escolhido
  deixa de existir ou é desativado. Nesse último caso o lookup ignora a escolha e mantém a recusa.
- **A decisão vem antes do teste de telefone compartilhado.** Se a dona já disse que o telefone é Z, isso
  resolve a ambiguidade que hoje deixa a identidade pendente.
- **Quando só sobra o cliente recusado, não se cria um lead novo.** Um lead novo teria o mesmo telefone de
  Y, e a partir daí o telefone passaria a ser compartilhado. A conversa nasce sem cliente e com
  `VinculoEmRevisao`, igual ao caminho de telefone compartilhado. A equipe vê "identidade a conferir" e
  vincula pela ficha.
- **O telefone é guardado como hash**, no padrão do `ClienteOtp.CalcularTelefoneHash`: SHA-256 do E.164
  canônico com `+` e nono dígito, que é o `candidatos[0]` do `NormalizadorTelefone`. Não há telefone em
  claro na tabela nova. Na consulta, o lookup calcula o hash de todos os candidatos do wa_id, com e sem o
  nono dígito, e então acha a decisão gravada com qualquer grafia.
- **Só o webhook do WhatsApp lê a decisão** (linhas 154 e 293, incluindo o eco do app). Quem prova
  identidade não passa pela decisão: OTP, comanda, chat do site e cadastro feito pela equipe continuam como
  hoje.

## Modelo

Tabela `decisoes_vinculo_telefone`, com RLS `tenant_isolation`, `ENABLE` e `FORCE`, igual à migration
`20261112191242_CadastrosR4EVinculoCliente`.

| Coluna | Tipo | Nota |
|---|---|---|
| `Id` | uuid | PK |
| `EmpresaId` | uuid | Tenant |
| `TelefoneHash` | varchar(64) | SHA-256 hex do E.164 canônico |
| `ClienteRecusadoId` | uuid null | O telefone não é este cliente |
| `ClienteEscolhidoId` | uuid null | O telefone é este cliente (só em `vincular`) |
| `ConversaOrigemId` | uuid | A conversa onde a dona decidiu |
| `AlteracaoVinculoId` | uuid | Liga à linha de `alteracoes_vinculo_conversa` |
| `UsuarioId`, `CriadaEm` | | Quem decidiu e quando |
| `RevogadaEm`, `RevogadaPorUsuarioId` | null | Preenchidos ao revogar |

Índice único parcial `(EmpresaId, TelefoneHash) WHERE RevogadaEm IS NULL`, que garante uma decisão ativa
por telefone. Não há FK para `clientes`: se o cliente for excluído, a decisão sobrevive e o lookup ignora
a escolha.

## Onde muda

| Camada | Mudança |
|---|---|
| Domain | Entidade `DecisaoVinculoTelefone`, com `Recusar`, `Escolher` e `Revogar` |
| Infra | Configuração EF, migration com RLS, repositório na porta `IVinculoClienteConversaRepository` |
| Application | `VinculoClienteConversaUseCase` grava ou revoga a decisão na mesma transação da correção. `IdentificarClientePorTelefoneUseCase` recebe a decisão ativa, vincula o escolhido e pula o recusado |
| Console | Texto em `CorrecaoVinculoCliente.jsx`: "Vale também para as próximas conversas deste telefone." Sem tela nova |

Sem mudança de contrato HTTP. O corpo de `POST vinculo-cliente` continua o mesmo.

## Testes (Red antes)

1. Integração, com Postgres: corrigir a conversa de Y para Z, encerrar, chegar mensagem nova do mesmo wa_id
   e a conversa nova nascer com Z. Hoje nasce com Y, e esse é o Red.
2. Integração: `retirar` Y quando só Y tem o telefone. A conversa nova nasce sem cliente, com
   `VinculoEmRevisao`, e sem lead novo.
3. Integração: `restaurar` revoga a decisão, e a conversa seguinte volta a vincular Y.
4. Integração, RLS: a decisão da empresa A não aparece no lookup da empresa B com o mesmo telefone.
5. Unidade: wa_id sem nono dígito acha a decisão gravada com nono dígito.
6. Unidade: escolha cujo cliente foi desativado é ignorada, e a recusa continua valendo.

## Rollback

Reverter a PR. A migration só cria a tabela nova; o `Down` dropa a policy e a tabela. Sem a decisão, o
lookup volta ao de hoje. As correções feitas nesse intervalo continuam na trilha
`alteracoes_vinculo_conversa`.

## Fora

- Desfazer a decisão por uma tela própria de "decisões por telefone". `restaurar` e uma nova correção
  bastam na primeira versão.
- Fazer o OTP verificado revogar a recusa. O OTP já vincula pela prova de posse e não passa por esse
  lookup.
- Mesclar cadastros duplicados.
- Remover o `GET candidatos-cliente` legado.

## Decisões para o Felipe

| # | Pergunta | Decisão (aprovada em 13/11) |
|---|---|---|
| D1 | Só sobrou o recusado: criar lead novo ou ficar sem vínculo? | Sem vínculo, com a identidade pendente |
| D2 | A decisão expira? | Não. Sai por `restaurar` ou por nova correção |
| D3 | Gravar a escolha (Z), além da recusa? | Sim, porque é o caso comum: a dona aponta a pessoa certa |
| D4 | A decisão vence o telefone compartilhado? | Sim |
| D5 | Telefone em hash? | Sim, no padrão do `ClienteOtp` |
