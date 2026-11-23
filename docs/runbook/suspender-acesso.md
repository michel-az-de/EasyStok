# Suspender o acesso de uma pessoa (#3185)

O bloqueio por senha errada **não suspende a conta**. Ele protege a senha e nada mais.

## O que o bloqueio faz

- Cinco senhas erradas seguidas (`Usuario.FalhasParaBloquear`) bloqueiam o login **por senha** por `Usuario.MinutosDeBloqueio` minutos (15 hoje).
- Vale para o passo 1 do login (lista de empresas), o login completo, o código de acesso e a troca de contato.
- Desde a #3170 ele **não barra a entrada pelo Google**, no console e no aparelho: a senha não participa desse login (`AutenticarUsuarioUseCase.RevalidarLoginGoogleNativoAsync`, teste `LoginGoogleNativo_ContaBloqueadaPorSenhaEntra`).
- Também não derruba sessão aberta: quem já está dentro continua.

Quem errar a senha de outra pessoa de propósito só atrasa o login por senha dela. Não tira ninguém do sistema.

## Como suspender de verdade

| Quero | Faço | Efeito |
|---|---|---|
| Tirar a pessoa da casa | Console, Configurações, Usuários, **Desativar** | Desativa o vínculo com a empresa, derruba as sessões (`RevogadorSessoes`), revoga convite e código de acesso abertos. Nenhum login entra nesta empresa, nem pelo Google. Quem tem vínculo com outra empresa continua entrando lá |
| Derrubar as sessões sem tirar o acesso | Trocar o perfil da pessoa ou redefinir a senha | Grava o corte `SessoesValidasDesde`: os tokens emitidos antes param de valer e a pessoa entra de novo |

A dona só é desativada depois de perder a marca de dona, e a última dona ativa não perde a marca (ADR-0038). Nesses casos quem resolve é o suporte.
