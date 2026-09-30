// Criadores de ação da Frente 5 · Encerrar atendimento com resumo (rodada 5,
// seção 5). `AtendimentoProvider.jsx` chama `criarAcoesEncerramento(despachar)`
// e espalha o resultado no objeto `acoes` do contexto; a F5 só mexe aqui
// dentro, nunca no Provider. `abrirEncerramento`/`fecharEncerramento` (o
// `ui.encerrando`) já vêm prontos do passo zero, direto no Provider: a F5 só
// cuida das ações do CONTEÚDO do resumo. `despachar` já é o `dispatch` do
// reducer; `agora` vem de quem chama (mesmo padrão de `decidirAreaEntrega` em
// `PainelFicha.jsx`), porque este arquivo não tem acesso ao relógio do
// Provider.
import * as acao from '../acoes'

export function criarAcoesEncerramento(despachar) {
  return {
    salvarAvaliacaoCliente: (id, valor) => despachar({ tipo: acao.SALVAR_AVALIACAO_CLIENTE, id, valor }),
    salvarAutoavaliacao: (id, valor) => despachar({ tipo: acao.SALVAR_AUTOAVALIACAO, id, valor }),
    salvarAnotacaoFechamento: (id, patch) => despachar({ tipo: acao.SALVAR_ANOTACAO_FECHAMENTO, id, ...patch }),
    // `opcoes` (decisão 46, "Encerrar" v2): a escolha de mandar a mensagem de
    // encerramento, o texto já editado na hora, e os dois avisos simulados
    // (e-mail, SMS) lidos do cadastro. Tudo decidido na modal, o reducer só
    // grava.
    encerrarComResumo: (id, agora, opcoes = {}) => despachar({ tipo: acao.ENCERRAR_COM_RESUMO, id, agora, ...opcoes }),
  }
}
