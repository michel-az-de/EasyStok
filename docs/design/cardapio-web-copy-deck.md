# Copy-deck — Gestão de Cardápio do Tenant (EasyStock.Web)

> **Fase 3 (UX) do plano ADR-0031.** Microcopy PT-BR pronta para as Views Razor da Fase 4.
> **Persona:** Thatiane (Casa da Babá) — dona de negócio de comida artesanal, não-técnica, gerencia
> o próprio cardápio. **Tom:** prático e honesto (EasyStok) + acolhedor (Casa da Babá). Sem jargão,
> sem "sistema", sem "registro/entidade". Fala com o cliente como uma pessoa que ajuda.
>
> **Princípios aplicados:** verbo no CTA · erro = o que aconteceu + por quê + como resolver ·
> empty state = o que é + por que vazio + como começar · confirmação nomeia a ação e a consequência.
> **A11y:** todo label tem `for`/`id`; erro com `role="alert"`; helper via `aria-describedby`; o `*`
> de obrigatório é `aria-hidden` e a obrigatoriedade vai no helper, não só na cor.

---

## 1. Listagem do cardápio

| Elemento | Copy | Notas |
|---|---|---|
| Título da página | **Cardápio** | `ViewBag.Title`. Menu lateral: "Cardápio" |
| Subtítulo | O que aparece na sua vitrine online. | tom: orienta sem explicar demais |
| CTA primário | **Adicionar item** | verbo + objeto; `es-button variant="primary"` |
| Link impressão | **Imprimir cardápio** | abre `/{slug}/menu/imprimir` em nova aba; `es-button variant="ghost" icon="printer"` |
| Badge rascunho | **Rascunho** | `es-badge variant="warn"`; tooltip ↓ |
| Tooltip rascunho | Só você vê. Publique quando estiver pronto. | `data-tooltip` |
| Indicador avulso | **avulso** | `es-badge variant="neutral"`; tooltip ↓ |
| Tooltip avulso | Item sem ligação com o estoque do sistema. | evita "ERP/SKU" |
| Coluna disponibilidade (ok) | **Disponível** | `es-badge variant="ok"` |
| Coluna disponibilidade (esgotado) | **Esgotado** | `es-badge variant="crit"` |
| Botão publicar (inline) | **Publicar** | 1 clique, sem modal; `aria-label="Publicar {nome}"` |
| Botão despublicar | **Voltar a rascunho** | quando `Visivel=true` |
| Botão esgotado | **Marcar esgotado** | quando `Disponivel=true` |
| Botão disponível | **Marcar disponível** | quando `Disponivel=false` |
| Ação editar | **Editar** | `aria-label="Editar {nome}"` |
| Ação remover | **Remover** | `aria-label="Remover {nome}"`; abre confirmação ↓ |
| Seletor de loja (2+) | **Loja:** | `<label>` do `<select>`; só aparece com 2+ storefronts; com 1, oculto |

### Empty state — cardápio vazio
| | Copy |
|---|---|
| Ícone | `utensils-crossed` / `book-open` |
| Título | **Seu cardápio ainda está vazio** |
| Descrição | Adicione o primeiro item — ele aparece na sua vitrine assim que você publicar. |
| CTA | **Adicionar primeiro item** |

---

## 2. Criar / Editar item

| Campo | Label | Helper | Placeholder |
|---|---|---|---|
| Modo | **Como você quer cadastrar?** | — | radio: "Item novo" (avulso) / "Usar produto do estoque" (vinculado) |
| Nome (avulso) | **Nome do item** * | Como aparece na sua vitrine. | Ex: Lasanha à bolonhesa (família) |
| Produto (vinculado) | **Produto do estoque** * | Puxa nome e preço do que você já tem cadastrado. | Busque pelo nome… |
| Preço | **Preço (R$)** * | Quanto o cliente paga por este item. | 0,00 |
| Categoria | **Categoria** | Agrupa os itens na vitrine (ex: Massas, Sobremesas). | Ex: Massas |
| Foto | **Foto (link)** | Cole o endereço de uma imagem. Sem foto, mostramos um espaço neutro. | https://… |
| Descrição | **Descrição** | Conte o que torna o item especial. | Massa fresca, molho da casa… |
| Contador descrição | **{n}/240** | vira `aria-live="polite"` quando passa de 200 | — |
| Publicar agora | **Publicar agora** | Desmarcado, fica como rascunho e só você vê. | checkbox, default OFF |
| Seção colapsável | **Detalhes para o cliente (opcional)** | Ingredientes, alergênicos e dicas de preparo. | `aria-expanded` |
| → Ingredientes | **Ingredientes** | — | Farinha, ovos, espinafre… |
| → Alergênicos | **Contém** | Avise sobre glúten, lactose, etc. | Glúten, leite |
| → Sugestão de molho | **Sugestão de molho** | — | Ao sugo, branco… |
| → Tempo de preparo | **Tempo de preparo** | — | 20 min no forno |
| → Peso/porção | **Peso ou porção** | — | 500 g · serve 2-3 |

### Botões do formulário
| Estado | Copy |
|---|---|
| Salvar (publicar ON) | **Publicar item** |
| Salvar (publicar OFF) | **Salvar rascunho** |
| Salvando | **Salvando…** (`aria-busy="true"`) |
| Cancelar | **Cancelar** |
| Editar — salvar | **Salvar alterações** |

