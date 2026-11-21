using EasyStock.Domain.Enums.Atendimento;

namespace EasyStock.Domain.Entities.Atendimento;

/// <summary>
/// Corrida feita por terceiro (99, Lalamove, Uber) para uma viagem (I9.1, ADR-0061 D5). É o lugar único
/// do custo de corrida: <see cref="Viagem"/>, <see cref="ParadaViagem"/> e <see cref="Entregador"/> não
/// guardam custo. Uma viagem pode ter mais de uma (corrida refeita). O instante vem por parâmetro (UTC).
/// </summary>
public class CorridaExterna
{
    public const int MotoristaTamanhoMaximo = 120;

    /// <summary>R$ 5.000,00: acima disso é erro de digitação, não corrida.</summary>
    public const int CustoMaximoCentavos = 500_000;

    public Guid Id { get; private set; }
    public Guid EmpresaId { get; private set; }
    public Guid ViagemId { get; private set; }
    public EmpresaEntregador Provedor { get; private set; }
    public OrigemCorridaExterna Origem { get; private set; }
    public int CustoCentavos { get; private set; }
    public string? Motorista { get; private set; }
    public Guid RegistradoPorUsuarioId { get; private set; }
    public DateTime RegistradoEm { get; private set; }
    public Guid? CorrigidoPorUsuarioId { get; private set; }
    public DateTime? CorrigidoEm { get; private set; }

    // Corrida chamada pela API (#2158, ADR-0064). Nulos na corrida manual.
    public const int IdExternoTamanhoMaximo = 64;
    public const int LinkRastreioTamanhoMaximo = 500;
    public const int TelefoneTamanhoMaximo = 30;
    public const int PlacaTamanhoMaximo = 20;

    /// <summary>A Lalamove só cancela sem custo até 5 minutos depois de achar o motorista.</summary>
    public static readonly TimeSpan JanelaCancelamentoAposMatch = TimeSpan.FromMinutes(5);

    /// <summary>
    /// #2168: o POST de criação tem 15 s de timeout. Sem confirmação depois disto, o pedido pode existir na Lalamove
    /// sem o EasyStok saber o número: a corrida segue travando a viagem até a atendente conferir no painel.
    /// </summary>
    public static readonly TimeSpan PrazoConfirmacao = TimeSpan.FromMinutes(2);

    public StatusCorrida? Status { get; private set; }
    public VeiculoCorrida? Veiculo { get; private set; }
    public string? CotacaoId { get; private set; }
    public int? CustoCotadoCentavos { get; private set; }
    public string? IdExterno { get; private set; }
    public string? IdExternoAnterior { get; private set; }
    public string? LinkRastreio { get; private set; }
    public string? MotoristaIdExterno { get; private set; }
    public string? MotoristaTelefone { get; private set; }
    public string? MotoristaPlaca { get; private set; }
    public DateTime? MotoristaAtribuidoEm { get; private set; }
    public DateTime? EncerradaEm { get; private set; }

    /// <summary>Corrida da API ainda em curso: é a que o índice "uma ativa por viagem" protege.</summary>
    public bool Ativa => Status is { } s && !Finais.Contains(s);

    /// <summary>
    /// Gorjeta (priority fee) já no preço: o preço do provedor inclui a gorjeta, e na Lalamove a gorjeta nova
    /// substitui a anterior. Zero na corrida manual e na que encerrou sem custo.
    /// </summary>
    public int GorjetaCentavos => Origem == OrigemCorridaExterna.Api && CustoCotadoCentavos is { } cotado && CustoCentavos > cotado
        ? CustoCentavos - cotado : 0;

    /// <summary>Até quando a Lalamove cancela sem custo depois do match. Nulo procurando motorista (sem prazo) e depois.</summary>
    public DateTime? CancelaSemCustoAte => Status == StatusCorrida.MotoristaACaminho && MotoristaAtribuidoEm is { } em
        ? em + JanelaCancelamentoAposMatch : null;

    /// <summary>Pedida há mais que <see cref="PrazoConfirmacao"/> sem o número do pedido: a atendente confere no painel.</summary>
    public bool AguardaConferencia(DateTime agora) =>
        Status == StatusCorrida.Solicitando && Datas.Utc(agora) - RegistradoEm > PrazoConfirmacao;

