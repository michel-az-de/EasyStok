// Textos e contas do topo e das abas do celular (#2232, banca UX/UI). Puro: nada de React aqui, para
// a prova poder conferir cada rótulo sem montar a tela.
import { contarAtivas } from '../dominio/automacao'
import { fecharLojaPedeJustificativa } from '../dominio/aberturaDaLoja'

// L3: "Auto" sem estado era botão mudo. Com regras, diz "ligadas" ou "desligadas"; sem regras (modo
// API, F06) não há o que afirmar, então não diz nada em vez de inventar.
export const estadoDasAutomaticas = (regras) => (regras ? (contarAtivas(regras) > 0 ? 'ligadas' : 'desligadas') : null)

export function nomeDasAutomaticas(regras) {
  if (!regras) return 'Respostas automáticas'
  const n = contarAtivas(regras)
  return `Respostas automáticas, ${n} ${n === 1 ? 'ligada' : 'ligadas'}`
}

// L2: o número visível do contador entra no nome acessível, na mesma ordem em que é lido.
export const nomeComContador = (nome, quantidade, complemento = '') =>
  (quantidade > 0 ? `${nome}, ${quantidade}${complemento ? ` ${complemento}` : ''}` : nome)

// L11 e X3: a aba de baixo mostra só o que pede ação, o mesmo critério de "Precisa de você" do Balcão.
// Sem pendência, sem número. Os ids seguem os mesmos; só o rótulo diz o que a aba é para quem usa.
export function abasDoCelular(precisamDeVoce) {
  const n = precisamDeVoce > 0 ? precisamDeVoce : 0
  return [
    {
      id: 'balcao',
      rotulo: 'Conversas',
      contador: n > 0 ? n : undefined,
      tom: 'alerta',
      nome: nomeComContador('Conversas', n, n === 1 ? 'precisa de você' : 'precisam de você'),
    },
    { id: 'atendimento', rotulo: 'Mensagens' },
    { id: 'ficha', rotulo: 'Cliente' },
  ]
}

// L1: fechar a loja pede confirmação; abrir não. Dentro do horário, no modo API, o gesto de fechar já é
// um diálogo com justificativa (aplicacao/api/expediente.js), então não se pergunta duas vezes.
export const fechamentoPedeConfirmacao = ({ aberta, fonteApi, agora, funcionamento }) =>
  Boolean(aberta) && !(fonteApi && fecharLojaPedeJustificativa(agora, funcionamento))
