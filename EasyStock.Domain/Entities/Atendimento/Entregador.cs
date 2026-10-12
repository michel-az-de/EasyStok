using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>
/// Entregador (S44, ADR-0051): motoboy, plataforma ou próprio. Editar o cadastro não altera o
/// retrato já gravado nas paradas despachadas (<see cref="ParadaViagem"/>).
/// </summary>
public class Entregador
{
    public const int NomeTamanhoMaximo = 120;
    public const int TelefoneTamanhoMaximo = 20;
    public const int VeiculoTamanhoMaximo = 60;
    public const int PlacaTamanhoMaximo = 10;

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public string Nome { get; private set; } = null!;
    public TipoEntregador Tipo { get; private set; }
    public EmpresaEntregador Empresa { get; private set; }
    public string? Telefone { get; private set; }
    public string? Veiculo { get; private set; }
    public string? Placa { get; private set; }
    public bool Ativo { get; private set; }
    public DateTime CriadoEm { get; private set; }
    public DateTime AlteradoEm { get; private set; }

    // EF Core ctor sem parâmetros
    private Entregador() { }

    public static Entregador Criar(
        Guid empresaId, string nome, TipoEntregador tipo, EmpresaEntregador empresa,
        string? telefone, string? veiculo, string? placa, DateTime agora)
    {
        if (empresaId == Guid.Empty) throw new RegraDeDominioVioladaException("EmpresaId é obrigatório.");
        var entregador = new Entregador
        {
            Id = Guid.NewGuid(),
            EmpresaId = empresaId,
            Ativo = true,
            CriadoEm = Datas.Utc(agora),
        };
        entregador.Atualizar(nome, tipo, empresa, telefone, veiculo, placa, agora);
        return entregador;
    }

    public void Atualizar(
        string nome, TipoEntregador tipo, EmpresaEntregador empresa,
        string? telefone, string? veiculo, string? placa, DateTime agora)
    {
        if (string.IsNullOrWhiteSpace(nome)) throw new RegraDeDominioVioladaException("Nome do entregador é obrigatório.");
        if (!Enum.IsDefined(tipo)) throw new RegraDeDominioVioladaException($"Tipo de entregador inválido: {tipo}.");
        if (!Enum.IsDefined(empresa)) throw new RegraDeDominioVioladaException($"Empresa do entregador inválida: {empresa}.");

        Nome = Limitar(nome, NomeTamanhoMaximo, "Nome")!;
        Tipo = tipo;
        Empresa = empresa;
        Telefone = Limitar(telefone, TelefoneTamanhoMaximo, "Telefone");
        Veiculo = Limitar(veiculo, VeiculoTamanhoMaximo, "Veículo");
        Placa = Limitar(placa, PlacaTamanhoMaximo, "Placa")?.ToUpperInvariant();
        AlteradoEm = Datas.Utc(agora);
    }

    public void Desativar(DateTime agora) { Ativo = false; AlteradoEm = Datas.Utc(agora); }

    public void Reativar(DateTime agora) { Ativo = true; AlteradoEm = Datas.Utc(agora); }

    private static string? Limitar(string? valor, int maximo, string campo)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        var limpo = valor.Trim();
        if (limpo.Length > maximo) throw new RegraDeDominioVioladaException($"{campo} acima de {maximo} caracteres.");
        return limpo;
    }
}
