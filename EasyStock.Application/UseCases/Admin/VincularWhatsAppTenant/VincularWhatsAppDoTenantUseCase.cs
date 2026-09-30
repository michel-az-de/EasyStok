namespace EasyStock.Application.UseCases.Admin.VincularWhatsAppTenant;

/// <summary><paramref name="PhoneNumberId"/> <c>null</c> desvincula.</summary>
public sealed record VincularWhatsAppDoTenantCommand(Guid EmpresaId, string? PhoneNumberId);

public enum StatusVinculoWhatsApp { Vinculado, Desvinculado, EmpresaNaoEncontrada, NumeroEmUsoPorOutraEmpresa }

public sealed record VinculoWhatsAppResultado(StatusVinculoWhatsApp Status, string? PhoneNumberId);

/// <summary>
/// Vincula (ou desvincula) o <c>phone_number_id</c> da Cloud API da Meta à empresa pelo
/// back-office (#1102). O número decide duas coisas: para qual tenant o webhook roteia a mensagem
/// recebida e por qual número a resposta sai. Por isso o mesmo número nunca pode ter dois donos.
/// </summary>
public sealed class VincularWhatsAppDoTenantUseCase(IEmpresaRepository empresaRepository, IUnitOfWork unitOfWork)
{
    /// <summary>Mesmo limite da coluna (<c>EmpresaConfiguration</c>): recusar aqui dá mensagem melhor que erro do banco.</summary>
    public const int TamanhoMaximo = 32;

    public async Task<VinculoWhatsAppResultado> ExecuteAsync(
        VincularWhatsAppDoTenantCommand cmd, CancellationToken ct = default)
    {
        var numero = cmd.PhoneNumberId is null ? null : Validar(cmd.PhoneNumberId);

        var empresa = await empresaRepository.GetByIdAsync(cmd.EmpresaId);
        if (empresa is null)
            return new VinculoWhatsAppResultado(StatusVinculoWhatsApp.EmpresaNaoEncontrada, null);

        if (numero is null)
        {
            empresa.DesvincularWhatsApp();
            await PersistirAsync(empresa);
            return new VinculoWhatsAppResultado(StatusVinculoWhatsApp.Desvinculado, null);
        }

        // Pré-checagem para devolver 409 legível; a corrida entre dois PUTs simultâneos ainda
        // esbarra no índice único filtrado e vira 409 pelo handler global de 23505.
        var dono = await empresaRepository.GetByWhatsAppPhoneNumberIdAsync(numero, ct);
        if (dono is not null && dono.Id != empresa.Id)
            return new VinculoWhatsAppResultado(StatusVinculoWhatsApp.NumeroEmUsoPorOutraEmpresa, numero);

        empresa.VincularWhatsApp(numero);
        await PersistirAsync(empresa);
        return new VinculoWhatsAppResultado(StatusVinculoWhatsApp.Vinculado, numero);
    }

    private static string Validar(string bruto)
    {
        var numero = bruto.Trim();
        if (numero.Length == 0)
            throw new UseCaseValidationException("Informe o phone_number_id (ou envie null para desvincular).");
        if (numero.Length > TamanhoMaximo)
            throw new UseCaseValidationException($"phone_number_id muito longo (maximo {TamanhoMaximo} digitos).");
        if (!numero.All(char.IsAsciiDigit))
            throw new UseCaseValidationException("phone_number_id deve conter apenas digitos (o id numerico da Meta, nao o telefone formatado).");
        return numero;
    }

    private async Task PersistirAsync(Empresa empresa)
    {
        await empresaRepository.UpdateAsync(empresa);
        await unitOfWork.CommitAsync();
    }
}
