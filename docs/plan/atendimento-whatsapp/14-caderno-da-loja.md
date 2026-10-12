# Onda 10 — Caderno da loja para o agente (S54–S56)

Issue: #1319 · Origem: teste do agente DeepSeek em 01/10/2026 (8 clientes, 15 mensagens) e a conversa
sobre RAG com o Felipe no mesmo dia.

Objetivo: o agente conhece a loja (políticas, entrega, troca, preparo, congelamento, perguntas
frequentes) sem que o prompt cresça junto com o conhecimento. Hoje o prompt tem cerca de 1 mil tokens de
regras e nada sobre a empresa; o que a dona escreve nas respostas prontas (S42) não chega ao modelo.

## Decisões (Felipe, 01/10/2026)

| Tema | Decisão |
|---|---|
| Forma | Caderno em **trechos** por empresa, não um texto único lido inteiro a cada chamada. |
| Camadas | **Núcleo** (sempre no prompt, teto de 6.000 caracteres) + **índice** (uma linha por trecho, sempre no prompt) + **texto do trecho** puxado sob demanda pela ferramenta `consultar_caderno`. |
| Cache | Núcleo e índice entram logo depois do prompt base e antes do dossiê do cliente, para o começo do prompt ser igual entre conversas da mesma empresa. |
| Onde mora | Banco do EasyStok, por empresa, com RLS. Fora do Atlas: é dado de cliente, sob LGPD. |
| Aprendizado | O agente não grava no caderno sozinho. Trecho novo nasce da dona (S55: o agente propõe, a dona aprova). |
| Busca | Fatia 1 só com índice. Pré-busca por palavra-chave (S55) e busca semântica com pgvector (S56) entram quando a bateria mostrar ganho. |

## Arquitetura

```
Mensagem do cliente
      │
AgenteAtendimentoService ── system = PromptAtendimento + CadernoParaAgente(núcleo + índice) + dossiê
      │                                                     ▲
      │                                   ICadernoRepository.ListarAsync(empresa, ativos)
      ▼
LLM ──(precisa do texto)──► consultar_caderno(codigos[]) ──► texto dos trechos (só da empresa da conversa)
```

Código do trecho = 8 primeiros caracteres hexadecimais do `Id` (estável, curto para o índice).

---

### S54 · Caderno em trechos com núcleo, índice e consulta

**Problema.** O agente não recebe conhecimento da empresa e escala ou responde genérico. Pôr tudo no
prompt cresce sem limite a cada chamada.
**Abordagem.** Entidade `TrechoCaderno`, API de cadastro para o console e duas peças no agente: o bloco
de núcleo e índice no prompt e a ferramenta `consultar_caderno`.
**Escopo.**
- `Domain/Entities/Atendimento/TrechoCaderno.cs`: `Titulo` (1–120), `Texto` (1–4.000), `PalavrasChave`
  (até 300, normalizadas: minúsculas, separadas por vírgula, sem repetição), `Nucleo`, `Arquivado`,
  `CriadoEm`, `AlteradoEm`; `Codigo` calculado do `Id`.
- `caderno_trechos` com RLS (ADR-0010), migration por `dotnet ef` (ADR-0024).
- `ICadernoRepository` + `CadernoRepository` (`EmpresaId` no `WHERE` de toda consulta).
- `CadernoUseCases`: listar, criar, editar, arquivar/desarquivar. Núcleo ativo somado acima de 6.000
  caracteres devolve `UseCaseValidationException` (400).
- `api/atendimento/caderno` (política `Operador`): `GET`, `POST`, `PUT {id}`, `POST {id}/arquivar`.
- `Services/Atendimento/CadernoParaAgente.cs`: monta o bloco (vazio quando não há trecho ativo).
- `AgenteAtendimentoService`: insere o bloco entre o prompt base e o dossiê.
- `Ferramentas/ConsultarCadernoFerramenta.cs`: `consultar_caderno(codigos: string[1..5])` devolve
  `{trechos:[{codigo,titulo,texto}], naoEncontrados:[...]}`; só trechos ativos da empresa da conversa.
**Testes (Red).**
- `TrechoCadernoTests`: valida obrigatórios e limites; normaliza palavras-chave; código tem 8 caracteres.
- `CadernoUseCasesTests`: núcleo acima do teto recusa; arquivado sai da listagem padrão.
- `CadernoParaAgenteTests`: sem trechos devolve vazio; núcleo inteiro e índice de uma linha por trecho.
- `ConsultarCadernoFerramentaTests`: devolve texto pelo código; código desconhecido em `naoEncontrados`.
- `AgenteAtendimentoServiceTests`: com trechos, o system traz o caderno antes do dossiê; sem trechos, igual ao atual.
- `CadernoRepositoryTests` (InMemory): lista só a empresa pedida e só ativos.
**Aceite.** Testes acima verdes; gate verde; bateria local medida antes e depois com um caderno de exemplo
(tokens por chamada, tempo, acerto).
**Rollback.** Arquivar os trechos (o prompt volta ao atual) ou reverter o commit; a migration só cria a tabela.
**Tier.** ALTO (migration + RLS).

### S55 · Pré-busca por palavra-chave e trecho proposto pelo agente (a especificar)

Antes de chamar o modelo, buscar no Postgres os trechos cujas palavras-chave aparecem na mensagem e
anexar os 2 ou 3 mais prováveis, poupando a ida à ferramenta (3,8 s por chamada no teste de 01/10).
Quando a dona responde uma conversa escalada, o agente propõe um trecho novo para ela aprovar.

### S56 · Busca semântica com pgvector (a especificar)

Só quando o índice passar de cerca de 300 trechos ou os canários da bateria mostrarem que a palavra-chave
não acha o trecho certo. A interface de `consultar_caderno` não muda.
