# Design-system audit — Cardápio do Tenant (EasyStock.Web)

> **Fase 3 (UX) do plano ADR-0031.** Mapeia cada elemento da UI de cardápio aos componentes e
> tokens **já existentes** no EasyStock.Web. Regra de ouro: **zero hardcode** de cor/espaçamento/raio —
> tudo via tokens (`wwwroot/css/tokens.css`) e componentes (`Views/Shared/_*.cshtml`, TagHelpers `es-*`).
> Fonte medida: `tokens.css`, `components.css`, `_FormField.cshtml`, `_CreatableCombobox.cshtml`,
> `_Modal.cshtml`, `_ConfirmModal.cshtml`, `_EmptyState.cshtml`, `_Toast.cshtml`, TagHelpers `es-*`.

## Veredito da auditoria

O design system do Web **cobre 100%** da superfície de cardápio sem precisar de componente novo.
Nada de CSS ad-hoc nas Views. O Web tem TagHelpers `es-*` (conjunto menor que o Admin) **e** partials
Razor — usar o que já existe. Diferença vs Admin: o Web **não** tem `es-input`/`es-checkbox` TagHelper;
use `_FormField` + `<input class="ui-field">` (primitivo `ui-form-field` em `tokens.css`).

## Mapa elemento → componente/token

| Elemento (copy-deck) | Componente / classe | Token-chave |
|---|---|---|
| Título + subtítulo da página | `.pg-hd` (padrão Lojas/Index) | `--text-primary`, `.t-h2` |
| CTA "Adicionar item" | `<es-button variant="primary" icon="plus">` | `--action-accent` (orange-700, AA) |
| "Imprimir cardápio" | `<es-button variant="ghost" icon="printer" href=...>` | `--navy-700` |
| Tabela do cardápio | `<es-data-table>` (colunas + ações + is-empty) | `--table-row-h`, `--border-soft` |
| Badge "Rascunho" | `<es-badge variant="warn">` | `--warn-50/700/200` |
| Badge "avulso" | `<es-badge variant="neutral">` | `--ink-100/700/200` |
| Badge "Disponível" / "Esgotado" | `<es-badge variant="ok">` / `variant="crit">` | `--ok-*` / `--crit-*` |
| Botões inline (Publicar/Toggle) | `<es-button variant="secondary" size="sm">` | `--border-soft`, `--navy-700` |
| Ação "Remover" | `<es-button variant="danger" size="sm">` + `$dispatch('confirm-action')` | `--intent-destructive` |
| Tooltip (rascunho/avulso) | `[data-tooltip="..."]` (CSS-only em tokens.css) | `--ink-900`, `--z-dropdown` |
| Seletor de loja (2+) | `_FormField` + `<select class="ui-field">` | `ui-form-field__label` |
| Empty state | `<es-empty-state icon="utensils-crossed" title=... description=...>` | `--ink-50`, `.t-lead` |
| Loading da lista | `<es-skeleton>` nas linhas | `--ink-100` |

### Formulário criar/editar
| Elemento | Componente | Notas |
|---|---|---|
| Campo de texto/número/url | `_FormField` (label+helper+erro+`role=alert`) envolvendo `<input class="ui-field">` | já tem `aria-describedby`, `ui-form-field--error` |
| Toggle modo avulso/vinculado | radios Alpine `x-data`/`x-model` + `x-show` | sem componente novo; padrão do app |
| Categoria (datalist + criar) | `_CreatableCombobox` (`role=combobox`, `aria-expanded`, criação inline) | acessível, já existe |
| Dropdown produto (vinculado) | `_CreatableCombobox` ou `_Combobox` alimentado por `ProdutosService` | busca por nome |
| Descrição + contador | `<textarea class="ui-field">` + `x-text` contador `aria-live="polite"` | inline Alpine |
| Checkbox "Publicar agora" | `<input type="checkbox" class="ui-checkbox">` em `_FormField` | default OFF |
| Seção colapsável (detalhes) | `<details>`/Alpine `x-data="{open:false}"` + `x-collapse` | `aria-expanded` nativo |
| Rodapé de ações | `.form-actions-sticky` (já em tokens.css) | sticky bottom, `--bg-surface` |
| Botão submit com loading | `<es-button loading>` / `.btn[aria-busy="true"]` | spinner inline já no token |

### Feedback
| Elemento | Componente | Notas |
|---|---|---|
| Toasts (sucesso/erro/undo) | `BaseController.Toast("success"/"error", msg, undoUrl)` → `_Toast.cshtml` (`role=region aria-live=polite`) | undo já suportado |
| Confirmação de remoção | `_ConfirmModal` via `$dispatch('confirm-action', {...})` | título+corpo+botões nomeados |
| Modal criar/editar (se inline) | `_Modal` via `$dispatch('open-modal', 'cardapio-form')` | ou página dedicada `/cardapio/{id}/editar` |
| Erro de validação | `ViewData.ModelState` → `_FormField` Error slot (`role=alert`) | sem cor-only |

## Regras para a Fase 4

1. **Nunca** `style="color:#..."` nem `class="text-orange-600"` ad-hoc — usar `es-*`/tokens.
2. Ações na tabela: `aria-label` com o nome do item ("Publicar Lasanha").
3. Botão de ação primária (CTA, foco) usa `--action-accent` (orange-700 AA), não orange-600.
4. Touch targets 44px (já no token `.btn` mobile) — não fixar largura.
5. `[x-cloak]` para anti-flash de `x-show` (NUNCA `display:none` em classe persistente — gotcha recorrente do topbar).
6. Decimais: confiar no `InvariantDecimalModelBinderProvider` (Program.cs) — input de preço com vírgula/ponto.
7. Navegação: registrar "Cardápio" no menu via o `MenuDefinition` novo (ADR-0032 da sessão paralela),
   não editar `_Sidebar.cshtml` direto — alinhar com o estado commitado em `03aee000`.

## Lacunas / decisões

- O Web é **BFF HTTP puro**: as Views consomem o `CardapioService` (Fase 4) que fala com
  `/api/minha-vitrine/cardapio` (Fase 1). Sem `ProjectReference` ao Domain — DTOs próprios em `Models/Api/`.
- `es-input`/`es-checkbox` TagHelper não existem no Web → usar `_FormField` + `ui-field`. (Se a Fase 4
  repetir muito, vale extrair um `es-input` no Web — registrar como issue, não bloquear.)
