using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Endereco;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Exceptions.Storefront;
using ClienteEntity = EasyStock.Domain.Entities.Cliente;

namespace EasyStock.Application.UseCases.Atendimento.ClienteDaConversa;

/// <summary>Endereço como a Ficha do console coleta: CEP e as partes digitadas.</summary>
public sealed record EnderecoDaConversaInput(string? Cep, string? Logradouro, string? Numero, string? Complemento, string? Bairro);

/// <param name="Telefone">Obrigatório quando a conversa ainda não tem cliente: é por ele que o cadastro é achado.</param>
/// <param name="Endereco">Opcional; com CEP, vira o endereço de entrega padrão do cliente.</param>
/// <param name="Email">Opcional (#1430). Só entra em cadastro que nasce agora; o achado pelo telefone não recebe.</param>
/// <param name="ConfirmarVinculoExistente">
/// #1430: a operadora conferiu com o cliente e manda ligar a conversa ao cadastro que já tem o telefone informado
/// pelo visitante. Sem isso, <see cref="TelefoneJaCadastradoException"/>.
/// </param>
public sealed record CadastrarClienteDaConversaCommand(
    Guid EmpresaId, Guid ConversaId, string? Nome, string? Telefone, EnderecoDaConversaInput? Endereco,
    string? Email = null, bool ConfirmarVinculoExistente = false);

/// <summary>
/// #1430: o telefone que o visitante do chat do site digitou já é de um cadastro. Ninguém verificou que o
/// visitante é essa pessoa, então nada foi ligado nem gravado: a operadora confere e repete o pedido com
/// <see cref="CadastrarClienteDaConversaCommand.ConfirmarVinculoExistente"/>. 409 com o código e o nome.
/// </summary>
public sealed class TelefoneJaCadastradoException(Guid clienteId, string nome)
    : Exception($"Este telefone já é de {nome}. Confira com o cliente antes de ligar esta conversa ao cadastro dele.")
{
    public const string Codigo = "TELEFONE_JA_CADASTRADO";

    public Guid ClienteId { get; } = clienteId;
    public string Nome { get; } = nome;
}

/// <param name="Novo">O cadastro nasceu agora (o telefone não existia na empresa).</param>
/// <param name="DentroDaArea">Nulo sem endereço; falso quando o CEP está fora da área de entrega.</param>
public sealed record ClienteDaConversaResult(
    Guid ClienteId, string Nome, string? Telefone, string? Endereco, string? Cep, bool Novo,
    bool? DentroDaArea, string? MensagemForaArea, string? Email = null);

