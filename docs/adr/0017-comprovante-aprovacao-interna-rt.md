# ADR-0017 — Comprovante de Aprovação Interna do Responsável Técnico (RT)

**Status**: Aceito (MVP P-02 Rotulagem Nutricional)
**Data**: 2026-05-16
**Autores**: Felipe Azevedo

## Contexto

O módulo P-02 Rotulagem Nutricional exige aprovação de Responsável Técnico
(nutricionista com CRN) para rótulos de Suplemento Alimentar e Alimento
Infantil (RDC 27/2010 e RDC 243/2018). A aprovação precisa ser auditável
e rastreável em caso de fiscalização Anvisa.

A solução ideal seria assinatura digital ICP-Brasil A1/A3, mas:
- Custo do certificado (R$ 150-300/ano por RT)
- Fricção de uso (token físico A3 ou senha A1)
- Integração técnica não-trivial (.NET + cadeia de confiança ICP)
- Inviável no MVP de 8-10 semanas part-time

## Decisão

O MVP entrega **Comprovante de Aprovação Interna** — NÃO é assinatura
digital legalmente válida, é registro interno auditável de que houve
aprovação no sistema por usuário com `Cargo=RT`.

### Estrutura

`AprovacaoRtRotulo`:
- `ComprovanteAprovacaoInternaHash` = SHA256(nome + CRN + timestamp +
  rotuloId + **secret de servidor**)
- O secret de servidor (`Anvisa_AprovacaoSecret` em config) impede que
  terceiros gerem o mesmo hash apenas com dados públicos
- `AssinaturaImagemUri` (nullable): upload opcional de imagem da
  assinatura física scaneada (JPG/PNG até 2MB)
- `AprovadoEm`, `ResponsavelTecnicoId`, `ObservacoesRT`

### Honestidade radical na UI

- Badge no Rotulo: "Aprovado por {Nome} ({CRN}) em {data}"
- Tooltip: "Comprovante de aprovação interna no sistema. Não é assinatura
  digital ICP-Brasil — disponível em versão futura."
- Disclaimer no modal de aprovação RT: "Este sistema gera comprovante
  interno de aprovação. Para assinatura digital legalmente válida
  (ICP-Brasil), aguarde a versão F+1."

## Consequências

### Positivas
- Viabiliza MVP sem barreira de certificado digital
- Rastreabilidade interna preservada (quem, quando, qual rótulo)
- Hash com secret protege contra reprodução por terceiros
- Honestidade reduz exposição legal (não vendemos como assinatura digital)

### Negativas
- Não é prova legal definitiva em fiscalização Anvisa
- RT que aprovar precisa confiar na cadeia interna do EasyStok
- Migração para ICP-Brasil em F+1 vai exigir backfill de comprovantes
  existentes (campo paralelo `AssinaturaDigitalIcpBrasilUri`)

## Alternativas consideradas

1. **ICP-Brasil A1 no MVP**: descartada — custo + fricção + complexidade
2. **DocuSign**: descartada — custo recorrente alto para volume baixo
3. **Aprovação sem hash, só registro**: descartada — sem integridade
4. **Bloquear suplemento/infantil até F+1**: descartada — limita oferta
   competitiva no MVP

## Implementação

Ver plano: [docs/plan/p-02-rotulagem-nutricional.md](../plan/p-02-rotulagem-nutricional.md)
seção "Pontos de Atenção/Riscos" item 7 e "Comprovante de Aprovação
Interna (RT)" no UX Copy §8.

## Migração para F+1

Quando ICP-Brasil for adicionado:
- Novo campo `AssinaturaDigitalIcpBrasilUri` em `AprovacaoRtRotulo`
- UI passa a oferecer "Assinar digitalmente" além de "Aprovar como RT"
- Rotulos antigos mantêm apenas `ComprovanteAprovacaoInternaHash`
  (não há backfill retroativo de assinatura digital)
- Diferenciação visual no badge: "Aprovação interna" vs "Assinatura digital"
