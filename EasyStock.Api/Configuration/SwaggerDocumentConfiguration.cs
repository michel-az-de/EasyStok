using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Reflection;

namespace EasyStock.Api.Configuration;

/// <summary>
/// Configures the two Swagger documents (English and Brazilian Portuguese).
/// </summary>
public static class SwaggerConfiguration
{
    // ── API metadata ─────────────────────────────────────────────────────────

    private static readonly OpenApiContact Contact = new()
    {
        Name  = "EasyStock Support",
        Email = "suporte@easystock.com.br",
        Url   = new Uri("https://easystock.com.br")
    };

    private static readonly OpenApiLicense License = new()
    {
        Name = "Proprietária / Proprietary",
        Url  = new Uri("https://easystock.com.br/terms")
    };

    // ── English document ─────────────────────────────────────────────────────

    public static OpenApiInfo InfoEnglish => new()
    {
        Title   = "EasyStock API",
        Version = "v1",
        Contact = Contact,
        License = License,
        Description = """
            ## EasyStock Inventory Management API

            A **professional, multi-tenant inventory management platform** built with .NET 9 and Clean Architecture.

            ### Key Features
            - 🔐 **JWT Bearer authentication** with role-based access control
            - 🏢 **Multi-tenant** — every resource is scoped to a `empresaId`
            - 📦 **Product catalogue** with variants, packaging and photo management
            - 📊 **Analytics & Intelligence** — projections, seasonality, replenishment alerts
            - 🤖 **AI-powered** product listing generation (Anthropic Claude)
            - 🔔 **Notifications** for low-stock and expiry alerts
            - 🗄️ **Database-agnostic** (PostgreSQL or MongoDB)

            ### Authentication
            1. `POST /api/auth/register` — create a user account
            2. `POST /api/empresas/registrar` — register your company
            3. `POST /api/auth/login` — obtain a JWT token
            4. Click **Authorize** and enter `Bearer <your-token>`

            ### Response Envelope
            All responses follow a consistent envelope:
            ```json
            {
              "data": { /* payload */ },
              "meta": { "total": 100, "pages": 5, "page": 1, "limit": 20 }
            }
            ```
            Errors use:
            ```json
            {
              "error": {
                "code": "NOT_FOUND",
                "message": "Resource not found.",
                "detail": "Optional detail",
                "correlationId": "uuid"
              }
            }
            ```

            ### Access Levels
            | Level      | Can access                          |
            |------------|-------------------------------------|
            | Operador   | Read-only + stock entries/exits     |
            | Gerente    | + Create/update products, suppliers |
            | Admin      | + Delete, user management           |
            | SuperAdmin | Full access                         |
            """
    };

    // ── Portuguese document ──────────────────────────────────────────────────

    public static OpenApiInfo InfoPortuguese => new()
    {
        Title   = "EasyStock API",
        Version = "v1",
        Contact = Contact,
        License = License,
        Description = """
            ## API de Gestão de Estoque EasyStock

            Plataforma profissional de **gestão de estoque multi-tenant** construída com .NET 9 e Clean Architecture.

            ### Principais Funcionalidades
            - 🔐 **Autenticação JWT Bearer** com controle de acesso por perfil
            - 🏢 **Multi-tenant** — todo recurso é vinculado a um `empresaId`
            - 📦 **Catálogo de produtos** com variações, embalagens e fotos
            - 📊 **Analytics e Inteligência** — projeções, sazonalidade, alertas de reposição
            - 🤖 **Geração de anúncios por IA** (Anthropic Claude)
            - 🔔 **Notificações** de estoque baixo e validade próxima
            - 🗄️ **Banco de dados agnóstico** (PostgreSQL ou MongoDB)

            ### Autenticação
            1. `POST /api/auth/register` — cadastre um usuário
            2. `POST /api/empresas/registrar` — registre sua empresa
            3. `POST /api/auth/login` — obtenha o token JWT
            4. Clique em **Authorize** e informe `Bearer <seu-token>`

            ### Envelope de Resposta
            Todas as respostas seguem um envelope padronizado:
            ```json
            {
              "data": { /* payload */ },
              "meta": { "total": 100, "pages": 5, "page": 1, "limit": 20 }
            }
            ```
            Erros utilizam:
            ```json
            {
              "error": {
                "code": "NOT_FOUND",
                "message": "Recurso não encontrado.",
                "detail": "Detalhe opcional",
                "correlationId": "uuid"
              }
            }
            ```

            ### Níveis de Acesso
            | Nível      | Permissões                                   |
            |------------|----------------------------------------------|
            | Operador   | Leitura + entradas/saídas de estoque         |
            | Gerente    | + Criar/editar produtos, fornecedores        |
            | Admin      | + Excluir, gerenciar usuários                |
            | SuperAdmin | Acesso completo                              |
            """
    };

