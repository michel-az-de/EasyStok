using System.Globalization;
using EasyStock.Application.Ports.Output;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Async;

/// <summary>
/// Stub DEV-ONLY do gateway Pix: devolve um QR PNG de amostra + copia-e-cola fixo, sem chamar
/// a Efí. Registrado SOMENTE sob <c>Efi:UseStub=true</c> em Development (gate + hard-fail fora de
/// Development em <see cref="DependencyInjection.ServiceCollectionExtensions"/>). Permite ver e
/// demonstrar o fluxo de Pix QR sem credencial real. <b>NÃO reconcilia pagamentos</b> (o QR é
/// falso) — por isso é proibido fora de Development.
/// </summary>
public sealed class StubEfiPixService(ILogger<StubEfiPixService> logger) : IEfiPixService
{
    // QR PNG de amostra gerado offline (Python stdlib). Renderiza como imagem QR-like;
    // NÃO é escaneável para pagamento real — serve só para exercitar a UI.
    private const string SampleQrPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAH0AAAB9CAAAAACq6zP5AAAA80lEQVR42u3bQRLCIAwF0N7/0rrWgfhDtc6U" +
        "153DNK+LSAilx+Of10Gn0+lX60d4je5YDUCnv4y8p+dAqgemUej0wcgoR6fxp1ldR6HTQ71+GDr9Ar2u4HR" +
        "6R6/re5D4P1xd0G+op11I8IBf76Tot9bnOdqp5UGnQ99eT8NMsz+dbun0dpi0oQkmaDo9nFCD/0b8k767/qE" +
        "Uh1wN0+nlW+D+GjENQKe3u+Azd9Dpa2cPgoYmnn3p9Gx3ebrmbG3o0Omdswf91ppOP6un70iCfSA6/YTeOo9" +
        "Fp7fre9Aer/Yy9D319PzU4nYPne5LJTqdvon+BDaMXKxmrB+9AAAAAElFTkSuQmCC";

    public Task<EfiCobrancaResult> CriarCobrancaAsync(string txid, decimal valor, string descricao, CancellationToken ct = default)
    {
        logger.LogWarning(
            "StubEfiPixService (DEV): cobrança SIMULADA txid={Txid} valor={Valor}. QR falso, não reconcilia.",
            txid, valor.ToString("0.00", CultureInfo.InvariantCulture));

        var txidCurto = txid.Length >= 8 ? txid[..8] : txid;
        var copiaCola =
            "00020126430014br.gov.bcb.pix0121pix-demo-stub@easystok" +
            "5204000053039865406" + valor.ToString("0.00", CultureInfo.InvariantCulture) +
            "5802BR5913Casa da Baba6008SaoPaulo62290525txid-" + txidCurto + "6304STUB";

        return Task.FromResult(new EfiCobrancaResult(
            Txid: txid,
            PixCopiaCola: copiaCola,
            QrCodeBase64: SampleQrPngBase64,
            ExpiracaoEm: DateTime.UtcNow.AddHours(1)));
    }

    public Task<EfiCobrancaStatusResult> ConsultarCobrancaAsync(string txid, CancellationToken ct = default) =>
        Task.FromResult(new EfiCobrancaStatusResult(txid, EfiCobrancaStatus.Ativa));

    public Task<EfiEstornoResult> EstornarAsync(string e2eId, string idSolicitacao, decimal valor, CancellationToken ct = default) =>
        Task.FromResult(new EfiEstornoResult(true, Id: "stub-" + idSolicitacao, Status: "DEVOLVIDO", Mensagem: "Estorno simulado (DEV)."));
}
