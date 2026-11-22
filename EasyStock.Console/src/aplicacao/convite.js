// #2070: entrar com o Google na tela do link do convite. O convite se aceita entrando com a conta Google do e-mail
// convidado. Mesmo formato do `google` da TelaLogin, mas nunca entra sozinho: o aparelho da casa pode ter a conta
// Google de outra pessoa já escolhida.
import { sair } from '../infra/api/autenticacao'
import { esquecerRascunhos } from '../infra/api/rascunhosDaSessao'
import { identidadeDaSessao, lerSessao } from '../infra/api/sessao'
import { encerrarSessaoAnterior } from '../infra/api/usuariosApi'
import { esquecerVisitas } from '../infra/api/visitaDaSessao'
import { travarEntradaSozinha } from '../infra/entradaGoogle'

export function googleDoConvite(google) {
  if (!google) return null
  return {
    buscarClientId: google.buscarClientId,
    opcoes: () => ({ auto_select: false, oferecer: false }),
    // Devolve quem entrou. A API ainda não confere se a conta é a do convite, então a tela mostra antes de largá-lo.
    entrar: async (idToken) => {
      const anterior = lerSessao()
      await google.entrar(idToken)
      const atual = lerSessao()
      // Quem estava logado neste aparelho perdeu a sessão para a outra conta: ela sai também do servidor, com os
      // rascunhos e a visita dela (o "Sair" de sempre faz o mesmo).
      if (anterior && identidadeDaSessao(anterior) !== identidadeDaSessao(atual)) {
        esquecerRascunhos()
        esquecerVisitas()
        if (anterior.refreshToken) encerrarSessaoAnterior(anterior.refreshToken).catch(() => { /* sem rede, vale o vencimento */ })
      }
      return { nome: atual?.usuario?.nome ?? null, email: atual?.usuario?.email ?? null }
    },
    // "Não sou eu": sai da conta que entrou sem sair da tela do convite; o One Tap não a escolhe sozinho depois.
    sair: () => {
      travarEntradaSozinha()
      return sair()
    },
  }
}
