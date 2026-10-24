using EasyStock.Application.Ports.Output.Lookup;
using EasyStock.Application.UseCases.Operacao.Impressao;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.Services.Pedidos;

public sealed record DadosEnderecoEntrega(string? Logradouro, string? Numero, string? Complemento,
    string? Bairro, string? Cidade, string? Uf, string? Cep)
{
    public static DadosEnderecoEntrega De(Cliente c) =>
        new(c.Endereco, null, c.Complemento ?? c.Apt, c.Bairro, c.Cidade, null, c.Cep);

    public static DadosEnderecoEntrega De(ClienteEndereco e) =>
        new(e.Logradouro, e.Numero, e.Complemento, e.Bairro, e.Cidade, e.Estado, e.Cep);
}

public sealed class EnderecoEntregaPedido(ICepLookupClient ceps, IGeocodingClient geocoding,
    TimeProvider relogio, ILogger<EnderecoEntregaPedido> logger)
{
    public async Task RegistrarAsync(Pedido pedido, DadosEnderecoEntrega dados, CancellationToken ct = default)
    {
        var cep = EnderecoBr.NormalizarCep(dados.Cep);
        var consulta = string.IsNullOrWhiteSpace(dados.Logradouro) && cep.Length == 8
            ? await ceps.LookupAsync(cep, ct) : null;
        var rua = Limpo(dados.Logradouro) ?? consulta?.Logradouro;
        var bairro = Limpo(dados.Bairro) ?? consulta?.Bairro;
        var cidade = Limpo(dados.Cidade) ?? consulta?.Cidade;
        var uf = Limpo(dados.Uf) ?? consulta?.Uf;
        var logradouro = string.Join(", ", new[] { rua, dados.Numero }.Where(s => !string.IsNullOrWhiteSpace(s)));
        // Null identifica legado; vazio preserva a ausência de endereço na criação.
        pedido.EnderecoEntrega = EnderecoImpresso.Montar(logradouro, dados.Complemento, null, bairro, cidade, dados.Cep, uf) ?? string.Empty;
        pedido.BairroEntrega = bairro;
        pedido.CepEntrega = cep.Length == 8 ? cep : null;
        pedido.EnderecoConfirmadoEm = string.IsNullOrWhiteSpace(rua) ? null : relogio.GetUtcNow().UtcDateTime;
        if (pedido.EnderecoEntrega.Length == 0) return;

        var geo = await geocoding.GeocodificarAsync(new GeocodeQuery(rua, dados.Numero,
            bairro, cidade, uf, pedido.CepEntrega), ct);
        pedido.EntregaLat = geo is { Confiavel: true } ? geo.Lat : null;
        pedido.EntregaLng = geo is { Confiavel: true } ? geo.Lng : null;
        if (pedido.EntregaLat is null)
            logger.LogWarning("Pedido {PedidoId} criado com endereço sem coordenadas de entrega", pedido.Id);
    }

    private static string? Limpo(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
}
