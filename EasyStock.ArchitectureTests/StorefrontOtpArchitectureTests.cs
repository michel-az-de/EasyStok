using EasyStock.Domain.Entities.Storefront;
using FluentAssertions;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace EasyStock.ArchitectureTests;

/// <summary>
/// Regras arquiteturais para a stack OTP/Session do storefront (ADR-0012).
///
/// Estas regras blindam:
/// - **Privacidade**: <see cref="ClienteOtp"/> NÃO pode expor propriedades
///   públicas que armazenem telefone ou código em plaintext (apenas hashes).
/// - **Determinismo temporal**: <see cref="ClienteOtp"/> e <see cref="ClienteSession"/>
///   devem usar <see cref="TimeProvider"/> injetado, NUNCA chamar
///   <c>DateTime.UtcNow</c> ou <c>DateTimeOffset.UtcNow</c> diretamente —
///   testes precisam ser determinísticos (avançar tempo via FakeTime).
/// </summary>
public class StorefrontOtpArchitectureTests
{
    // ── Privacidade: sem plaintext em propriedades públicas ────────────

    [Fact]
    public void ClienteOtp_NAO_expoe_propriedade_publica_Telefone_em_plaintext()
    {
        var t = typeof(ClienteOtp);

        // Permitido: TelefoneHash. Proibido: Telefone (plaintext).
        var props = t.GetProperties().Select(p => p.Name).ToList();
        props.Should().NotContain("Telefone",
            "ClienteOtp NUNCA armazena telefone plaintext — só TelefoneHash (SHA-256). LGPD + segurança.");
        props.Should().Contain("TelefoneHash",
            "ClienteOtp deve expor TelefoneHash para queries de lookup (SHA-256 determinístico).");
    }

    [Fact]
    public void ClienteOtp_NAO_expoe_propriedade_publica_Codigo_em_plaintext()
    {
        var t = typeof(ClienteOtp);

        var props = t.GetProperties().Select(p => p.Name).ToList();
        props.Should().NotContain("Codigo",
            "ClienteOtp NUNCA armazena código plaintext — só CodigoHash (BCrypt). Anti-rainbow-table.");
        props.Should().Contain("CodigoHash",
            "ClienteOtp deve expor CodigoHash para validação via BCrypt.Verify.");
    }

    // ── Determinismo temporal: TimeProvider, nunca DateTime.UtcNow ─────

    [Fact]
    public void ClienteOtp_NAO_usa_DateTime_UtcNow_direto_deve_usar_TimeProvider()
    {
        AssertNaoUsaUtcNowDireto(typeof(ClienteOtp));
    }

    [Fact]
    public void ClienteSession_NAO_usa_DateTime_UtcNow_direto_deve_usar_TimeProvider()
    {
        AssertNaoUsaUtcNowDireto(typeof(ClienteSession));
    }

    /// <summary>
    /// Inspeciona o IL do tipo (via Mono.Cecil) e falha se encontrar chamadas
    /// a <c>System.DateTime::get_UtcNow</c> ou <c>System.DateTimeOffset::get_UtcNow</c>
    /// em qualquer método.
    ///
    /// Permitidos: <c>TimeProvider::GetUtcNow</c>, <c>TimeProvider::GetLocalNow</c>.
    /// </summary>
    private static void AssertNaoUsaUtcNowDireto(Type tipo)
    {
        var assemblyPath = tipo.Assembly.Location;
        using var module = ModuleDefinition.ReadModule(assemblyPath);

        var typeDef = module.GetType(tipo.FullName);
        typeDef.Should().NotBeNull($"tipo {tipo.FullName} deve existir no assembly {assemblyPath}");

        var infracoes = new List<string>();
        foreach (var metodo in typeDef!.Methods.Where(m => m.HasBody))
        {
            foreach (var instr in metodo.Body.Instructions)
            {
                if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt)
                    continue;

                if (instr.Operand is not MethodReference mr)
                    continue;

                var declaring = mr.DeclaringType.FullName;
                var nome = mr.Name;

                var isUtcNow =
                    (declaring == "System.DateTime" || declaring == "System.DateTimeOffset")
                    && nome == "get_UtcNow";

                if (isUtcNow)
                {
                    infracoes.Add($"{tipo.Name}.{metodo.Name} chama {declaring}.{nome}");
                }
            }
        }

        infracoes.Should().BeEmpty(
            "ADR-0012: ClienteOtp/ClienteSession devem usar TimeProvider injetado (testes determinísticos). " +
            $"Infrações:\n  - {string.Join("\n  - ", infracoes)}");
    }
}
