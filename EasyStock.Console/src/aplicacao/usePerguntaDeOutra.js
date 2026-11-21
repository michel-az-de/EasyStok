import { useEffect } from 'react'
import { definirPerguntaDeOutra } from '../infra/api/conversasApi'

// #3037: liga a tela à pergunta "conversa de outra atendente" da camada de API (ATD-1). `perguntar(mensagem)`
// devolve uma Promise de boolean; enquanto o componente está montado, é ele quem pergunta.
export function usePerguntaDeOutra(perguntar) {
  useEffect(() => definirPerguntaDeOutra(perguntar), [perguntar])
}