    // Depois da coleta a moto está carregada: a resposta atrasada de antes dela não volta a corrida.
    private static int Etapa(StatusCorrida status) => status switch
    {
        StatusCorrida.ProcurandoMotorista => 1,
        StatusCorrida.MotoristaACaminho => 2,
        StatusCorrida.Coletada => 3,
        _ => 0,
    };

    private static readonly HashSet<StatusCorrida> Finais =
        [StatusCorrida.Entregue, StatusCorrida.Cancelada, StatusCorrida.Rejeitada, StatusCorrida.Expirada, StatusCorrida.Falha];

    /// <summary>Provedores com despacho pela API. A 99 fica manual até a API ser confirmada (ADR-0061 D5).</summary>
    private static readonly HashSet<EmpresaEntregador> ProvedoresComApi = [EmpresaEntregador.Lalamove];

    // EF Core ctor sem parâmetros
    private CorridaExterna() { }

    /// <summary>
    /// Gravada ANTES do POST ao provedor (R4): o <see cref="Id"/> vai no <c>metadata</c> do pedido e, num
    /// timeout, a reconsulta acha a corrida sem criar outra. A viagem precisa estar montando e com parada.
    /// </summary>
    public static CorridaExterna SolicitarPorApi(
        Viagem viagem, EmpresaEntregador provedor, VeiculoCorrida veiculo, string cotacaoId, int custoCotadoCentavos,
        Guid usuarioId, DateTime agora)
    {
        ArgumentNullException.ThrowIfNull(viagem);
        if (!ProvedoresComApi.Contains(provedor))
            throw new RegraDeDominioVioladaException("Este provedor não tem chamada pela API: registre a corrida à mão.");
        if (viagem.Situacao != SituacaoViagem.Montando)
            throw new RegraDeDominioVioladaException("Só viagem montando pode chamar corrida.");
        if (viagem.Paradas.Count == 0) throw new RegraDeDominioVioladaException("Viagem sem parada não chama corrida.");
        // R7: uma parada por corrida. Com mais, a coleta e a entrega marcariam todos os pedidos de uma vez.
        if (viagem.Paradas.Count != 1)
            throw new RegraDeDominioVioladaException("A Lalamove leva um pedido por vez: deixe uma parada só nesta viagem.");
        if (!Enum.IsDefined(veiculo)) throw new RegraDeDominioVioladaException("Veículo da corrida inválido.");
        if (string.IsNullOrWhiteSpace(cotacaoId)) throw new RegraDeDominioVioladaException("Cotação é obrigatória.");
        if (usuarioId == Guid.Empty) throw new RegraDeDominioVioladaException("Quem chamou a corrida é obrigatório.");

        var custo = CustoValido(custoCotadoCentavos);
        return new CorridaExterna
        {
            Id = Guid.NewGuid(),
            EmpresaId = viagem.EmpresaId,
            ViagemId = viagem.Id,
            Provedor = provedor,
            Origem = OrigemCorridaExterna.Api,
            Status = StatusCorrida.Solicitando,
            Veiculo = veiculo,
            CotacaoId = cotacaoId.Trim(),
            CustoCotadoCentavos = custo,
            CustoCentavos = custo,
            RegistradoPorUsuarioId = usuarioId,
            RegistradoEm = Datas.Utc(agora),
        };
    }

    /// <summary>O provedor aceitou o pedido: grava o id dele e o link de rastreio para o cliente.</summary>
    public void ConfirmarNoProvedor(string idExterno, string? linkRastreio)
    {
        GarantirApi();
        if (Status != StatusCorrida.Solicitando) throw new RegraDeDominioVioladaException("A corrida já foi confirmada pelo provedor.");
        IdExterno = Texto(idExterno, IdExternoTamanhoMaximo, "Id do pedido no provedor")
            ?? throw new RegraDeDominioVioladaException("Id do pedido no provedor é obrigatório.");
        LinkRastreio = Texto(linkRastreio, LinkRastreioTamanhoMaximo, "Link de rastreio");
        Status = StatusCorrida.ProcurandoMotorista;
    }

    /// <summary>O provedor recusou o pedido ou ele não existe lá: nada foi cobrado.</summary>
    public void FalharSolicitacao(DateTime agora)
    {
        GarantirApi();
        if (Status != StatusCorrida.Solicitando) throw new RegraDeDominioVioladaException("Só a corrida ainda não confirmada falha na solicitação.");
        Status = StatusCorrida.Falha;
        CustoCentavos = 0;
        EncerradaEm = Datas.Utc(agora);
    }

