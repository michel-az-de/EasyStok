using Scriban;
using Scriban.Runtime;
using System.Reflection;

namespace EasyStock.Infra.Notifications.Templating;

/// <summary>
/// Cria um <see cref="TemplateContext"/> hardened para renderizar templates editados
/// por super-admin sem permitir RCE, ReDoS ou DoS por consumo de memoria/CPU.
/// </summary>
internal static class ScribanSandbox
{
    private const int LoopLimit = 500;
    private const int RecursiveLimit = 50;
    private const int ObjectRecursionLimit = 50;
    private const int LimitInterpolatedString = 100_000;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly HashSet<Type> TiposBloqueados =
    [
        typeof(Type),
        typeof(Assembly),
        typeof(MemberInfo),
        typeof(MethodBase),
        typeof(MethodInfo),
        typeof(ConstructorInfo),
        typeof(PropertyInfo),
        typeof(FieldInfo),
        typeof(EventInfo),
        typeof(Module),
        typeof(Delegate),
        typeof(IServiceProvider)
    ];

    internal static TemplateContext CriarContexto(
        IDictionary<string, object?> variaveis,
        CancellationToken cancellationToken)
    {
        var scriptObj = new ScriptObject();

        foreach (var kv in variaveis)
            scriptObj.SetValue(kv.Key, kv.Value ?? string.Empty, readOnly: true);

        var context = new TemplateContext
        {
            // Sem TemplateLoader -> include/import disparam ScriptRuntimeException
            // (testado: "Unable to include <X>. No TemplateLoader registered").
            TemplateLoader = null,

            // Limites contra DoS por loops/recursao/strings gigantes
            LoopLimit = LoopLimit,
            RecursiveLimit = RecursiveLimit,
            ObjectRecursionLimit = ObjectRecursionLimit,
            LimitToString = LimitInterpolatedString,
            RegexTimeOut = RegexTimeout,

            // Variaveis indefinidas viram string vazia em vez de exception
            StrictVariables = false,

            // Bloqueia acesso a membros .NET potencialmente perigosos via reflection
            // (Type, Assembly, MethodInfo, etc.) — defesa principal contra RCE.
            MemberFilter = AceitarApenasMembrosSeguros,

            // Hardening: nao tentar coercao implicita de targets/indexers/funcoes
            EnableRelaxedMemberAccess = false,
            EnableRelaxedTargetAccess = false,
            EnableRelaxedIndexerAccess = false,
            EnableRelaxedFunctionAccess = false,

            // CancellationToken e honrado por RenderAsync (testado: dispara
            // ScriptAbortException ao expirar).
            CancellationToken = cancellationToken
        };

        context.PushGlobal(scriptObj);
        return context;
    }

    private static bool AceitarApenasMembrosSeguros(MemberInfo member)
    {
        var memberType = member switch
        {
            PropertyInfo p => p.PropertyType,
            FieldInfo f => f.FieldType,
            MethodInfo m => m.ReturnType,
            _ => null
        };

        if (memberType is null)
            return true;

        if (TiposBloqueados.Any(b => b.IsAssignableFrom(memberType)))
            return false;

        return true;
    }
}
