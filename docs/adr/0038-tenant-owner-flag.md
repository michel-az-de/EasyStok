# ADR-0038 · A dona da empresa: marca no vínculo e hierarquia de acesso

- Status: Aceita
- Data: 2026-10-26 (reescreve a proposta de 2026-06-28, `UsuarioEmpresa.IsOwner`, que nunca foi aplicada)
- Issue: #2070
- Relacionados: ADR-0010 (RLS), ADR-0031 (login em duas etapas), ADR-0056 (tenant único), N9 do plano
  `docs/plan/notificacoes-sistema/06-acesso.md` (convite)

## Contexto

A proposta de junho queria só um selo de "dono" no Admin, com backfill pelo Admin mais antigo. Em outubro o
problema ficou maior: a dona da Casa da Baba vai entrar pela primeira vez, e a auditoria sobre `0d905a6fc` achou
furos na hierarquia de usuários:

- A1: qualquer Admin trocava o e-mail de outro Admin; com convite pendente a troca era imediata e o convite novo
  ia para o invasor.
- A2: qualquer Admin rebaixava ou desativava outro Admin.
- A4: um Admin podia ocupar antes o e-mail de quem ia ser dona (convite com o WhatsApp dele).
- M1: nada garantia administrador ativo; a corrida entre rebaixar, desativar e anonimizar zerava a empresa.
- B1: "é SuperAdmin" era decidido só pelo nível do perfil, sem `EmpresaId` nulo, em sete lugares.

Um selo derivado por data não resolve nada disso: o que falta é autorização.

## Decisão

### 1. A marca

`UsuarioEmpresa.EhDona` (bool, falso por padrão). A dona é a marca mais um perfil de nível Admin na empresa.
Pode haver mais de uma dona. "Dona" na tela vem só da marca; o perfil de nível Admin da casa se chama
"Administração", e nasce "Gerência" (Gerente). O JWT não leva a marca.

**Dona ativa** = marca, vínculo ativo, usuário ativo e nível Admin na empresa. A convidada que ainda não aceitou
conta.

### 2. A hierarquia

```
Suporte global   Perfil.EhSuporteGlobal: nível SuperAdmin E Perfil.EmpresaId nulo
  └─ Dona        marca + nível Admin; sempre pelo menos uma ativa
       └─ Administração (Admin) ─ Gerência (Gerente) ─ Operador ─ Visualizador (e quem não tem perfil)
```

Uma regra só no domínio: `PoliticaDeHierarquia.Avaliar(ator, alvo, ação, donasAtivas, nívelNovo?)`. Ator e alvo
são lidos do banco no vínculo da empresa do pedido, nunca do JWT. Ações: `Criar`, `EditarNome`, `TrocarEmail`,
`TrocarPerfil`, `Desativar`, `Reativar`, `ReenviarConvite`, `MarcarDona`, `DesmarcarDona`.

| Ator \ alvo | Si mesmo | Dona | Administração | Gerência para baixo |
|---|---|---|---|---|
| Suporte global | tudo | tudo; `TrocarEmail` e `DesmarcarDona` com motivo (10+) | tudo | tudo |
| Dona | só o nome | nome, reativar, reenviar, desmarcar; e-mail é do suporte; perfil e desativar → `DONA_PROTEGIDA` | tudo, inclusive marcar | tudo |
| Administração | só o nome | `HIERARQUIA` | `HIERARQUIA` | tudo, menos dar perfil de nível Admin |

Ordem: o suporte só é alvo do suporte e nunca recebe a marca (`DONA_SUPORTE`); quem não é suporte precisa de
acesso ativo de nível Admin (`HIERARQUIA`, o caso do ator rebaixado com JWT antigo); sobre si mesmo só o nome
(`USUARIO_PROPRIO`); tirar a última dona ativa é `ULTIMA_DONA` para qualquer ator; depois a tabela.

Marcar exige alvo com acesso ativo de nível Admin (`DONA_NAO_ADMIN`) e conta provada: e-mail confirmado ou convite
aceito por e-mail ou Google, nunca só pelo WhatsApp (`DONA_CONTA_NAO_PROVADA`). Com zero donas, só o suporte marca
a primeira. Cadastrar já como dona (`POST api/usuarios` com `dona: true`) exige perfil de nível Admin, e-mail novo
(`DONA_EMAIL_EXISTENTE`, sem sugerir marcar a conta existente) e convite só por e-mail.

