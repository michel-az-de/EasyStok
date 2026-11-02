// Anexo escolhido e ainda não enviado, por conversa (#2185 F9). O texto digitado já sobrevive à troca de
// conversa; a foto na prévia morria junto com o Composer (que remonta por conversa e ao girar o tablet).
// Só memória da página: a foto pesa, e quem recarrega a página escolhe de novo. A chave inclui quem está
// logado, para outra conta no mesmo aparelho não herdar a foto.
import { identidadeDaSessao } from '../infra/api/sessao'

const memoria = new Map()

export const chaveDoAnexo = (sessao, conversaId) => `${identidadeDaSessao(sessao)}:${conversaId}`

export const anexosEmEspera = {
  ler: (chave) => memoria.get(chave) ?? null,
  guardar(chave, anexo) {
    if (anexo) memoria.set(chave, anexo)
    else memoria.delete(chave)
  },
}
