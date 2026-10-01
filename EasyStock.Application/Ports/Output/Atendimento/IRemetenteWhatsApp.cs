namespace EasyStock.Application.Ports.Output.Atendimento;

/// <summary>
/// Diz por qual número da Cloud API da Meta a mensagem sai: o <c>phone_number_id</c> vinculado
/// à empresa do tenant corrente (#1102). O tenant vem do mesmo lugar que o resto da requisição
/// usa: JWT no console, <see cref="ITenantContextAccessor"/> no webhook e nos jobs.
/// </summary>
public interface IRemetenteWhatsApp
{
    /// <summary>
    /// <c>true</c> quando há empresa corrente. Com tenant e sem número vinculado o envio falha: o
    /// número global só serve a chamadas sem tenant (diagnóstico), nunca ao cliente de outra empresa.
    /// </summary>
    bool HaTenantCorrente { get; }


    /// <summary>
    /// <c>phone_number_id</c> da empresa corrente, ou <c>null</c> quando não há tenant ou a
    /// empresa ainda não tem número vinculado (o cliente decide o fallback).
    /// </summary>
    Task<string?> ObterPhoneNumberIdAsync(CancellationToken ct = default);
}
