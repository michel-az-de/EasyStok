# ADR-0048 — FMA Informática como segundo tenant: plataforma única

- Status: Accepted
- Data: 2026-08-08
- Contexto: épico #1013, issue #1014
- Relacionado: ADR-0010 (RLS Postgres), ADR-0038 (tenant owner flag), ADR-0047 (login multi-empresa)

## Contexto

O EasyStok nasceu com pretensão de SaaS de mercado, mas essa pretensão acabou. Hoje ele é
**ferramenta interna da Casa da Babá** (food service). O mesmo dono tem uma segunda empresa,
a **FMA Informática** (prestação de serviços de TI), e quer os sistemas dela — financeiro,
CRM, cadastros, contratos, propostas — numa plataforma que ele mesmo gerencie.

Restrições declaradas, que valem mais que qualquer elegância arquitetural aqui:

- **Nenhuma das duas empresas fatura pelo sistema.** Fiscal e NFS-e estão fora de escopo.
- **Operação solo.** Um desenvolvedor, que também é o operador e o revisor.
- **Critério de escolha: resultado mais rápido.** Não "arquitetura mais limpa", não
  "preparado para escalar".
- No máximo, um dia, licenciar para conhecidos; "quando maduro, estudar vender" é hipótese
  distante, não requisito.

A pergunta a decidir é: onde os sistemas da FMA moram?

## Fatos medidos

Medidos em 2026-08-08, **nesta data e neste repositório** — não herdados da conversa que
originou a avaliação:

| Fato | Medição |
|---|---|
| Financeiro existente | 2.110 linhas em `Application/UseCases/Financeiro`; 6 controllers; **36 endpoints** (contas a pagar, a receber, categorias, centros de custo, dashboard) |
| CRM, propostas, contratos | **Não existem.** Só há `LeadPublico`, que é do site de marketing. Greenfield em qualquer cenário |
| `Cliente` suporta PJ? | **Não.** Tem `Nome`, `Documento`, `Cpf`, `Telefone`, `Email` — sem CNPJ, razão social ou tipo de pessoa |
| Acoplamento do front | `EasyStock.Web` e `EasyStock.Admin` têm **zero** `ProjectReference`: consomem a Api 100% por HTTP |
| Tenancy | RLS Postgres com defesa em profundidade (ADR-0010); `Empresa`, `UsuarioEmpresa`, tenant owner (ADR-0038) |
| Esqueleto multi-tenant | `Plano`, `AssinaturaEmpresa`, `CobrancaAssinatura`, `TenantFeatureFlag`, back-office no Admin |
| `TenantFeatureFlag` | Tabela existe e é isenta do filtro de tenant — mas **nenhum código a lê em runtime** |
| Banco | 1 `DbContext`, 129 `DbSet` |
| Stack | .NET 10 |
| Login multi-empresa | **Já funciona** (ADR-0047, #1007): usuário com 2+ empresas escolhe no login |

Duas medições mandam na decisão. A primeira: **o financeiro — que é o maior pedaço do que a
FMA precisa — já existe e é maduro.** A segunda: **o que falta (CRM, propostas, contratos)
não existe em lugar nenhum**, então será escrito do zero independentemente de onde morar.

## Decisão

**Plataforma única. A FMA entra como um segundo tenant (`Empresa`) do EasyStok**, com os
módulos novos (Cliente PJ, Propostas, Contratos, CRM leve) escritos no mesmo backend e
visíveis por feature flag de tenant. Mesmo front Web, com a navegação por módulo (ADR-0046)
filtrada por tenant.

Sem fork. Sem greenfield. Sem microserviços. Sem segundo deploy.

O caminho de implementação está no épico #1013; a ordem é: ligar o `TenantFeatureFlag` →
Cliente PJ → Propostas → Contratos + recorrência → CRM leve.

## Alternativas consideradas

**Fork do repositório para a FMA.** Descartada. Dobraria o custo de manutenção para uma
operação solo: toda correção de bug no financeiro, no auth, no deploy ou na infra teria que
ser aplicada duas vezes, com os dois lados divergindo em silêncio. O ganho seria isolamento —
que o RLS já entrega dentro de um banco só.

**Greenfield separado para a FMA.** Descartada pelo critério declarado (resultado mais
rápido). Jogaria fora 36 endpoints de financeiro maduro, mais auth, multi-tenancy, RLS,
deploy, observabilidade e design system — para reescrever tudo isso antes da primeira
proposta emitida.

**Microserviços / segundo backend.** Descartada. Não há problema de escala, de time ou de
domínio que justifique a fronteira: são duas empresas de um dono só, num banco só, num deploy
só. Seria complexidade sem contrapartida.

**Front separado para a FMA, consumindo a mesma Api.** Descartada **por ora**, não por
princípio. `Web` e `Admin` já têm zero `ProjectReference` — a fronteira é HTTP pura, então a
porta continua aberta para fazer isso depois sem tocar o backend. Hoje seria um segundo front
para manter sozinho, com um design system para replicar, sem ganho.

**Modelar a FMA como uma "loja" da Casa da Babá.** Descartada, e vale registrar porque é o
atalho tentador. Loja é subdivisão operacional **dentro** de uma empresa: compartilha
clientes, plano de contas, usuários e assinatura. Duas empresas distintas com CNPJs
diferentes não podem compartilhar isso — seria misturar o financeiro das duas.

## Consequências

### O que fica mais fácil

- A FMA usa o financeiro, o auth, o multi-tenancy, o deploy, os backups e a observabilidade
  que já existem e já estão em produção. O primeiro valor entregue é o Cliente PJ, não
  infraestrutura.
- Uma base de código, um pipeline, um lugar para corrigir bug.
- O login multi-empresa (ADR-0047) já está pronto: o usuário que acessar as duas empresas
  escolhe no login, sem trabalho adicional.

### O que fica mais difícil (assumido conscientemente)

- **O `DbContext` cresce.** Já são 129 `DbSet`; os módulos B2B somam mais. Aceito enquanto for
  um monólito modular de dono único; se um dia incomodar, o corte natural é por contexto, não
  por empresa.
- **Todo módulo novo precisa de gate por tenant.** Um módulo sem flag aparece para a Casa da
  Babá, e "Propostas" no menu de uma cozinha é ruído. O aceite do épico exige verificar isso
  **em tela**, não só na configuração.
- **O `TenantFeatureFlag` precisa deixar de ser decorativo.** Hoje a tabela existe e ninguém
  a lê — é o pré-requisito de tudo, e por isso é o item 0 do épico. Enquanto não for lido em
  runtime, "módulo por tenant" não existe de fato.
- **Risco de vazamento entre tenants passa a ter consequência real.** Com um tenant só, uma
  falha de RLS era teórica; com dois, ela mistura os dados de duas empresas do mesmo dono.
  O ADR-0010 (defesa em profundidade) deixa de ser precaução e vira requisito ativo.

### O que esta decisão NÃO decide

Fiscal e NFS-e continuam fora de escopo — nenhuma das duas empresas fatura pelo sistema.
Licenciar para terceiros ou vender o produto seguem hipóteses; se um dia virarem requisito,
o esqueleto SaaS (`Plano`, `AssinaturaEmpresa`, cobrança) já está lá, mas nada aqui foi
desenhado para isso.
