using EasyStock.Domain.Defaults;
using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.Ports.Output.Events;
using EasyStock.Domain.Events;
using EasyStock.Domain.Services;
using EasyStock.Domain.Specifications;
using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.RegistrarEntradaEstoque
{
    public sealed record RegistrarEntradaEstoqueCommand(
        [property: Required] Guid EmpresaId,
        [property: Required] Guid ProdutoId,
        Guid? ProdutoVariacaoId,
        [property: Range(typeof(decimal), "0.001", "999999999999")] decimal Quantidade,
        [property: Range(0, double.MaxValue)] decimal CustoUnitario,
        decimal? PrecoVendaSugerido,
        DateTime DataEntrada,
        NaturezaMovimentacaoEstoque Natureza,
        string? CodigoInterno,
        string? CodigoLote,
        string? CodigoMarketplace,
        string? VariacaoDescricao,
        string? Cor,
        string? Tamanho,
        string? FornecedorNome,
        DateTime? Validade,
        string? Observacoes,
        string? DescricaoAnuncio,
        string? DocumentoReferencia,
        DimensoesInput? DimensoesReais,
        string? InstrucoesGeracaoDescricao,
        Guid? LojaId = null);

    public sealed record RegistrarEntradaEstoqueResult(
        Guid ItemEstoqueId,
        Guid MovimentacaoId,
        string? DescricaoAnuncio,
        string ChavePesquisa);

    public class RegistrarEntradaEstoqueUseCase(
        IProdutoRepository produtoRepository,
        IProdutoVariacaoRepository produtoVariacaoRepository,
        IItemEstoqueRepository itemEstoqueRepository,
        IMovimentacaoEstoqueRepository movimentacaoEstoqueRepository,
        IUnitOfWork unitOfWork,
        ILogger<RegistrarEntradaEstoqueUseCase> logger,
        IGeradorDescricaoAnuncio? geradorDescricaoAnuncio = null,
        IPublicadorEventos? publicadorEventos = null,
        ILojaRepository? lojaRepository = null,
        IConfiguracaoLojaRepository? configuracaoLojaRepository = null,
        EasyStock.Application.Ports.Output.ICurrentUserAccessor? currentUser = null,
        ILoteRepository? loteRepository = null)
    {
        // #306: publicador opcional na assinatura (DI o registra); se um evento precisa
        // ser publicado e ele esta null, falha explicito em vez de descartar em silencio.
        private IPublicadorEventos PublicadorObrigatorio() =>
            publicadorEventos ?? throw new InvalidOperationException(
                "IPublicadorEventos nao injetado: eventos de RegistrarEntradaEstoque seriam perdidos silenciosamente (#306).");

        public virtual async Task<RegistrarEntradaEstoqueResult> ExecuteAsync(RegistrarEntradaEstoqueCommand command)
        {
            logger.LogInformation("Registrando entrada de estoque. ProdutoId: {ProdutoId}, Quantidade: {Quantidade}", command.ProdutoId, command.Quantidade);

            UseCaseGuards.EnsureEmpresaId(command.EmpresaId);
            if (command.Quantidade <= 0) throw new QuantidadeInvalidaException(command.Quantidade);

            var produto = await produtoRepository.GetByIdAsync(command.ProdutoId)
                ?? throw new UseCaseValidationException("Produto nao encontrado.");

            if (produto.EmpresaId != command.EmpresaId)
                throw new UseCaseValidationException("O produto informado nao pertence a empresa.");

            if (!new ProdutoAtivoSpecification().EhSatisfeitaPor(produto))
                throw new ProdutoInativoException(produto.Id);

            Domain.Entities.ConfiguracaoLoja? configuracaoLoja = null;
            if (command.LojaId.HasValue)
            {
                if (lojaRepository is null || configuracaoLojaRepository is null)
                    throw new UseCaseValidationException("Configuracao de loja indisponivel para esta operacao.");

                var loja = await lojaRepository.GetByIdAsync(command.EmpresaId, command.LojaId.Value)
                    ?? throw new UseCaseValidationException("Loja nao encontrada.");

                configuracaoLoja = await configuracaoLojaRepository.GetOrDefaultAsync(loja.Id);
            }

            ProdutoVariacao? variacao = null;
            if (command.ProdutoVariacaoId.HasValue)
            {
                variacao = await produtoVariacaoRepository.GetByIdAsync(command.ProdutoVariacaoId.Value)
                    ?? throw new UseCaseValidationException("Variacao de produto nao encontrada.");

                if (variacao.ProdutoId != produto.Id)
                    throw new UseCaseValidationException("A variacao informada nao pertence ao produto.");

                if (variacao.EmpresaId != command.EmpresaId)
                    throw new UseCaseValidationException("A variacao informada nao pertence a empresa.");

                if (!variacao.Ativa)
                    throw new UseCaseValidationException("A variacao informada esta inativa.");
            }

            var quantidade = Quantidade.From(command.Quantidade);
            var descricaoAnuncio = await ResolverDescricaoAnuncioAsync(command, produto, variacao);
            var agora = DateTime.UtcNow;

            // P5: o codigo de lote informado (digitado ou auto-gerado no front) vira um Lote real,
            // visivel em /lotes. O mesmo VO normalizado vai no ItemEstoque e no Lote para casarem.
            var codigoLote = string.IsNullOrWhiteSpace(command.CodigoLote) ? null : CodigoLote.From(command.CodigoLote);

            var item = ItemEstoque.CriarParaEntrada(
                Guid.NewGuid(),
                command.EmpresaId,
                produto,
                variacao,
                quantidade,
                Dinheiro.FromDecimal(command.CustoUnitario),
                command.PrecoVendaSugerido.HasValue ? Dinheiro.FromDecimal(command.PrecoVendaSugerido.Value) : null,
                command.DataEntrada,
                command.CodigoInterno,
                codigoLote,
                command.CodigoMarketplace,
                command.VariacaoDescricao,
                command.Cor,
                command.Tamanho,
                descricaoAnuncio,
                command.DimensoesReais.ToValueObjectOrNull(),
                command.FornecedorNome,
                command.Validade.HasValue ? Validade.From(command.Validade.Value) : null,
                command.Observacoes,
                agora);

            if (command.LojaId.HasValue)
                item.LojaId = command.LojaId.Value;

            var limiares = LimiarEstoqueResolver.Resolver(produto, produto.Categoria, configuracaoLoja);
            item.QuantidadeMinima = limiares.QuantidadeMinima;
            item.QuantidadeCritica = limiares.QuantidadeCritica;
            item.RecalcularIndicadores(command.DataEntrada, configuracaoLoja?.DiasAlertaParado ?? OperacionalDefaults.DiasAlertaParado);

            var auditoria = currentUser is null ? null : new AuditoriaContexto(
                UsuarioId: currentUser.UsuarioId == Guid.Empty ? null : currentUser.UsuarioId,
                Ip: currentUser.Ip,
                UserAgent: currentUser.UserAgent,
                DispositivoId: currentUser.DispositivoId);

            var movimentacao = MovimentacaoEstoque.CriarEntrada(
                Guid.NewGuid(),
                command.EmpresaId,
                item,
                command.Natureza,
                quantidade,
                item.CustoUnitario,
                command.DataEntrada,
                descricaoAnuncio,
                command.DocumentoReferencia,
                agora,
                auditoria);

            await itemEstoqueRepository.InsertAsync(item);
            await movimentacaoEstoqueRepository.InsertAsync(movimentacao);
            await VincularLoteEntradaAsync(command, produto, codigoLote);
            await unitOfWork.CommitAsync();

            logger.LogInformation("Entrada de estoque registrada com sucesso. ItemEstoqueId: {ItemEstoqueId}, MovimentacaoId: {MovimentacaoId}", item.Id, movimentacao.Id);

            await PublicadorObrigatorio().PublicarAsync(new EntradaEstoqueRegistrada(
                Guid.NewGuid(), agora, item.Id, item.ProdutoId, item.EmpresaId,
                item.QuantidadeAtual.Value, item.CodigoLote?.ToString()));

            return new RegistrarEntradaEstoqueResult(item.Id, movimentacao.Id, descricaoAnuncio, item.ChavePesquisa ?? string.Empty);
        }

        /// <summary>
        /// P5: vincula a entrada a um Lote real (visivel em /lotes). So age quando ha codigo de lote
        /// e o repositorio esta injetado (DI em producao; null nos testes que nao exercitam lote).
        ///  - codigo novo  -> cria Lote (Origem="entrada") + 1 LoteItem
        ///  - codigo existente em_producao -> adiciona LoteItem
        ///  - codigo existente finalizado  -> so referencia pelo codigo (nao muta lote congelado)
        /// Tudo na MESMA transacao da entrada (compartilha unitOfWork) — atomico.
        /// </summary>
        private async Task VincularLoteEntradaAsync(RegistrarEntradaEstoqueCommand command, Produto produto, CodigoLote? codigoLote)
        {
            if (loteRepository is null || codigoLote is null) return;

            var codigo = codigoLote.Value;
            if (codigo.Length > 40)
                throw new UseCaseValidationException("Codigo de lote deve ter no maximo 40 caracteres para aparecer em Lotes.");

            var existente = await loteRepository.FindByCodigoAsync(command.EmpresaId, codigo);
            if (existente is null)
            {
                var lote = Lote.Criar(command.EmpresaId, codigo, command.DataEntrada, command.LojaId);
                lote.Origem = "entrada";
                lote.OperadorUserId = currentUser?.UsuarioId is { } uid && uid != Guid.Empty ? uid : null;
                lote.Itens.Add(MontarLoteItem(lote.Id, produto, command));
                await loteRepository.AddAsync(lote);
            }
            else if (!existente.EstaFinalizado)
            {
                await loteRepository.AddItemAsync(MontarLoteItem(existente.Id, produto, command));
            }
        }

        private static LoteItem MontarLoteItem(Guid loteId, Produto produto, RegistrarEntradaEstoqueCommand command)
        {
            // LoteItem.Quantidade e int (unidades); entrada pode ser decimal (ex.: 2,5 kg).
            // Arredonda para int >= 1 no lote; a quantidade exata fica na MovimentacaoEstoque.
            var qtd = (int)Math.Max(1m, Math.Round(command.Quantidade, MidpointRounding.AwayFromZero));

            int? validadeDias = null;
            DateTime? expira = null;
            if (command.Validade is { } val)
            {
                expira = val;
                var dias = (val.Date - command.DataEntrada.Date).Days;
                validadeDias = dias > 0 ? dias : null;
            }

            var nome = produto.Nome.Length > 150 ? produto.Nome[..150] : produto.Nome;
            return new LoteItem
            {
                Id = Guid.NewGuid(),
                LoteId = loteId,
                ProdutoId = produto.Id,
                Nome = nome,
                Quantidade = qtd,
                ValidadeDias = validadeDias,
                ExpiraEm = expira,
                CriadoEm = DateTime.UtcNow
            };
        }

        private async Task<string?> ResolverDescricaoAnuncioAsync(RegistrarEntradaEstoqueCommand command, Produto produto, ProdutoVariacao? variacao)
        {
            if (!string.IsNullOrWhiteSpace(command.DescricaoAnuncio))
                return command.DescricaoAnuncio.Trim();

            if (geradorDescricaoAnuncio is null)
                return produto.SugestaoDescricaoAnuncio;

            try
            {
                return await geradorDescricaoAnuncio.GerarAsync(produto, variacao, null, command.InstrucoesGeracaoDescricao);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao gerar descrição de anúncio via IA para o produto {ProdutoId}. Entrada será salva sem descrição.", produto.Id);
                return produto.SugestaoDescricaoAnuncio;
            }
        }
    }
}
