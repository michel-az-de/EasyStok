import { obterConfiguracao, salvarConfiguracao } from '../../infra/api/configuracaoApi'

// Configuração do atendimento no modo API (F02, S08). Não passa pelo reducer: só a aba
// Atendimento da Gestão lê e grava, e ela guarda o formulário no próprio estado.
export function criarAcoesConfiguracaoApi() {
  return {
    carregarConfiguracao: () => obterConfiguracao(),
    salvarConfiguracao: (configuracao) => salvarConfiguracao(configuracao),
  }
}
