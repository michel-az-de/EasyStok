using Scriban;
using Scriban.Runtime;
using System.Reflection;

namespace EasyStock.Infra.Notifications.Templating;

/// <summary>
/// Configura o contexto Scriban com restrições de segurança para evitar RCE
/// quando super-admins editam templates HTML livremente.
/// </summary>
internal static class ScribanSandbox
{
    private const int LoopLimit = 500;
    private const int RecursiveLimit = 50;

    internal static TemplateContext CriarContexto(IDictionary<string, object?> variaveis)
    {
        var scriptObj = new ScriptObject();

        foreach (var kv in variaveis)
            scriptObj.SetValue(kv.Key, kv.Value ?? string.Empty, readOnly: true);

        var context = new TemplateContext
        {
            // Impede acesso a arquivos e imports externos
            TemplateLoader = null,

            // Limites de segurança contra loops infinitos e recursão profunda
            LoopLimit = LoopLimit,
            RecursiveLimit = RecursiveLimit,

            // Variáveis indefinidas retornam string vazia em vez de exceção
            StrictVariables = false,

            // Bloqueia acesso a membros de objetos .NET arbitrários —
            // retornar string vazia impede que templates acessem reflect/IO
            MemberRenamer = BlockMemberAccess,

            // Desativa acessos relaxados a métodos, indexers e targets
            EnableRelaxedMemberAccess = false
        };

        context.PushGlobal(scriptObj);

        RemoverFuncoesPerigosas(context);

        return context;
    }

    private static string BlockMemberAccess(MemberInfo member) => string.Empty;

    private static void RemoverFuncoesPerigosas(TemplateContext context)
    {
        var builtins = context.BuiltinObject;
        foreach (var nome in new[] { "include", "import" })
        {
            if (builtins.Contains(nome))
                builtins.Remove(nome);
        }
    }
}