/// <summary>
/// #1276: a dona cadastra pelo console o cliente de uma conversa que chegou sem ele (chat do site,
/// Instagram, Messenger, e-mail, SMS). Sem isso o pedido da conversa é recusado (<see cref="Comanda.GerarPedidoConversaUseCase"/>).
/// Compõe o que já existe: <see cref="IdentificarClientePorTelefoneUseCase"/> acha ou cria pelo telefone
/// (o cadastro antigo é reaproveitado, sem trocar o nome dele), a conversa é vinculada, e o endereço passa por
/// <see cref="ValidarEnderecoUseCase"/> e vira o padrão por <see cref="ConfirmarEnderecoClienteUseCase"/>.
/// Com cliente já vinculado, atualiza nome e telefone informados. Endereço fora da área é gravado mesmo assim
/// e volta com <c>DentroDaArea=false</c>: a dona decide (liberar fora da área ou corrigir).
/// <para>
/// #1430: é aqui que a loja confirma o contato informado pelo visitante do chat do site. Telefone digitado no
/// formulário não é telefone verificado: se ele já é de um cadastro, a conversa só é ligada com
/// <see cref="CadastrarClienteDaConversaCommand.ConfirmarVinculoExistente"/>, e esse cadastro não recebe o e-mail
/// nem o aceite do visitante. Só o cadastro que nasce aqui leva o e-mail e o consentimento transacional do canal
/// (origem <see cref="OrigemAceiteChatSite"/>).
/// </para>
/// </summary>
public sealed class CadastrarClienteDaConversaUseCase(
    IConversaRepository conversaRepository,
    IClienteRepository clienteRepository,
    IUnitOfWork unitOfWork,
    IdentificarClientePorTelefoneUseCase identificarCliente,
    ValidarEnderecoUseCase validarEndereco,
    ConfirmarEnderecoClienteUseCase confirmarEndereco,
    IConsentimentoContatoRepository consentimentos)
{
    private const int NomeTamanhoMaximo = 150;
    public const string OrigemAceiteChatSite = "chat_site_formulario";

    public async Task<ClienteDaConversaResult> ExecuteAsync(CadastrarClienteDaConversaCommand command, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        UseCaseGuards.EnsureEmpresaId(command.EmpresaId);

        var conversa = await conversaRepository.ObterPorIdAsync(command.EmpresaId, command.ConversaId, ct)
            ?? throw new ConversaNaoEncontradaException(command.ConversaId);

        var telefone = TelefoneE164(command.Telefone);
        var nome = NomeLimpo(command.Nome);
        var email = EmailValido(command.Email);

        ClienteEntity cliente;
        var novo = false;
        if (conversa.ClienteId is { } clienteId)
        {
            cliente = await clienteRepository.GetByIdWithDetailsAsync(command.EmpresaId, clienteId)
                ?? throw new RegraDeDominioVioladaException("O cliente desta conversa não existe mais.");
            // #1430: pedido de ligar que chega de novo (segundo toque, outra aba) já está atendido; o nome e o
            // telefone do visitante que vêm junto não entram no cadastro. E-mail nunca entra por aqui: só o
            // cartão do visitante manda, e ele vale para o cadastro que nasce.
            if (!command.ConfirmarVinculoExistente)
            {
                AtualizarDados(cliente, nome, telefone);
                await clienteRepository.UpdateAsync(cliente);
            }
        }
        else
        {
            if (telefone is null)
                throw new UseCaseValidationException("Informe o telefone do cliente para cadastrar.");
            var identificacao = await identificarCliente.ExecuteAsync(
                new IdentificarClientePorTelefoneInput(command.EmpresaId, telefone.TrimStart('+'), nome), ct);
            cliente = identificacao.Cliente;
            novo = identificacao.EhNovo;
            // #1430: o telefone do formulário não prova que o visitante é o dono do cadastro. Sem a operadora
            // confirmar, nada é ligado nem gravado (quem achou não criou, e o commit só vem depois daqui).
            if (!novo && TelefoneVeioDoVisitante(conversa, telefone) && !command.ConfirmarVinculoExistente)
                throw new TelefoneJaCadastradoException(cliente.Id, cliente.Nome);
            conversa.VincularCliente(cliente.Id);
            // E-mail e aceite só entram no cadastro que nasce desta conversa: o que já existia não muda.
            if (novo)
            {
                if (email is not null) cliente.Email = email;
                await RegistrarAceiteDoChatSiteAsync(command.EmpresaId, conversa, cliente.Id, ct);
            }
        }

        // A inbox mostra o nome do contato: passa a ser o do cadastro (o do chat do site é genérico).
        conversa.AtualizarContatoNome(cliente.Nome);

        // O endereço relê o cliente do banco: o cadastro novo precisa estar gravado antes.
        await unitOfWork.CommitAsync();

        bool? dentroDaArea = null;
        string? mensagemForaArea = null;
        if (!string.IsNullOrWhiteSpace(command.Endereco?.Cep))
        {
            var e = command.Endereco;
            var validacao = await validarEndereco.ExecuteAsync(
                new ValidarEnderecoInput(command.EmpresaId, e.Cep, e.Logradouro, e.Numero, e.Complemento, e.Bairro), ct);
            if (validacao.Motivo == ValidarEnderecoUseCase.MotivoCepInvalido)
                throw new UseCaseValidationException("CEP inválido: informe os 8 dígitos.");

            await confirmarEndereco.ExecuteAsync(
                new ConfirmarEnderecoClienteCommand(command.EmpresaId, cliente.Id, validacao.EnderecoNormalizado), ct);
            dentroDaArea = validacao.DentroDaArea;
            mensagemForaArea = validacao.MensagemForaArea;
        }

        return new ClienteDaConversaResult(
            cliente.Id, cliente.Nome, cliente.Telefone, cliente.Endereco, cliente.Cep, novo, dentroDaArea, mensagemForaArea,
            cliente.Email);
    }

    private static bool TelefoneVeioDoVisitante(Conversa conversa, string telefoneE164) =>
        conversa.Canal == CanalConversa.ChatSite && conversa.ContatoTelefoneInformado == telefoneE164;

    private async Task RegistrarAceiteDoChatSiteAsync(Guid empresaId, Conversa conversa, Guid clienteId, CancellationToken ct)
    {
        if (conversa.Canal != CanalConversa.ChatSite || conversa.ContatoInformadoEm is not { } aceiteEm)
            return;
        var atuais = await consentimentos.ListarDoClienteAsync(empresaId, clienteId, ct);
        if (atuais.Any(c => c.Canal == CanalConversa.ChatSite && c.Finalidade == FinalidadeContato.Transacional))
            return;
        await consentimentos.AddAsync(ConsentimentoContato.Registrar(empresaId, clienteId, CanalConversa.ChatSite,
            FinalidadeContato.Transacional, SituacaoConsentimento.Concedido, OrigemAceiteChatSite, aceiteEm), ct);
    }

    private static string? EmailValido(string? email)
    {
        try
        {
            return ContatoInformadoVisitante.EmailNormalizado(email);
        }
        catch (RegraDeDominioVioladaException)
        {
            throw new UseCaseValidationException("E-mail inválido.");
        }
    }

    private static void AtualizarDados(ClienteEntity cliente, string? nome, string? telefone)
    {
        if (nome is not null)
            cliente.Nome = nome;
        if (telefone is not null && telefone != cliente.Telefone)
        {
            cliente.Telefone = telefone;
            // Mesmo hash do OTP: o login do storefront com este número cai neste cadastro.
            cliente.TelefoneHash = ClienteOtp.CalcularTelefoneHash(telefone);
        }
    }

    private static string? TelefoneE164(string? telefone)
    {
        if (string.IsNullOrWhiteSpace(telefone))
            return null;
        try
        {
            return NormalizadorTelefone.NormalizarE164Br(telefone);
        }
        catch (TelefoneInvalidoException)
        {
            throw new UseCaseValidationException("Telefone inválido: informe DDD e número.");
        }
    }

    private static string? NomeLimpo(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
            return null;
        var limpo = nome.Trim();
        return limpo.Length > NomeTamanhoMaximo ? limpo[..NomeTamanhoMaximo] : limpo;
    }
}
