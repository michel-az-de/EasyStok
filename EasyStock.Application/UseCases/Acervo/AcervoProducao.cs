using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Domain.Entities.Acervo;

namespace EasyStock.Application.UseCases.Acervo;

public sealed record DadosBriefing(Guid OperacaoId, Guid ItemId, string Canal, string Finalidade, string Formato,
    Guid[] Referencias, string Restricoes, string Prompt, Guid? AnteriorId = null);
public sealed record DadosUso(string Canal, decimal? LarguraMm = null, decimal? AlturaMm = null);
public sealed record CoberturaAcervo(Guid ItemId, Guid? ProdutoId, string Nome, string Canal, int Total, int Fotografias,
    int Geradas, int Comunicacoes, int Adequadas, int Candidatas, int Pendentes, int FotosNoCardapio, string[] FormatosPendentes);

public sealed partial class AcervoUseCase
{
    public static readonly string[] Canais = ["site", "instagram", "whatsapp", "impressao"];
    private static string[] FormatosCanal(string canal) => canal switch {
        "site" => ["1:1", "3:2", "16:9"], "instagram" => ["1:1", "4:5", "9:16"],
        "whatsapp" => ["1:1", "4:5"], "impressao" => ["1:1", "4:5", "16:9", "9:16", "3:2", "livre"], _ => [] };
    private static void Canal(string canal)
    {
        if (!Canais.Contains(canal)) throw new UseCaseValidationException("Canal inválido.");
    }
    public static bool ProporcaoConfere(string formato, int largura, int altura)
    {
        if (largura <= 0 || altura <= 0) return false;
        if (formato == "livre") return true;
        var partes = formato.Split(':');
        return partes.Length == 2 && double.TryParse(partes[0], out var x) && double.TryParse(partes[1], out var y) && y > 0 &&
            Math.Abs((double)largura / altura / (x / y) - 1) <= .02;
    }
    private static bool Compativel(AcervoImagem i, string canal) => FormatosCanal(canal).Contains(i.Formato) &&
        ProporcaoConfere(i.Formato, i.Largura, i.Altura) && (canal == "impressao" ||
        (canal == "instagram" ? i.Largura >= 1080 : Math.Min(i.Largura, i.Altura) >= 600));