    // ── Security definition ───────────────────────────────────────────────────

    public static OpenApiSecurityScheme BearerScheme => new()
    {
        Description = "JWT Bearer token. Enter: **Bearer** &lt;token&gt;",
        Name        = "Authorization",
        In          = ParameterLocation.Header,
        Type        = SecuritySchemeType.Http,
        Scheme      = "bearer",
        BearerFormat = "JWT"
    };

    public static OpenApiSecurityRequirement BearerRequirement => new()
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    };
}

// ── Schema Examples Filter ────────────────────────────────────────────────────

/// <summary>
/// Adds pre-populated examples and descriptions to well-known schemas.
/// </summary>
public sealed class SchemaExamplesFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        var name = context.Type?.Name;
        if (name is null) return;

        switch (name)
        {
            case "CadastrarProdutoCommand":
                AddProdutoExample(schema);
                break;
            case "CriarFornecedorCommand":
                AddFornecedorExample(schema);
                break;
            case "RegistrarEntradaEstoqueCommand":
                AddEntradaEstoqueExample(schema);
                break;
            case "LoginRequest":
                AddLoginExample(schema);
                break;
            case "CriarCategoriaCommand":
                AddCategoriaExample(schema);
                break;
            case "RegistrarEmpresaCommand":
                AddEmpresaExample(schema);
                break;
        }
    }

    private static void AddProdutoExample(OpenApiSchema schema)
    {
        schema.Example = new OpenApiObject
        {
            ["empresaId"]         = new OpenApiString("3fa85f64-5717-4562-b3fc-2c963f66afa6"),
            ["categoriaId"]       = new OpenApiString("3fa85f64-5717-4562-b3fc-2c963f66afa7"),
            ["nome"]              = new OpenApiString("Camiseta Polo Masculina"),
            ["descricaoBase"]     = new OpenApiString("Camiseta polo 100% algodão, disponível em várias cores."),
            ["marca"]             = new OpenApiString("Polo Brasil"),
            ["codigoBarras"]      = new OpenApiString("7891234567890"),
            ["tipo"]              = new OpenApiString("FISICO"),
            ["custoReferencia"]   = new OpenApiDouble(29.90),
            ["precoReferencia"]   = new OpenApiDouble(79.90),
            ["controlaValidade"]  = new OpenApiBoolean(false)
        };
    }

    private static void AddFornecedorExample(OpenApiSchema schema)
    {
        schema.Example = new OpenApiObject
        {
            ["empresaId"]             = new OpenApiString("3fa85f64-5717-4562-b3fc-2c963f66afa6"),
            ["nome"]                  = new OpenApiString("Distribuidora ABC Ltda"),
            ["documento"]             = new OpenApiString("12.345.678/0001-90"),
            ["email"]                 = new OpenApiString("comercial@abc.com.br"),
            ["telefone"]              = new OpenApiString("(11) 99999-9999"),
            ["contato"]               = new OpenApiString("João da Silva"),
            ["leadTimeEstimadoDias"]  = new OpenApiInteger(7)
        };
    }

    private static void AddEntradaEstoqueExample(OpenApiSchema schema)
    {
        schema.Example = new OpenApiObject
        {
            ["empresaId"]    = new OpenApiString("3fa85f64-5717-4562-b3fc-2c963f66afa6"),
            ["itemEstoqueId"] = new OpenApiString("3fa85f64-5717-4562-b3fc-2c963f66afa8"),
            ["quantidade"]   = new OpenApiInteger(50),
            ["custoUnitario"] = new OpenApiDouble(29.90),
            ["descricao"]    = new OpenApiString("Recebimento NF 12345")
        };
    }

    private static void AddLoginExample(OpenApiSchema schema)
    {
        schema.Example = new OpenApiObject
        {
            ["email"]     = new OpenApiString("usuario@empresa.com.br"),
            ["senha"]     = new OpenApiString("Senha@123"),
            ["empresaId"] = new OpenApiString("3fa85f64-5717-4562-b3fc-2c963f66afa6")
        };
    }

    private static void AddCategoriaExample(OpenApiSchema schema)
    {
        schema.Example = new OpenApiObject
        {
            ["empresaId"] = new OpenApiString("3fa85f64-5717-4562-b3fc-2c963f66afa6"),
            ["nome"]      = new OpenApiString("Vestuário"),
            ["descricao"] = new OpenApiString("Roupas e acessórios em geral")
        };
    }

    private static void AddEmpresaExample(OpenApiSchema schema)
    {
        schema.Example = new OpenApiObject
        {
            ["nome"]      = new OpenApiString("Minha Empresa ME"),
            ["documento"] = new OpenApiString("12.345.678/0001-90"),
            ["adminEmail"] = new OpenApiString("admin@minhaempresa.com.br"),
            ["adminNome"]  = new OpenApiString("Admin"),
            ["adminSenha"] = new OpenApiString("Senha@123")
        };
    }
}