---

## 3. Mensagens de erro (amigáveis)

| Origem | Copy | Como mostra |
|---|---|---|
| Nome duplicado (23505) | **Já existe um item com esse nome no seu cardápio.** Use outro nome ou edite o que já está lá. | toast `error` |
| Nome vazio em avulso (23514 / validação) | **Dê um nome ao item para continuar.** | inline no campo Nome, `role="alert"` |
| Preço ≤ 0 | **Informe um preço maior que zero.** | inline no campo Preço |
| Nome > 200 | **O nome ficou muito longo. Use até 200 caracteres.** | inline |
| Descrição > 240 | **A descrição passou de 240 caracteres.** | inline, sincroniza com contador |
| Sem empresa vinculada (guard p2) | **Sua conta ainda não está ligada a uma empresa.** Fale com o suporte para liberar a gestão do cardápio. | página de aviso / toast |
| Falha genérica | **Não foi possível salvar agora.** Tente de novo em instantes. | toast `error` |
| Sessão expirada | **Sua sessão expirou.** Entre de novo para continuar. | redirect login |

> Regra: nunca mostrar código de erro (`23505`, `42703`), nome de tabela/coluna ou stack. O backend
> traduz no controller (catch PostgresException → mensagem acima). Ver `AdminStorefrontCardapioController`.

---

## 4. Estados

| Estado | Copy |
|---|---|
| Sem vitrine (404 do tenant) — título | **Sua vitrine ainda não está pronta** |
| Sem vitrine — descrição | Assim que sua loja online for criada, seu cardápio aparece aqui. Fale com o suporte se precisar de ajuda. |
| Carregando lista | **Carregando seu cardápio…** (`es-skeleton` nas linhas) |
| Carregando produtos (dropdown) | **Buscando seus produtos…** |
| Erro ao carregar | **Não foi possível carregar o cardápio.** Toque para tentar de novo. + botão **Tentar de novo** |

---

## 5. Confirmações e toasts

### Confirmação de remoção
| | Copy |
|---|---|
| Título | **Remover este item?** |
| Corpo | "{nome}" sai da sua vitrine na hora. Você pode adicionar de novo depois. |
| Botão confirmar | **Remover** (`variant="danger"`) |
| Botão cancelar | **Manter** |

### Toasts
| Evento | Copy | Undo |
|---|---|---|
| Item adicionado (rascunho) | **Item adicionado.** Está como rascunho — publique quando quiser. | — |
| Item adicionado (publicado) | **Item publicado!** Já está na sua vitrine. | — |
| Publicado | **Publicado!** Já aparece para os clientes. | **Desfazer** |
| Voltou a rascunho | **Voltou para rascunho.** Só você vê agora. | **Desfazer** |
| Marcado esgotado | **Marcado como esgotado.** O cliente vê, mas não consegue pedir. | **Desfazer** |
| Marcado disponível | **Disponível de novo.** | **Desfazer** |
| Alterações salvas | **Alterações salvas.** | — |
| Item removido | **Item removido do cardápio.** | **Desfazer** |

---

## 6. Onboarding (primeiro item)

| Momento | Copy |
|---|---|
| Banner topo (cardápio vazio) | **Vamos montar seu cardápio?** Cada item que você publicar aparece na sua vitrine online na hora. |
| Dica no 1º "Publicar agora" | Deixe desmarcado para revisar com calma — nada vai ao ar sem você publicar. |
| Após publicar o 1º item | **Seu primeiro item está no ar! 🎉** Veja como ficou na vitrine. + link **Ver vitrine** |

---

## Alternativas (CTAs-chave)

| Elemento | A (escolhida) | B | C | Quando usar B/C |
|---|---|---|---|---|
| Adicionar | **Adicionar item** | Novo item | + Item | B/C em telas estreitas / botão-ícone |
| Publicar | **Publicar** | Pôr no ar | Mostrar na vitrine | C se "publicar" testar confuso com tenants |
| Empty CTA | **Adicionar primeiro item** | Começar cardápio | Criar item | B em tom mais caloroso |

## Rationale

- **"Vitrine" > "storefront/loja online"**: palavra concreta, calorosa, que a Thatiane usa naturalmente.
- **"Rascunho" + "Publicar"**: modelo mental familiar (Instagram/posts) — separa "só eu vejo" de "no ar".
- **Erros sempre com saída**: toda mensagem diz o próximo passo ("use outro nome", "fale com o suporte").
- **"avulso" com tooltip, não no rótulo principal**: o conceito técnico fica disponível mas não polui.
- **Esgotado ≠ removido**: o copy reforça que esgotado continua visível (decisão de domínio ADR-0031).

## Localization / a11y

- Sem travessão (—) em texto de UI corrido; usar dois-pontos ou frase curta (consistência EasyStok).
- Expansão PT-BR: rótulos ~20% mais longos que EN — botões usam `min-height` 44px, sem largura fixa.
- Não codificar significado só por cor: "Esgotado"/"Rascunho" sempre com texto, não só badge colorido.
- `aria-label` nos botões de ação inclui o nome do item ("Publicar Lasanha") para leitores de tela na tabela.