    public Task<IReadOnlyList<AcervoImagem>> BuscarAsync(Guid empresa, int pagina, string busca, string origem, string formato,
        string natureza, Guid? itemId, string canal, CancellationToken ct)
    {
        Empresa(empresa); busca ??= ""; origem ??= ""; formato ??= ""; natureza ??= ""; canal ??= "";
        ValidarTextos(busca, origem, formato, natureza, canal);
        if (busca.Length > 160 || (origem.Length > 0 && origem is not ("Fotografia" or "IA" or "Canva")) ||
            (natureza.Length > 0 && natureza is not ("Produto" or "Comunicacao" or "NaoClassificada")))
            throw new UseCaseValidationException("Filtro inválido.");
        if (formato.Length > 0) Formato(formato);
        if (canal.Length > 0) Canal(canal);
        return repo.BuscarAsync(empresa, Math.Clamp(pagina, 0, 10000), busca.Trim(), origem, formato, natureza, itemId, canal, ct);
    }
    public Task<IReadOnlyList<AcervoBriefing>> BriefingsAsync(Guid empresa, CancellationToken ct) => repo.BriefingsAsync(Empresa(empresa), ct);
    public Task<IReadOnlyList<AcervoUso>> UsosAsync(Guid empresa, CancellationToken ct) => repo.UsosAsync(Empresa(empresa), ct);
    public Task<AcervoImagem> ClassificarAsync(Guid empresa, Guid id, string natureza, string naturezaEsperada, CancellationToken ct)
    {
        Empresa(empresa);
        if (natureza is not ("Produto" or "Comunicacao")) throw new UseCaseValidationException("Natureza inválida.");
        return uow.ExecuteInTransactionSemRetryAsync(async token => {
            await repo.BloquearSolicitacaoAsync(empresa, id, token);
            var imagem = await ObterAsync(empresa, id, token);
            if (imagem.Natureza == natureza) return imagem;
            if (imagem.Natureza != naturezaEsperada) throw new UseCaseValidationException("A classificação mudou. Atualize a ficha antes de confirmar.");
            imagem.Natureza = natureza; await uow.CommitAsync(); return imagem;
        }, ct);
    }
    public Task<IReadOnlyList<AcervoSolicitacao>> HistoricoFilaAsync(Guid empresa, CancellationToken ct) => repo.HistoricoFilaAsync(Empresa(empresa), ct);
    public async Task<IReadOnlyList<CoberturaAcervo>> CoberturaAsync(Guid empresa, CancellationToken ct)
    {
        Empresa(empresa);
        var itens = await repo.ItensAsync(empresa, ct);
        var imagens = await repo.MapaImagensAsync(empresa, ct);
        var usos = await repo.UsosAsync(empresa, ct);
        var fila = await repo.MapaSolicitacoesAsync(empresa, ct);
        var briefings = await repo.MapaBriefingsAsync(empresa, ct);
        var vinculos = imagens.Select(i => (Imagem: i, Itens: JsonSerializer.Deserialize<Guid[]>(i.ItensJson)!)).ToArray();
        return itens.SelectMany(item => Canais.Select(canal => {
            var acervo = vinculos.Where(v => v.Itens.Contains(item.Id)).Select(v => v.Imagem).ToArray();
            var adequadas = acervo.Where(i => i.AprovadoEm != null && Compativel(i, canal) && usos.Any(u => u.ImagemId == i.Id && u.Canal == canal)).ToArray();
            var pendentes = fila.Count(s => s.ItemId == item.Id && s.BriefingId != null && briefings.Any(b => b.Id == s.BriefingId && b.Canal == canal));
            var esperados = canal switch { "site" => new[] { "1:1", "3:2" }, "instagram" => ["4:5", "9:16"], "whatsapp" => ["1:1", "4:5"], _ => ["medida física"] };
            return new CoberturaAcervo(item.Id, item.ProdutoId, item.NomeEfetivo() ?? "Item", canal, acervo.Length,
                acervo.Count(i => i.Origem == "Fotografia" && i.Natureza == "Produto"), acervo.Count(i => i.Origem == "IA" && i.Natureza != "Comunicacao"),
                acervo.Count(i => i.Natureza == "Comunicacao"), adequadas.Length, acervo.Count(i => Compativel(i, canal)), pendentes,
                canal == "site" ? item.ObterFotosGaleria().Append(item.FotoUrl).Where(url => !string.IsNullOrWhiteSpace(url)).Distinct().Count() : 0,
                canal == "impressao" ? (adequadas.Length > 0 ? [] : esperados) : esperados.Except(adequadas.Select(i => i.Formato)).ToArray());
        })).ToArray();
    }
    public Task<AcervoBriefing> SalvarBriefingAsync(Guid empresa, Guid autor, DadosBriefing d, CancellationToken ct)
    {
        Empresa(empresa); Canal(d.Canal); Formato(d.Formato); ValidarTextos(d.Finalidade, d.Restricoes, d.Prompt);
        if (autor == Guid.Empty || d.OperacaoId == Guid.Empty || d.ItemId == Guid.Empty) throw new UseCaseValidationException("Operação não identificada.");
        if (!FormatosCanal(d.Canal).Contains(d.Formato)) throw new UseCaseValidationException("O formato não é adequado ao canal.");
        if (string.IsNullOrWhiteSpace(d.Finalidade) || d.Finalidade.Length > 500 || string.IsNullOrWhiteSpace(d.Restricoes) || d.Restricoes.Length > 4000 ||
            string.IsNullOrWhiteSpace(d.Prompt) || d.Prompt.Length > 7000 || d.Referencias is null || d.Referencias.Length is < 1 or > 10 || d.Referencias.Contains(Guid.Empty))
            throw new UseCaseValidationException("Informe finalidade (500), restrições (4.000), prompt (7.000) e de uma a dez referências aprovadas.");
        var referencias = d.Referencias.Distinct().Order().ToArray();
        var conteudo = JsonSerializer.Serialize(new { d.ItemId, d.Canal, Finalidade = d.Finalidade.Trim(), d.Formato, Referencias = referencias,
            Restricoes = d.Restricoes.Trim(), Prompt = d.Prompt.Trim(), d.AnteriorId });
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conteudo)));
        return uow.ExecuteInTransactionSemRetryAsync(async token => {
            await repo.BloquearSolicitacaoAsync(empresa, d.ItemId, token);
            if (await repo.ItemAsync(empresa, d.ItemId, token) is null) throw new UseCaseValidationException("Item não encontrado.");
            var repetido = await repo.BriefingAsync(empresa, d.OperacaoId, token);
            if (repetido is not null) return repetido.ConteudoHash == hash ? repetido : throw new UseCaseValidationException("Operação já usada para outro briefing.");
            AcervoBriefing? anterior = null;
            if (d.AnteriorId is { } id)
            {
                anterior = await repo.BriefingAsync(empresa, id, token) ?? throw new KeyNotFoundException("Briefing anterior não encontrado.");
                if (anterior.ItemId != d.ItemId) throw new UseCaseValidationException("Uma versão precisa preservar o produto do briefing.");
            }
            var igual = await repo.BriefingPorHashAsync(empresa, hash, token);
            if (igual is not null) return igual;
            if (anterior is not null && await repo.UltimaVersaoAsync(empresa, anterior.RaizId, token) != anterior.Versao)
                throw new UseCaseValidationException("O briefing mudou. Abra sua versão mais recente.");
            foreach (var referencia in referencias)
                if ((await ObterAsync(empresa, referencia, token)).AprovadoEm is null) throw new UseCaseValidationException("Aprove cada referência antes de salvar o briefing.");
            var b = new AcervoBriefing { Id = d.OperacaoId, EmpresaId = empresa, UsuarioId = autor, ItemId = d.ItemId,
                RaizId = anterior?.RaizId ?? d.OperacaoId, AnteriorId = d.AnteriorId, Versao = (anterior?.Versao ?? 0) + 1,
                Canal = d.Canal, Finalidade = d.Finalidade.Trim(), Formato = d.Formato, ReferenciasJson = JsonSerializer.Serialize(referencias),
                Restricoes = d.Restricoes.Trim(), Prompt = d.Prompt.Trim(), ConteudoHash = hash };
            repo.Adicionar(b); await uow.CommitAsync(); return b;
        }, ct);
    }
    public Task<AcervoSolicitacao> EnfileirarBriefingAsync(Guid empresa, Guid autor, Guid id, Guid operacaoId, CancellationToken ct)
    {
        Empresa(empresa);
        if (operacaoId == Guid.Empty || autor == Guid.Empty) throw new UseCaseValidationException("Operação não identificada.");
        return uow.ExecuteInTransactionSemRetryAsync(async token => {
            var b = await repo.BriefingAsync(empresa, id, token) ?? throw new KeyNotFoundException("Briefing não encontrado.");
            await repo.BloquearSolicitacaoAsync(empresa, b.ItemId, token);
            var repetido = await repo.SolicitacaoAsync(empresa, operacaoId, token);
            if (repetido is not null) return repetido.BriefingId == id ? repetido : throw new UseCaseValidationException("Operação já usada para outra solicitação.");
            var existente = await repo.PendenteBriefingAsync(empresa, id, token);
            // Um briefing imutável tem um resultado. Nova tentativa requer nova versão, preservando o resultado anterior.
            if (existente is not null) return existente;
            foreach (var referencia in JsonSerializer.Deserialize<Guid[]>(b.ReferenciasJson)!)
                if ((await ObterAsync(empresa, referencia, token)).AprovadoEm is null) throw new UseCaseValidationException("Referência sem aprovação.");
            if (await repo.ItemAsync(empresa, b.ItemId, token) is null) throw new UseCaseValidationException("Item não encontrado.");
            var s = new AcervoSolicitacao { Id = operacaoId, EmpresaId = empresa, UsuarioId = autor, ItemId = b.ItemId, BriefingId = b.Id,
                Formato = b.Formato, Brief = b.Prompt + "\n\nFinalidade: " + b.Finalidade + "\nRestrições: " + b.Restricoes, ReferenciasJson = b.ReferenciasJson };
            repo.Adicionar(s); await uow.CommitAsync(); return s;
        }, ct);
    }
    public Task<AcervoUso> ConfirmarUsoAsync(Guid empresa, Guid autor, Guid imagemId, DadosUso d, int largura, int altura, CancellationToken ct)
    {
        Empresa(empresa); Canal(d.Canal);
        if (autor == Guid.Empty) throw new UseCaseValidationException("Usuário não identificado.");
        return uow.ExecuteInTransactionSemRetryAsync(async token => {
            await repo.BloquearSolicitacaoAsync(empresa, imagemId, token);
            var imagem = await ObterAsync(empresa, imagemId, token);
            if (imagem.AprovadoEm is null) throw new UseCaseValidationException("Aprove a versão antes de confirmar um uso.");
            imagem.Largura = largura; imagem.Altura = altura;
            if (!Compativel(imagem, d.Canal)) throw new UseCaseValidationException("Proporção ou resolução insuficiente para este canal. Prepare outra versão.");
            if (d.Canal == "impressao")
            {
                if (d.LarguraMm is null or <= 0 or > 2000 || d.AlturaMm is null or <= 0 or > 2000 ||
                    largura * 25.4m / d.LarguraMm < 300 || altura * 25.4m / d.AlturaMm < 300 ||
                    Math.Abs((decimal)largura / altura / (d.LarguraMm.Value / d.AlturaMm.Value) - 1) > .02m)
                    throw new UseCaseValidationException("Informe medidas físicas compatíveis com o original a pelo menos 300 dpi. A prova física continua necessária.");
            }
            else if (d.LarguraMm is not null || d.AlturaMm is not null) throw new UseCaseValidationException("Medidas físicas se aplicam apenas à impressão.");
            var existente = await repo.UsoAsync(empresa, imagemId, d.Canal, token);
            if (existente is not null)
            {
                if (existente.LarguraMm != d.LarguraMm || existente.AlturaMm != d.AlturaMm) throw new UseCaseValidationException("Este uso já possui outra medida. Prepare uma nova versão para outro tamanho.");
                return existente;
            }
            var uso = new AcervoUso { EmpresaId = empresa, ImagemId = imagemId, Canal = d.Canal, UsuarioId = autor, LarguraMm = d.LarguraMm, AlturaMm = d.AlturaMm };
            repo.Adicionar(uso); await uow.CommitAsync(); return uso;
        }, ct);
    }
}