// ── Operation Filter: adds examples to response bodies ───────────────────────

/// <summary>
/// Enriches GET operations with pre-populated mock response examples.
/// </summary>
public sealed class GetOperationExamplesFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = context.ApiDescription.RelativePath ?? "";

        // Add X-Correlation-Id header to ALL operations (GET, POST, PUT, PATCH, DELETE…)
        operation.Parameters ??= new List<OpenApiParameter>();
        if (!operation.Parameters.Any(p => p.Name == "X-Correlation-Id"))
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name        = "X-Correlation-Id",
                In          = ParameterLocation.Header,
                Required    = false,
                Description = "Optional correlation ID for distributed tracing.",
                Schema      = new OpenApiSchema { Type = "string", Format = "uuid" }
            });
        }

        // Inject mock examples only for GET operations
        if (context.ApiDescription.HttpMethod?.Equals("GET", StringComparison.OrdinalIgnoreCase) != true)
            return;

        // Inject mock examples per well-known paths
        if (!operation.Responses.ContainsKey("200")) return;
        var response = operation.Responses["200"];
        if (response.Content is null || !response.Content.ContainsKey("application/json")) return;

        var mediaType = response.Content["application/json"];
        mediaType.Examples ??= new Dictionary<string, OpenApiExample>();

        if (path.Contains("auth/me"))
        {
            mediaType.Examples["current-user"] = BuildExample("Current user", new OpenApiObject
            {
                ["data"] = new OpenApiObject
                {
                    ["id"]    = new OpenApiString("3fa85f64-5717-4562-b3fc-2c963f66afa6"),
                    ["nome"]  = new OpenApiString("João da Silva"),
                    ["email"] = new OpenApiString("joao@empresa.com.br"),
                    ["nivel"] = new OpenApiString("Gerente"),
                    ["ativo"] = new OpenApiBoolean(true)
                },
                ["meta"] = new OpenApiObject()
            });
        }
        else if (path.Contains("produtos") && !path.Contains("{"))
        {
            mediaType.Examples["products-list"] = BuildExample("Products list (paginated)", new OpenApiObject
            {
                ["data"] = new OpenApiArray
                {
                    new OpenApiObject
                    {
                        ["id"]   = new OpenApiString("11111111-1111-1111-1111-111111111111"),
                        ["nome"] = new OpenApiString("Camiseta Polo Masculina"),
                        ["marca"] = new OpenApiString("Polo Brasil"),
                        ["precoReferencia"] = new OpenApiDouble(79.90),
                        ["status"] = new OpenApiString("Ativo")
                    }
                },
                ["meta"] = new OpenApiObject
                {
                    ["total"]  = new OpenApiInteger(120),
                    ["pages"]  = new OpenApiInteger(6),
                    ["page"]   = new OpenApiInteger(1),
                    ["limit"]  = new OpenApiInteger(20)
                }
            });
        }
        else if (path.Contains("estoque") && !path.Contains("{"))
        {
            mediaType.Examples["stock-list"] = BuildExample("Stock items (paginated)", new OpenApiObject
            {
                ["data"] = new OpenApiArray
                {
                    new OpenApiObject
                    {
                        ["id"]               = new OpenApiString("22222222-2222-2222-2222-222222222222"),
                        ["produtoNome"]       = new OpenApiString("Camiseta Polo Masculina – Azul G"),
                        ["quantidadeAtual"]   = new OpenApiInteger(45),
                        ["quantidadeMinima"]  = new OpenApiInteger(10),
                        ["status"]            = new OpenApiString("Ok")
                    }
                },
                ["meta"] = new OpenApiObject
                {
                    ["total"] = new OpenApiInteger(80), ["pages"] = new OpenApiInteger(4),
                    ["page"]  = new OpenApiInteger(1),  ["limit"]  = new OpenApiInteger(20)
                }
            });
        }
        else if (path.Contains("analytics/dashboard"))
        {
            mediaType.Examples["dashboard"] = BuildExample("Dashboard summary", new OpenApiObject
            {
                ["data"] = new OpenApiObject
                {
                    ["totalProdutos"]    = new OpenApiInteger(150),
                    ["itensEstoque"]     = new OpenApiInteger(320),
                    ["itensEstoqueBaixo"]= new OpenApiInteger(12),
                    ["itensVencendo"]    = new OpenApiInteger(3),
                    ["receitaMes"]       = new OpenApiDouble(48250.75)
                },
                ["meta"] = new OpenApiObject()
            });
        }
        else if (path.Contains("notificacoes/badge"))
        {
            mediaType.Examples["badge"] = BuildExample("Unread badge count", new OpenApiObject
            {
                ["data"] = new OpenApiObject
                {
                    ["total"]   = new OpenApiInteger(5),
                    ["critica"] = new OpenApiInteger(1)
                },
                ["meta"] = new OpenApiObject()
            });
        }
    }

    private static OpenApiExample BuildExample(string summary, OpenApiObject value) =>
        new() { Summary = summary, Value = value };
}