    /// <summary>
    /// A atendente conferiu no painel da Lalamove que o pedido não foi criado e libera a viagem. Só depois do
    /// <see cref="PrazoConfirmacao"/>: antes disso a resposta do POST ainda pode chegar.
    /// </summary>
    public void DescartarSemPedidoNoProvedor(DateTime agora)
    {
        GarantirApi();
        if (!AguardaConferencia(agora))
            throw new RegraDeDominioVioladaException(Status == StatusCorrida.Solicitando
                ? "A Lalamove ainda pode confirmar: aguarde 2 minutos antes de liberar a viagem."
                : "Só a corrida sem confirmação da Lalamove é liberada assim.");
        FalharSolicitacao(agora);
    }

    /// <summary>
    /// Aplica o status lido do provedor, que é a fonte da verdade (o webhook chega fora de ordem e por isso
    /// sempre se reconsulta). Pular etapa é aceito; estado final não reabre. Devolve se mudou.
    /// </summary>
    public bool AplicarStatus(StatusCorrida novo, DateTime agora)
    {
        GarantirApi();
        if (!Enum.IsDefined(novo) || novo == StatusCorrida.Solicitando)
            throw new RegraDeDominioVioladaException("Status de corrida inválido.");
        if (Status == StatusCorrida.Solicitando)
            throw new RegraDeDominioVioladaException("A corrida ainda não foi confirmada pelo provedor.");
        if (Status == novo) return false;
        if (!Ativa) throw new RegraDeDominioVioladaException("A corrida já foi encerrada.");
        if (Status == StatusCorrida.Coletada && Etapa(novo) is > 0 and < 3) return false;

        if (novo == StatusCorrida.MotoristaACaminho) MotoristaAtribuidoEm ??= Datas.Utc(agora);
        if (novo == StatusCorrida.ProcurandoMotorista) LimparMotorista();
        Status = novo;
        if (Finais.Contains(novo)) EncerradaEm = Datas.Utc(agora);
        return true;
    }

    public void AtribuirMotorista(string? idExterno, string? nome, string? telefone, string? placa)
    {
        GarantirApi();
        // #2168: dado do provedor acima do limite é cortado. Recusar travaria a reconsulta da corrida inteira.
        MotoristaIdExterno = Texto(idExterno, IdExternoTamanhoMaximo, "Id do motorista");
        Motorista = Cortado(nome, MotoristaTamanhoMaximo);
        MotoristaTelefone = Cortado(telefone, TelefoneTamanhoMaximo);
        // A placa vai para a parada na coleta, que guarda o tamanho do cadastro de entregador.
        MotoristaPlaca = Cortado(placa, Entregador.PlacaTamanhoMaximo);
    }

    /// <summary>Cancelamento sem custo: procurando motorista, ou até 5 minutos depois do match.</summary>
    public bool PodeCancelar(DateTime agora) => Status switch
    {
        StatusCorrida.ProcurandoMotorista => true,
        StatusCorrida.MotoristaACaminho => MotoristaAtribuidoEm is { } em && Datas.Utc(agora) - em <= JanelaCancelamentoAposMatch,
        _ => false,
    };

    /// <summary>
    /// ORDER_REPLACED: o suporte da Lalamove cancelou o pedido e abriu outro no lugar. A corrida continua a
    /// mesma, agora com o id novo, e volta a procurar motorista, mesmo se o cancelamento já tinha chegado.
    /// </summary>
    public void SubstituirPedidoNoProvedor(string idAnterior, string idNovo, DateTime agora)
    {
        GarantirApi();
        if (IdExterno is null || IdExterno != idAnterior)
            throw new RegraDeDominioVioladaException("A troca de pedido é de outra corrida.");
        IdExterno = Texto(idNovo, IdExternoTamanhoMaximo, "Id do pedido no provedor")
            ?? throw new RegraDeDominioVioladaException("Id do pedido no provedor é obrigatório.");
        IdExternoAnterior = idAnterior;
        LimparMotorista();
        Status = StatusCorrida.ProcurandoMotorista;
        EncerradaEm = null;
    }

    /// <summary>Preço final do provedor (com gorjeta ou ajuste): substitui o cotado no custo da corrida.</summary>
    public void AtualizarCustoDoProvedor(int custoCentavos)
    {
        GarantirApi();
        CustoCentavos = CustoValido(custoCentavos);
    }

