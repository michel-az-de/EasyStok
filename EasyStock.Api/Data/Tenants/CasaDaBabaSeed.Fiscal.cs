using EasyStock.Domain.Entities;
using EasyStock.Domain.Fiscal;
using EasyStock.Domain.Integration;
using EasyStock.Domain.ValueObjects;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Data.Tenants;

internal static partial class CasaDaBabaSeed
{
    /// <summary>
    /// Garante que o tenant Casa da Baba demo tenha uma <see cref="EmpresaConfiguracaoFiscal"/>
    /// habilitada apontando para o provedor "mock" — isso permite ao usuario logar como
    /// admin@casadababa.demo e operar o fluxo de NFC-e (listar, emitir, cancelar)
    /// imediatamente, sem cadastrar credenciais reais nem certificado A1.
    ///
    /// <para>
    /// <b>Idempotente:</b> se ja existe configuracao fiscal para a empresa, nao recria.
    /// Apenas registra log. Atualizacoes manuais (via wizard ou API) sao preservadas.
    /// </para>
    ///
    /// <para>
    /// <b>Escopo dev:</b> chamado apenas pelo seed demo, que so roda em
    /// <c>IHostEnvironment.IsDevelopment()</c>. Producao nunca pega esta config.
    /// </para>
    /// </summary>
    internal static async Task EnsureFiscalAsync(
        EasyStockDbContext context,
        Empresa empresa,
        ILogger logger)
    {
        var existente = await context.EmpresaConfiguracoesFiscais
            .FirstOrDefaultAsync(c => c.EmpresaId == empresa.Id);

        if (existente is not null)
        {
            logger.LogInformation(
                "Casa da Baba fiscal: configuracao ja existe (provedor={Provedor}, habilitada={Habilitada}). Pulando seed.",
                existente.ProvedorPreferido, existente.Habilitada);
            return;
        }

        var config = EmpresaConfiguracaoFiscal.Criar(empresa.Id, RegimeTributario.Simples);

        config.AtualizarDadosEmitente(
            inscricaoEstadual: "ISENTO",
            inscricaoMunicipal: null,
            endereco: new Endereco(
                Logradouro: "Rua Conselheiro Crispiniano",
                Numero: "141",
                Complemento: null,
                Bairro: "Centro",
                Cidade: "Sao Paulo",
                Uf: "SP",
                Cep: "01037-001",
                Pais: "BR"));

        config.AlterarAmbiente(AmbienteIntegracao.Sandbox);
        config.EscolherProvedor("mock");
        config.AlterarSerieNfce(1);
        config.ConfigurarCsc("000001", "MOCK_CSC_TOKEN_NAO_USE_EM_PRODUCAO");
        config.Habilitar();

        context.EmpresaConfiguracoesFiscais.Add(config);
        await context.SaveChangesAsync();

        logger.LogInformation(
            "Casa da Baba fiscal: configuracao mock criada e habilitada. EmpresaId={EmpresaId} Serie={Serie} ProximoNumero={Numero}",
            empresa.Id, config.SerieNfce, config.ProximoNumeroNfce);
    }
}