// ── XML comments include filter ───────────────────────────────────────────────

/// <summary>
/// Registers XML comment files for the Swagger generator.
/// </summary>
public static class SwaggerXmlExtensions
{
    public static void IncludeXmlComments(SwaggerGenOptions options)
    {
        var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
            options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
    }
}

// ── Tag descriptions document filter ─────────────────────────────────────────

/// <summary>
/// Adds human-readable tag descriptions to the generated Swagger document
/// so the Swagger UI shows a description below each controller group.
/// </summary>
public sealed class TagDescriptionsDocumentFilter : IDocumentFilter
{
    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Auth"]           = "Autenticação e gerenciamento de sessão. Registro, login, refresh de token e perfil do usuário autenticado.",
        ["Empresas"]       = "Registro e configuração de empresas (tenants). Cada recurso da API é vinculado a um `empresaId`.",
        ["Usuarios"]       = "Gerenciamento de usuários: criação, atualização de perfil, desativação e listagem por empresa.",
        ["Produtos"]       = "Catálogo de produtos com suporte a variações, embalagens, fotos e geração de anúncios via IA.",
        ["ItemEstoque"]    = "Itens de estoque vinculados a produtos. Controle de quantidade atual, mínima e alertas de reposição.",
        ["Movimentacao"]   = "Entradas e saídas de estoque. Registro de compras, ajustes e saídas com rastreabilidade.",
        ["Fornecedor"]     = "Cadastro de fornecedores e configuração de lead time para cálculo de ponto de pedido.",
        ["Categoria"]      = "Categorias de produtos para organização do catálogo.",
        ["Analytics"]      = "Dashboard, projeções de demanda, análise de sazonalidade e relatórios de movimentação.",
        ["Inteligencia"]   = "Alertas inteligentes de estoque baixo, validade próxima e sugestões de reposição.",
        ["Notificacao"]    = "Gerenciamento de notificações do sistema: listagem, marcação como lida e contagem de não lidas.",
        ["Configuracoes"]  = "Configurações por empresa: limites de estoque, preferências de alerta e parametrizações gerais.",
        ["Loja"]           = "Configurações e dados da loja vinculada à empresa.",
        ["Plano"]          = "Gerenciamento de planos e assinaturas.",
        ["Uploads"]        = "Upload e gerenciamento de arquivos (fotos de produtos, documentos).",
        ["IaAnuncio"]      = "Geração de anúncios e descrições de produtos via Inteligência Artificial (Anthropic Claude).",
        ["Venda"]          = "Registro e consulta de vendas realizadas.",
        ["Diagnostico"]    = "Diagnóstico operacional da API: status de banco, Redis, SMTP, storage e configurações.",
    };

    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        swaggerDoc.Tags ??= new List<OpenApiTag>();

        foreach (var (name, description) in Descriptions)
        {
            if (!swaggerDoc.Tags.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                swaggerDoc.Tags.Add(new OpenApiTag { Name = name, Description = description });
            else
            {
                var existing = swaggerDoc.Tags.First(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(existing.Description))
                    existing.Description = description;
            }
        }
    }
}
