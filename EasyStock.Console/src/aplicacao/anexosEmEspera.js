// Anexo escolhido e ainda não enviado, por conversa (#2185 F9). O texto digitado já sobrevive à troca de
// conversa; a foto na prévia morria junto com o Composer (que remonta por conversa e ao girar o tablet).
// Só memória da página: a foto pesa, e quem recarrega a página escolhe de novo. A chave inclui quem está
// logado, para outra conta no mesmo aparelho não herdar a foto.
import { EVENTO_SESSAO_ALTERADA, identidadeDaSessao, lerSessao } from '../infra/api/sessao'

const memoria = new Map()

export const chaveDoAnexo = (sessao, conversaId) => `${identidadeDaSessao(sessao)}:${conversaId}`

export const anexosEmEspera = {
  ler: (chave) => memoria.get(chave) ?? null,
  guardar(chave, anexo) {
    if (anexo) memoria.set(chave, anexo)
    else memoria.delete(chave)
  },
  // Conta que saiu não deixa foto de cliente na memória da página, como os rascunhos de nota.
  manterSo(identidade) {
    for (const chave of memoria.keys()) if (!chave.startsWith(`${identidade}:`)) memoria.delete(chave)
  },
}

globalThis.addEventListener?.(EVENTO_SESSAO_ALTERADA, () => anexosEmEspera.manterSo(identidadeDaSessao(lerSessao())))
