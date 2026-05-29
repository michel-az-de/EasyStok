using EasyStock.Application.UseCases.Pedidos;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;
using PedidoEntity = EasyStock.Domain.Entities.Pedido;

namespace EasyStock.Application.UseCases.CriarPedido;

public sealed record CriarPedidoItemInput(
    [property: Required][property: MaxLength(150)] string Nome,
    decimal Quantidade,
    decimal PrecoUnitario,
    Guid? ProdutoId = null,
    [property: MaxLength(16)] string? Emoji = null,
    [property: MaxLength(32)] string? Unidade = null,
    string? Observacao = null);

public sealed record CriarPedidoCommand(
    [property: Required] Guid EmpresaId,
    Guid? LojaId = null,
    Guid? ClienteId = null,
    [property: MaxLength(150)] string? ClienteNomeAdHoc = null,
    [property: MaxLength(32)]  string? ClienteAptAdHoc = null,
    [property: MaxLength(32)]  string? ClienteTelefoneAdHoc = null,
    string? Observacoes = null,
    [property: MaxLength(20)] string? Origem = "web",
    [property: MaxLength(64)] string? MobileOrderId = null,
    IReadOnlyList<CriarPedidoItemInput>? Itens = null,
    Guid? CriadoPorUserId = null,
    [property: MaxLength(120)] string? CriadoPorNome = null,
    // F5 — agendamento (MVP). NULL = pedido pra agora.
    DateTime? AgendadoParaEm = null);

