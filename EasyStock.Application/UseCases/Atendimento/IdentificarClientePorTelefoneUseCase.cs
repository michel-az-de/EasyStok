using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;

namespace EasyStock.Application.UseCases.Atendimento;

/// <param name="WaId">Dígitos E.164 sem <c>+</c>, como a Meta entrega em <c>contacts[].wa_id</c>.</param>
/// <param name="NomePerfil">Nome do perfil do WhatsApp; vira o nome do cadastro quando o contato é novo.</param>
public sealed record IdentificarClientePorTelefoneInput(Guid EmpresaId, string WaId, string? NomePerfil);

/// <param name="EhLead">Ainda não comprou (<c>OrderCount == 0</c>), RN-03.</param>
/// <param name="EhNovo">O cadastro foi criado agora, nesta identificação.</param>
public sealed record IdentificacaoCliente(ClienteEntity Cliente, bool EhLead, bool EhNovo);

/// <summary>
/// RN-02 e RN-03 (S05): acha o <see cref="ClienteEntity"/> do contato pelo telefone ou cria o lead.
/// Procura, nesta ordem, pelo <c>TelefoneHash</c> (preenchido pelo OTP do storefront) e pelo
/// telefone do cadastro em E.164, com o DDI sem <c>+</c> e em dígitos nacionais; achado pelo telefone,
/// ganha o <c>TelefoneHash</c> que faltar. Não faz commit: participa da unidade
/// de trabalho do chamador, para o cliente novo e a conversa que o referencia nascerem juntos.
/// </summary>
public sealed class IdentificarClientePorTelefoneUseCase(
    IClienteRepository clienteRepository,
    IClienteStorefrontRepository clienteStorefrontRepository,
    ILogger<IdentificarClientePorTelefoneUseCase> logger)
{
    /// <summary>Nome do lead quando o perfil do WhatsApp não traz nome.</summary>
    public const string NomePadraoLead = "Cliente WhatsApp";

    private const int NomeTamanhoMaximo = 150;

    public async Task<IdentificacaoCliente> ExecuteAsync(IdentificarClientePorTelefoneInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var candidatos = NormalizadorTelefone.CandidatosDeWaId(input.WaId);

        var existente = await ProcurarAsync(input.EmpresaId, candidatos, ct);
        if (existente is not null)
            return new IdentificacaoCliente(existente, EhLead: existente.OrderCount == 0, EhNovo: false);

        var novo = CriarLead(input.EmpresaId, candidatos[0], input.NomePerfil);
        await clienteRepository.AddAsync(novo);

        logger.LogInformation(
            "Atendimento: lead criado pelo WhatsApp. empresaId={EmpresaId} clienteId={ClienteId}",
            input.EmpresaId, novo.Id);

        return new IdentificacaoCliente(novo, EhLead: true, EhNovo: true);
    }

    private async Task<ClienteEntity?> ProcurarAsync(Guid empresaId, IReadOnlyList<string> candidatos, CancellationToken ct)
    {
        foreach (var e164 in candidatos)
        {
            var porHash = await clienteStorefrontRepository.GetByTelefoneHashAsync(
                empresaId, ClienteOtp.CalcularTelefoneHash(e164), ct);
            if (porHash is not null)
                return porHash;
        }

        foreach (var e164 in candidatos)
        {
            foreach (var grafia in GrafiasDoCadastro(e164))
            {
                var porTelefone = await clienteRepository.FindByTelefoneAsync(empresaId, grafia);
                if (porTelefone is null) continue;

                // #1290: cadastro do ERP não tem TelefoneHash; sem ele o OTP e o checkout guest do site
                // (que procuram por hash) criam outro cliente. O hash do canônico está livre: nenhum
                // candidato achou cliente por hash logo acima.
                porTelefone.TelefoneHash ??= ClienteOtp.CalcularTelefoneHash(candidatos[0]);
                return porTelefone;
            }
        }

        return null;
    }

    /// <summary>
    /// Como o ERP pode ter gravado o número: E.164 (<c>+5511997573992</c>), com o DDI sem <c>+</c>
    /// (<c>5511997573992</c>, que é como o VO <c>Telefone</c> guarda o digitado) e nacional
    /// (<c>11997573992</c>, a forma sem máscara mais comum).
    /// </summary>
    private static IEnumerable<string> GrafiasDoCadastro(string e164)
    {
        yield return e164;
        if (!e164.StartsWith("+55", StringComparison.Ordinal)) yield break;
        yield return e164[1..];
        yield return e164[3..];
    }

    private static ClienteEntity CriarLead(Guid empresaId, string telefoneE164, string? nomePerfil)
    {
        var nome = string.IsNullOrWhiteSpace(nomePerfil) ? NomePadraoLead : nomePerfil.Trim();
        if (nome.Length > NomeTamanhoMaximo)
            nome = nome[..NomeTamanhoMaximo];

        var cliente = ClienteEntity.Criar(empresaId, nome);
        cliente.Telefone = telefoneE164;
        // Mesmo hash do OTP: quando o lead entrar no storefront, o login cai neste cadastro.
        cliente.TelefoneHash = ClienteOtp.CalcularTelefoneHash(telefoneE164);
        cliente.Telefones.Add(new ClienteTelefone
        {
            Id = Guid.NewGuid(),
            ClienteId = cliente.Id,
            Tipo = "celular",
            Numero = telefoneE164,
            Whatsapp = true,
            Principal = true,
            CriadoEm = cliente.CriadoEm,
            AlteradoEm = cliente.CriadoEm
        });
        return cliente;
    }
}