HTTP: `HIERARQUIA` é 403; `DONA_PROTEGIDA`, `ULTIMA_DONA`, `DONA_NAO_ADMIN`, `DONA_CONTA_NAO_PROVADA` e
`DONA_EMAIL_EXISTENTE` são 409; `USUARIO_PROPRIO`, `DONA_SUPORTE` e `MOTIVO_OBRIGATORIO` são 400.

### 3. O invariante

Toda escrita de hierarquia (criar, editar nome ou e-mail, trocar perfil, desativar, reativar, reenviar, marcar,
desmarcar, anonimizar) roda em `ExecuteInTransactionSemRetryAsync` com
`pg_advisory_xact_lock(hashtextextended('hierarquia:'||EmpresaId,0))` (`IUsuarioEmpresaRepository.TravarHierarquiaAsync`),
relê ator e alvo depois da trava e conta as donas ativas com `COUNT(DISTINCT UsuarioId)` e `EmpresaId` explícito.
A trava também liga a empresa travada na RLS da transação (`set_config` local). A marca de suporte global (perfil sem
empresa, que a RLS esconde dentro da transação) é lida antes da trava, onde o repositório abre a conexão com o bypass;
nenhuma escrita de hierarquia muda essa marca.

Anonimizar a última dona é `ULTIMA_DONA`; a dona que não é a última perde a marca antes de anonimizar, numa transação
com a trava de cada empresa. O suporte que rebaixa uma dona (nunca a última) também tira a marca.

A guarda de tenant ("Usuario nao encontrado") roda antes da política. Toda recusa grava `hierarquia-recusada`
(`Sucesso = false`) no rastro de usuários; marcar e desmarcar gravam `dona-marcada` e `dona-desmarcada`. Ação do
suporte sobre a dona com motivo vai também para o `AdminAuditService`.

### 4. Sem backfill

A migração cria a marca falsa para todos. Ninguém é promovido por heurística: o suporte marca a primeira dona de
cada empresa existente. Tenant novo (`CriarTenantPorAdminUseCase`) já nasce com o primeiro Admin marcado.

### 5. B1 e a sessão

"É suporte" tem um predicado só (`Perfil.EhSuporteGlobal`, e a expressão `Perfil.SuporteGlobal` nas consultas do
EF), com teste de arquitetura que proíbe comparar o nível SuperAdmin de um perfil fora de `Perfil.cs`. Perfil de
empresa com nível SuperAdmin vale Administração (`Perfil.NivelNaEmpresa`) e nunca abre `api/admin/*`.

Login, Google e refresh resolvem a sessão pela mesma regra (`ResolvedorDeSessao`). O refresh renova na empresa da
sessão (`refresh_tokens.EmpresaDaSessaoId`): o suporte volta SuperAdmin na mesma empresa; os demais precisam de
vínculo ativo com ela. Token antigo, sem a coluna, segue a única empresa ativa.

## Alternativas consideradas

| Opção | Veredito |
|---|---|
| Selo derivado (Admin mais antigo por data) | Rejeitada: não autoriza nada e adivinha a dona. |
| `Empresa.UsuarioDonoId` | Rejeitada: uma dona só, e a empresa tem sócias. |
| Nível novo `Dona` em `NivelAcesso` | Rejeitada: espalharia o nível por JWT, políticas e módulos; a marca fica no vínculo. |
| Backfill pelo Admin mais antigo | Rejeitada: promoveria quem não é dona. O suporte marca. |
| Trava por linha (`FOR UPDATE`) | Rejeitada: a contagem cobre linhas que ainda não existem (criar, marcar). |

## Consequências

- Uma coluna em `usuarios_empresas` e uma em `refresh_tokens`, aditivas.
- Toda escrita de usuários de uma empresa serializa na trava dela; o volume é de poucas por dia.
- A Administração deixa de alcançar outra Administração e a dona; quem precisar disso pede à dona ou ao suporte.
- Rollback: revert do squash e o down da migração. O perfil renomeado continua "Administração" (mesmo Id e
  permissões); o seed antigo procura "Dona" pelo nome e criaria outro perfil, então o rollback renomeia a linha de
  volta antes de subir a versão antiga.

## Verificação

- Matriz parametrizada da política (3 atores × 6 alvos × 9 ações) no `EasyStock.Domain.Tests`.
- Corridas em Postgres real com RLS e duas conexões com barreira (duas donas se desmarcando, rebaixar contra marcar,
  desativar e anonimizar a última dona): exatamente uma passa; com a trava desligada, o teste de controle zera a
  empresa.
- Matriz HTTP reduzida, refresh do suporte, Google só com convite aberto e perfil de empresa com nível SuperAdmin
  barrado em `api/admin/*`, no `EasyStock.Api.IntegrationTests`.
