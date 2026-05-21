using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Domain.Fiscal;
using EasyStock.Domain.Integration;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Integrations.Fiscal.Mock;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Fiscal;

/// <summary>
/// Prova que a emissão SIMULADA de NFC-e (provedor "mock") funciona sem
/// certificado A1 — exatamente o caminho de teste enquanto não há certificado
/// real. A queixa "não emite nem simulando" em produção era config fiscal
/// ausente; estes testes garantem que, com config válida, o mock autoriza.
/// </summary>
public class MockGatewayFiscalTests
{
    private readonly IGeradorChaveAcesso _geradorChave = Substitute.For<IGeradorChaveAcesso>();

    private MockGatewayFiscal Sut() => new(_geradorChave, NullLogger<MockGatewayFiscal>.Instance);

    private static readonly string ChaveValida = new('1', 44);

    private static ConfigFiscalDto Config(Endereco? endereco) => new(
        EmpresaId: Guid.NewGuid(),
        Provedor: "mock",
        Ambiente: AmbienteIntegracao.Sandbox,
        RegimeTributario: RegimeTributario.LucroPresumido,
        Cnpj: "11444777000161",
        InscricaoEstadual: null,
        InscricaoMunicipal: null,
        Endereco: endereco,
        SerieNfce: 1,
        CredencialToken: null,
        CertificadoA1Bytes: null,   // <- sem certificado: é o ponto do mock
        CertificadoA1Senha: null,
        CscId: null,
        CscToken: null);

    private static NfeDocumento Nfe(string? chave = null) => new()
    {
        Serie = 1,
        Numero = 1,
        ChaveAcesso = chave,
        TotalNota = Dinheiro.FromDecimal(100m)
    };

    [Fact]
    public async Task EmitirAsync_DeveAutorizarSimulado_SemCertificado()
    {
        var resultado = await Sut().EmitirAsync(Nfe(ChaveValida), Config(new Endereco(Uf: "SP")));

        resultado.ChaveAcesso.Should().Be(ChaveValida);
        resultado.ProtocoloAutorizacao.Should().NotBeNullOrWhiteSpace();
        resultado.DanfeUrl.Should().Contain(ChaveValida);
        resultado.DataAutorizacao.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task EmitirAsync_DeveRejeitarComMensagemClara_QuandoUfDoEmitenteAusente()
    {
        // Config sem Endereço (logo sem UF) → rejeição de domínio fiscal, NUNCA 500.
        var act = async () => await Sut().EmitirAsync(Nfe(ChaveValida), Config(endereco: null));

        await act.Should().ThrowAsync<GatewayFiscalRejeitadaException>().WithMessage("*UF*");
    }

    [Fact]
    public async Task ConsultarStatusAsync_DeveRefletirAutorizado_AposEmitir()
    {
        var sut = Sut();
        var config = Config(new Endereco(Uf: "SP"));

        await sut.EmitirAsync(Nfe(ChaveValida), config);
        var consulta = await sut.ConsultarStatusAsync(ChaveValida, config);

        consulta.StatusGateway.Should().Be("autorizado");
        consulta.ProtocoloAutorizacao.Should().NotBeNullOrWhiteSpace();
    }
}