    private void LimparMotorista()
    {
        MotoristaAtribuidoEm = null;
        MotoristaIdExterno = null;
        Motorista = null;
        MotoristaTelefone = null;
        MotoristaPlaca = null;
    }

    private void GarantirApi()
    {
        if (Origem != OrigemCorridaExterna.Api) throw new RegraDeDominioVioladaException("Corrida manual não tem status do provedor.");
    }

    private static string? Cortado(string? valor, int maximo)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        var limpo = valor.Trim();
        return limpo.Length > maximo ? limpo[..maximo].TrimEnd() : limpo;
    }

    private static string? Texto(string? valor, int maximo, string campo)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        var limpo = valor.Trim();
        if (limpo.Length > maximo) throw new RegraDeDominioVioladaException($"{campo} acima de {maximo} caracteres.");
        return limpo;
    }

    /// <summary>Registro manual. Aceita viagem montando, em rota ou concluída; desfeita não tem corrida.</summary>
    public static CorridaExterna RegistrarManual(
        Viagem viagem, EmpresaEntregador provedor, int custoCentavos, string? motorista, Guid usuarioId, DateTime agora)
    {
        ArgumentNullException.ThrowIfNull(viagem);
        GarantirViagemComCorrida(viagem);
        if (!Enum.IsDefined(provedor)) throw new RegraDeDominioVioladaException("Provedor da corrida inválido.");
        if (provedor == EmpresaEntregador.Propria)
            throw new RegraDeDominioVioladaException("Entregador próprio não é corrida externa: escolha o provedor que fez a corrida.");
        if (usuarioId == Guid.Empty) throw new RegraDeDominioVioladaException("Quem registrou a corrida é obrigatório.");

        return new CorridaExterna
        {
            Id = Guid.NewGuid(),
            EmpresaId = viagem.EmpresaId,
            ViagemId = viagem.Id,
            Provedor = provedor,
            Origem = OrigemCorridaExterna.Manual,
            CustoCentavos = CustoValido(custoCentavos),
            Motorista = MotoristaValido(motorista),
            RegistradoPorUsuarioId = usuarioId,
            RegistradoEm = Datas.Utc(agora),
        };
    }

    /// <summary>
    /// Corrige custo e motorista e guarda quem corrigiu. Quem registrou não muda. Vale também com a viagem
    /// desfeita depois do registro: é assim que se zera a corrida cancelada ou se acerta a que foi paga.
    /// </summary>
    public void Corrigir(Viagem viagem, int custoCentavos, string? motorista, Guid usuarioId, DateTime agora)
    {
        ArgumentNullException.ThrowIfNull(viagem);
        if (viagem.Id != ViagemId) throw new RegraDeDominioVioladaException("Corrida de outra viagem.");
        if (usuarioId == Guid.Empty) throw new RegraDeDominioVioladaException("Quem corrigiu a corrida é obrigatório.");
        if (Origem == OrigemCorridaExterna.Api)
            throw new RegraDeDominioVioladaException("Corrida chamada pela API: custo e motorista vêm do provedor.");

        var custo = CustoValido(custoCentavos);
        var nome = MotoristaValido(motorista);
        CustoCentavos = custo;
        Motorista = nome;
        CorrigidoPorUsuarioId = usuarioId;
        CorrigidoEm = Datas.Utc(agora);
    }

    private static void GarantirViagemComCorrida(Viagem viagem)
    {
        if (viagem.Situacao == SituacaoViagem.Desfeita)
            throw new RegraDeDominioVioladaException("Viagem desfeita não tem corrida.");
    }

    private static int CustoValido(int custoCentavos)
    {
        if (custoCentavos < 0) throw new RegraDeDominioVioladaException("O custo da corrida não pode ser negativo.");
        if (custoCentavos > CustoMaximoCentavos) throw new RegraDeDominioVioladaException("Custo da corrida acima de R$ 5.000,00. Confira o valor.");
        return custoCentavos;
    }

    private static string? MotoristaValido(string? motorista)
    {
        if (string.IsNullOrWhiteSpace(motorista)) return null;
        var limpo = motorista.Trim();
        if (limpo.Length > MotoristaTamanhoMaximo)
            throw new RegraDeDominioVioladaException($"Motorista acima de {MotoristaTamanhoMaximo} caracteres.");
        return limpo;
    }
}