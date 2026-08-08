# Transicao Menu Atual → Shell Modular

Documento de arquitetura. Baseado no codigo real do EasyStok (MenuDefinition.cs, EsSidebarTagHelper.cs, MenuViewModelBuilder.cs, _Layout.cshtml, _Topbar.cshtml, Dashboard/Index.cshtml, Auth/Login.cshtml).

> **Status (2026-08-08, #1007): IMPLEMENTADO, com divergencias.** O que valeu na
> implementacao esta no **ADR-0046**; este documento e o esboco que a antecedeu. As
> divergencias deliberadas:
>
> - **Modulo vem da ROTA, nao de querystring.** O `?modulo=` da §3.3 foi removido: nao
>   sobrevivia a redirect/formulario/paginacao, aceitava valor invalido em silencio e
>   produzia URL malformada nos cards.
> - **O Dashboard NAO foi movido para dentro de Crescimento** (§4 dizia que sim). Ele
>   continua fixo no topo, visivel em todos os modulos: e a ancora e o escape de 1 clique
>   para o menu inteiro.
> - **O rodape virou o modulo `admin`**, com os grupos escondidos.
> - **O `_Topbar.cshtml` NAO tinha breadcrumb** (§3.6 afirmava que sim); ele foi criado.
> - **Rail permanente: descartado** — contradiz o PATCH-1 do ADR-0032. Ver a nota em
>   `mapeamento-componentes-shell.md`.
> - **Multi-tenant (§6): parcialmente antecipado.** O login multi-empresa foi feito
>   (ADR-0047) porque destravava um bug real; o gating de modulos por tenant continua
>   fora, aguardando o epico da FMA.

---

## 1. O que o sistema ja tem (e que NAO mexe)

### Menu atual (MenuDefinition.cs — 25 itens, 5 grupos)

```
Dashboard (fixo no topo, fora de grupo)

├─ Operacao (accordion)
│  ├─ Pedidos          → /pedidos
│  ├─ KDS Operacao     → /kds
│  ├─ KDS Visor        → /pwa/#kds   (externo)
│  ├─ Caixa            → /caixa
│  ├─ Clientes         → /clientes
│  └─ Cardapio         → /cardapio
│
├─ Producao e estoque (accordion)
│  ├─ Validade         → /estoque?status=vencido
│  ├─ Posicao          → /estoque
│  ├─ Entradas         → /entradas/historico
│  ├─ Saidas           → /saidas/historico
│  ├─ Produtos         → /produtos
│  └─ Categorias       → /categorias
│
├─ Compras (accordion)
│  ├─ Pedidos de compra → /listas-compras
│  └─ Fornecedores     → /truck
│
├─ Financeiro (accordion)
│  ├─ Visao geral      → /financeiro
│  ├─ Contas a receber → /contas-a-receber
│  ├─ Contas a pagar   → /contas-a-pagar
│  └─ Notas fiscais    → /notas-fiscais
│
├─ Crescimento (accordion)
│  ├─ Analises         → /analytics
│  ├─ Relatorios       → /relatorios
│  └─ Anuncios         → /anuncios
│
Rodape
├─ Dispositivos        → /dispositivos
├─ Usuarios            → /usuarios
└─ Configuracoes       → /configuracoes
```

### Componentes que ja existem e funcionam

| Componente | Onde vive | Status |
|---|---|---|
| Login com preview ao vivo | Auth/Login.cshtml + auth-premium.css | Operacional |
| Dashboard com Pulso de hoje, alertas, KPIs, grafico, movimentacoes | Dashboard/Index.cshtml | Operacional |
| Busca universal Ctrl+K | _Topbar.cshtml (buscaUnificada) | Operacional |
| Menu lateral dinamico | EsSidebarTagHelper.cs + MenuViewModelBuilder.cs | Operacional |
| Favoritos "Meu dia" | MenuViewModelBuilder.cs + EsSidebarTagHelper.cs | Operacional |
| Badges de contagem | MenuBadges + EsSidebarTagHelper.cs | Operacional |
| Accordion de grupos | `<details>` nativo no sidebar | Operacional |
| Selecao de loja | Auth/SelecionarLoja.cshtml | Operacional |
| Tema dark/light | _Topbar.cshtml + tokens.css + localStorage | Operacional |
| Notificacoes dropdown | _Topbar.cshtml (notifDropdown) | Operacional |
| Acoes rapidas "Novo" | _Topbar.cshtml | Operacional |
| Bottom nav mobile | _BottomNav.cshtml | Operacional |
| Toast | _Toast.cshtml + toast.js | Operacional |
| Cheatsheet `?` | _Cheatsheet.cshtml | Operacional |

**Nada disso e recriado. Tudo e reusado.**

---

## 2. O que o shell modular muda

So a **arquitetura de navegacao**. Nenhuma rota muda. Nenhuma tela interna muda.

### Conceito: grupos viram modulos

Os 5 grupos accordion do menu atual viram 5 "modulos" no launcher:

| Modulo | Itens que contem | Badge principal |
|---|---|---|
| **Operacao** | Pedidos, KDS, Caixa, Clientes, Cardapio | Pedidos abertos |
| **Producao e Estoque** | Validade, Posicao, Entradas, Saidas, Produtos, Categorias | Lotes vencendo + Criticos |
| **Compras** | Pedidos de compra, Fornecedores | — |
| **Financeiro** | Visao geral, Contas a receber, Contas a pagar, Notas fiscais | Contas a vencer |
| **Crescimento** | Analises, Relatorios, Anuncios | — |

O rodape (Dispositivos, Usuarios, Configuracoes) vira o modulo **Administracao**.

### Fluxo de navegacao novo

```
[Login] → [Selecionar Loja] → [Launcher: escolhe modulo]
                                      ↓
                            [Shell: sidebar so desse modulo]
                                      ↓
                            [Clicou em item → tela interna como esta]
                                      ↓
                            ["← Modulos" volta pro launcher]
```

O launcher vira a "home" do sistema em vez do Dashboard. O Dashboard continua existindo como uma das telas internas (dentro do modulo Crescimento → Analises).

---

## 3. Como implementar sem quebrar nada

### 3.1. O launcher: view NOVA, zero impacto

```csharp
// novo: LauncherController.cs
public class LauncherController : BaseController
{
    public async Task<IActionResult> Index()
    {
        // Reutiliza os MESMOS servicos do Dashboard
        var badges = await _resumoSvc.ObterAsync(empresaId, lojaId);
        
        // Monta os cards de modulo com os dados reais
        ViewBag.Modulos = new[]
        {
            new ModuloCard("Operacao", "activity", badges.PedidosAbertos, "pedidos em aberto", "/pedidos"),
            new ModuloCard("Producao", "boxes", badges.LotesVencidos + badges.ProdutosCriticos, "alertas", "/estoque?status=vencido"),
            // ...
        };
        
        return View();
    }
}
```

A view `Launcher/Index.cshtml` usa os mesmos CSS (`tokens.css`, `app.css`, `components.css`), mesmas fontes, mesma estrutura de card do Dashboard.

### 3.2. O MenuViewModelBuilder: parametro opcional de modulo

```csharp
public static MenuViewModel Build(
    string? currentPath,
    string? activeMenuItem,
    IReadOnlyList<string>? favoritosKeys,
    MenuBadges? badges,
    bool kdsHabilitado,
    string? moduloAtivo = null)  // ← NOVO, opcional, default null
{
    // Se moduloAtivo == null: comportamento ATUAL (menu cheio)
    // Se moduloAtivo != null: filtra so o grupo desse modulo
    
    var groupsFiltrados = moduloAtivo is null 
        ? groups 
        : groups.Where(g => g.group.Key == moduloAtivo).ToList();
    
    // O resto do codigo nao muda
}
```

**Backward compatible**: todas as chamadas existentes continuam funcionando.

### 3.3. O EsSidebarTagHelper: repassa o modulo

```csharp
public sealed class EsSidebarTagHelper : TagHelper
{
    // NOVO: le do ViewBag ou querystring
    private string? ModuloAtivo => 
        ViewContext?.ViewData["ModuloAtivo"] as string
        ?? ViewContext?.HttpContext?.Request.Query["modulo"].FirstOrDefault();
    
    public override async Task ProcessAsync(...)
    {
        var vm = MenuViewModelBuilder.Build(
            path, activeMenuItem, favoritos, badges, kdsHabilitado,
            ModuloAtivo);  // ← repassa
        // ...
    }
}
```

### 3.4. O redirect pos-login: para o launcher em vez do Dashboard

```csharp
// AuthController.cs — unica mudanca
private IActionResult SafeRedirect(string? returnUrl)
{
    if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        return Redirect(returnUrl);
    
    // ANTES: ia pro Dashboard
    // DEPOIS: vai pro Launcher
    return RedirectToAction("Index", "Launcher");
}
```

Se o usuario tem deep link (`/pedidos/123`), o `returnUrl` preserva e ele cai direto no pedido, nao no launcher.

### 3.5. O "← Modulos" no sidebar

Adicionar no `_Sidebar.cshtml` (ou no EsSidebarTagHelper output) um botao condicional:

```html
@if (!string.IsNullOrEmpty(ViewBag.ModuloAtivo as string))
{
    <a href="/launcher" class="es-ni es-ni--voltar">
        <es-icon name="arrow-left" />
        <span>Modulos</span>
    </a>
}
```

So aparece quando o usuario esta dentro de um modulo.

### 3.6. O breadcrumb na topbar

O `_Topbar.cshtml` ja tem breadcrumb. Basta adicionar o nome do modulo:

```html
<!-- antes: Operacao / Pedidos -->
<!-- depois: Modulos / Operacao / Pedidos -->
<span class="topbar-breadcrumb">
    <a href="/launcher">Modulos</a>
    <span>/</span>
    <strong>Operacao</strong>
    <span>/</span>
    Pedidos
</span>
```

---

## 4. Fatias de implementacao

### Fatia 1: Launcher (2-3 dias)
- Criar `LauncherController` + `Index.cshtml`
- View com cards dos 5 modulos + Meu dia + resumo do dia
- Reutiliza CSS e componentes existentes
- Rota: `/launcher`
- **Nenhuma tela existente e alterada**

### Fatia 2: MenuViewModelBuilder com filtro de modulo (1-2 dias)
- Adicionar parametro `moduloAtivo` opcional
- Testes unitarios no `MenuViewModelBuilderTests`
- **Menu antigo continua funcionando identico**

### Fatia 3: EsSidebarTagHelper repassando modulo (1 dia)
- Ler `ModuloAtivo` do ViewBag/querystring
- Passar pro builder
- Adicionar botao "← Modulos" condicional

### Fatia 4: Redirect pos-login para launcher (1 dia)
- Mudar `SafeRedirect` no AuthController
- Se `returnUrl` presente, respeita (deep links funcionam)

### Fatia 5: Polimento e testes (2-3 dias)
- Mobile: launcher responsivo
- Dark mode no launcher
- Stagger de animacao no load
- Acessibilidade (tab order, aria-labels)

**Total estimado: 7-10 dias de trabalho focado.**

---

## 5. O que NAO muda (garantias)

| Item | Garantia |
|---|---|
| Todas as rotas `/pedidos`, `/estoque`, `/caixa`... | Continuam funcionando identico |
| ActiveMenuItem em todos controllers | Continua resolvendo |
| Favoritos "Meu dia" | Continuam salvos e funcionando |
| Badges no sidebar | Continuam contando |
| Selecao de loja | Mesmo fluxo |
| Login com preview ao vivo | Identico |
| Dashboard com grafico e KPIs | Identico, acessivel via Crescimento → Analises |
| Busca Ctrl+K | Identica |
| Notificacoes | Identicas |
| Acoes rapidas "Novo" | Identicas |
| Bottom nav mobile | Identico |
| Theme toggle | Identico |
| Cheatsheet `?` | Identico |

---

## 6. Multi-tenant: Casa da Babá ↔ FMA

Isso e **separado** do shell modular. A arquitetura do shell ja suporta:

```csharp
// No LauncherController
var modulos = empresaId switch
{
    "casadababa" => ModulosCasaDaBaba,  // Operacao, Estoque, Compras, Financeiro, Crescimento, Admin
    "fma" => ModulosFma,                 // Comercial, CRM, Financeiro, Admin
    _ => ModulosPadrao
};
```

A FMA teria modulos diferentes: Comercial, CRM, Financeiro (compartilhado), Administracao.

**A implementacao do multi-tenant vem DEPOIS do shell estar estavel.**

---

## 7. Resumo para decisao

O que voce ganha com essa transicao:
- **Menos ruido visual**: de 25 itens visiveis para 5-8 dentro de um modulo
- **Contexto preservado**: dentro do Financeiro, so ve coisas de financeiro
- **Launcher como cockpit**: resumo do dia antes de entrar em qualquer modulo
- **Zero quebra**: tudo existe, so a navegacao muda

O que voce nao ganha (ainda):
- Drawer lateral (e uma fatia futura, nao depende do shell)
- Multi-tenant FMA (e um epico separado)
- Reescrita de telas internas (nao e necessario)

**Recomendacao: fazer as fatias 1-5 (launcher + shell modular) como um unico PR de ~10 dias. Depois avaliar se o multi-tenant entra.**