/// <summary>
/// Cria um novo pedido (encomenda) — Onda P2. Se <see cref="ClienteId"/>
/// foi informado, snapshot dos dados é copiado do <see cref="Cliente"/>;
/// senão, snapshot vem dos campos *AdHoc (pedido balcão/anônimo).
///
/// Se houver clienteId válido, atualiza <see cref="Cliente.OrderCount"/>
/// e <see cref="Cliente.LastOrderAt"/> (paridade com app mobile).
/// </summary>
public class CriarPedidoUseCase(
    IPedidoRepository pedidoRepo,
    IClienteRepository clienteRepo,
    IProdutoRepository produtoRepo,
    IUnitOfWork uow,
    ILogger<CriarPedidoUseCase> logger)
{
    public async Task<PedidoResult> ExecuteAsync(CriarPedidoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        // Postgres 'timestamp with time zone' exige UTC; a data agendada vem do cliente
        // com Kind=Unspecified e o Npgsql rejeita no save. Normaliza para UTC.
        var agendadoParaEm = DataUtc.ParaUtcOpcional(cmd.AgendadoParaEm);

        // F5 — agendamento precisa ser no futuro. PWA valida client-side, mas
        // API publica e SyncController nao validam — espelha a checagem aqui
        // pra impedir pedido agendado pro passado (worker ignoraria silenciosamente
        // via filtro de status, mas dado corrompido fica registrado).
        if (agendadoParaEm.HasValue && agendadoParaEm.Value <= DateTime.UtcNow)
            throw new UseCaseValidationException("Data agendada precisa ser no futuro.");

        // Pedido sem itens é permitido (operador pode adicionar depois via
        // AdicionarItemPedido), mas se itens vieram, todos devem ser válidos.
        if (cmd.Itens != null && cmd.Itens.Count > 0)
        {
            foreach (var i in cmd.Itens)
            {
                if (i.Quantidade <= 0)
                    throw new UseCaseValidationException("Item com quantidade <= 0 não é permitido.");
                if (string.IsNullOrWhiteSpace(i.Nome))
                    throw new UseCaseValidationException("Item sem nome não é permitido.");
                if (i.PrecoUnitario < 0)
                    throw new UseCaseValidationException("Item com preço negativo não é permitido.");
            }
        }

        // Resolve cliente (se informado) — snapshot vem dele.
        ClienteEntity? cliente = null;
        if (cmd.ClienteId.HasValue && cmd.ClienteId.Value != Guid.Empty)
        {
            cliente = await clienteRepo.GetByIdAsync(cmd.EmpresaId, cmd.ClienteId.Value);
            if (cliente == null)
                throw new UseCaseValidationException("Cliente não encontrado nesta empresa.");
        }

        var pedido = PedidoEntity.Criar(cmd.EmpresaId, cliente, cmd.LojaId, cmd.Origem ?? "web");
        // Snapshot ad-hoc (sobrescreve quando cliente é null)
        if (cliente == null)
        {
            pedido.ClienteNome = cmd.ClienteNomeAdHoc;
            pedido.ClienteApt = cmd.ClienteAptAdHoc;
            pedido.ClienteTelefone = cmd.ClienteTelefoneAdHoc;
        }
        pedido.Observacoes = cmd.Observacoes;
        pedido.MobileOrderId = cmd.MobileOrderId;
        pedido.AgendadoParaEm = agendadoParaEm;

        // Adiciona itens.
        if (cmd.Itens != null)
        {
            foreach (var input in cmd.Itens)
            {
                if (input.Quantidade <= 0)
                    throw new UseCaseValidationException("Quantidade do item deve ser maior que zero.");

                // Valida que o ProdutoId, se informado, pertence à mesma empresa.
                // Sem isso, item pode referenciar produto de outro tenant via FK direta.
                if (input.ProdutoId.HasValue && input.ProdutoId.Value != Guid.Empty)
                {
                    var produto = await produtoRepo.GetByIdAsync(cmd.EmpresaId, input.ProdutoId.Value);
                    if (produto is null)
                        throw new UseCaseValidationException("Produto do item não pertence a esta empresa.");
                }

                var item = new PedidoItem
                {
                    Id = Guid.NewGuid(),
                    PedidoId = pedido.Id,
                    ProdutoId = input.ProdutoId,
                    Nome = input.Nome.Trim(),
                    Emoji = input.Emoji,
                    Unidade = input.Unidade,
                    Quantidade = input.Quantidade,
                    PrecoUnitario = input.PrecoUnitario,
                    Observacao = input.Observacao,
                    CriadoEm = DateTime.UtcNow
                };
                item.RecalcularSubtotal();
                pedido.Itens.Add(item);
            }
        }

        pedido.RecalcularTotal();

        // Trail de auditoria — evento de criação.
        pedido.Eventos.Add(new PedidoEvento
        {
            Id = Guid.NewGuid(),
            PedidoId = pedido.Id,
            Tipo = "criado",
            StatusNovo = pedido.Status,
            UsuarioId = cmd.CriadoPorUserId,
            UsuarioNome = cmd.CriadoPorNome,
            Origem = cmd.Origem,
            OcorridoEm = DateTime.UtcNow,
            Detalhes = $"{pedido.Itens.Count} item(s), total {pedido.Total:C}"
        });

        await pedidoRepo.AddAsync(pedido);

        // Atualiza métricas do cliente (paridade com app: OrderCount + LastOrderAt).
        if (cliente != null)
        {
            cliente.RegistrarPedido(pedido.CriadoEm);
            await clienteRepo.UpdateAsync(cliente);
        }
        else if (!string.IsNullOrWhiteSpace(cmd.ClienteNomeAdHoc))
        {
            // Pedido balcão: tenta associar ao cliente cadastrado com o mesmo nome.
            var matches = await clienteRepo.SearchAsync(cmd.EmpresaId, cmd.ClienteNomeAdHoc, maxResults: 1);
            var clienteAdHoc = matches.FirstOrDefault(c =>
                string.Equals(c.Nome, cmd.ClienteNomeAdHoc, StringComparison.OrdinalIgnoreCase));
            if (clienteAdHoc != null)
            {
                clienteAdHoc.RegistrarPedido(pedido.CriadoEm);
                await clienteRepo.UpdateAsync(clienteAdHoc);
            }
        }

        await uow.CommitAsync();

        logger.LogInformation("Pedido {Id} criado (cliente={ClienteId}, total={Total}, origem={Origem}).",
            pedido.Id, pedido.ClienteId, pedido.Total, pedido.Origem);

        return Map(pedido);
    }

    internal static PedidoResult Map(PedidoEntity p) => new(
        p.Id, p.EmpresaId, p.LojaId, p.ClienteId,
        p.ClienteNome, p.ClienteApt, p.ClienteTelefone,
        p.Status, p.Total, p.TotalPago,
        p.Observacoes, p.Origem, p.MobileOrderId, p.VendaId,
        p.Itens.Count,
        p.CriadoEm, p.AlteradoEm, p.EntreguEm, p.CanceladoEm,
        p.AgendadoParaEm);
}
