# Incidente: Alpine.js não executa em prod → interatividade morta (Web + Admin)

- **Data:** 2026-06-02
- **Severidade:** Alta (toda a interatividade client-side do Admin e do Web fora do ar em prod)
- **Status:** Corrigido em master (remoção do SRI + self-host); aguardando deploy p/ valer em prod.

## Sintoma

QA de prod reportou ~6 "bugs" separados no Admin:
- Abas inertes (não trocam, não destacam a ativa) — ex.: detalhe do tenant.
- Páginas com dashboards vazios: Operação, Diagnóstico, Dispositivos (métricas em "—", nenhuma chamada de API).
- Busca global (Ctrl+K) não abre.
- Card "Sincronização Mobile" travado em "carregando…".

No Web, o mesmo: count-up dos stat-cards, modais e comboboxes inertes.

## Diagnóstico (ao vivo, via browser contra prod)

`window.Alpine === undefined` em **todas** as páginas. Tudo que é `x-data`/`x-init`/`x-on`
depende do Alpine → com ele morto, todos os sintomas acima caem juntos.
**Os ~6 "bugs" eram 1 só.** (`esTabs`/`esDataTable` carregavam, mas só são chamados
via Alpine.)

Os 3 scripts do Alpine (jsDelivr) **baixavam (HTTP 200, tamanhos corretos:
core 44758, focus 14937, persist 837 bytes) mas NÃO executavam** — assinatura
clássica de bloqueio por SRI. Bloqueio de SRI é erro de segurança do browser e
**não aparece no console** (por isso "sem erros" no console enganava).

## Causa-raiz (dupla)

1. **Hashes SRI errados.** Os atributos `integrity=` dos `<script>` não batiam com
   o conteúdo servido → o browser baixa e **recusa executar**. Prova irrefutável:
   o **mesmo** arquivo `alpinejs@3.14.9/dist/cdn.min.js` tinha `integrity`
   **diferente** no Web (`sha384-9Ax3…`) e no Admin (`sha384-45lA…`) — o mesmo
   arquivo não pode ter dois hashes corretos.
2. **jsDelivr bloqueado pela rede corporativa.** `curl`, `npm` e re-fetch no
   browser contra `cdn.jsdelivr.net` falharam deste ambiente (firewall Avanade).
   Mesmo sem SRI, fresh loads do CDN são não-confiáveis aqui.

## Correção

1. Removido `integrity`/`crossorigin` dos 3 scripts (`fix(admin,web): remove SRI…`).
2. **Self-host** do Alpine: `npm pack alpinejs@3.14.9 @alpinejs/persist @alpinejs/focus`
   (registry corporativo funciona — o `npm pack` só falhava por mangling do `@` no
   shell; aspas simples resolvem) → `dist/cdn.min.js` vendored em
   `wwwroot/js/vendor/alpine/` (Admin: core+persist+focus; Web: core), servido da
   **mesma origem**. Ordem plugins-antes-do-core preservada. (`fix: self-host Alpine…`)

Self-host elimina **as duas** causas de uma vez (sem SRI, sem CDN externo).

## Prevenção

- **Não usar SRI em script de CDN crítico** sem um passo de CI que valide/atualize
  os hashes — hash errado = script morto silencioso.
- **Preferir self-host de JS crítico** (Alpine, etc.) em vez de CDN externo — ainda
  mais atrás de firewall corporativo. Dependência de rede externa em runtime é frágil.
- **Smoke pós-deploy:** verificar `typeof window.Alpine !== 'undefined'` numa página
  autenticada do Admin e do Web. Um único check pega esta classe inteira de bug.
- **Triagem:** N sintomas de runtime que somem juntos cheiram a 1 causa-raiz — medir
  o estado no browser (`window.Alpine`, console, network) **antes** de tratar cada
  sintoma como bug separado.
