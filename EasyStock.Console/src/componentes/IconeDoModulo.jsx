import { Icone } from './Icone'
import { MarcaIcone } from './MarcaIcone'
import { iconeDaTela, iconeDoModulo, partirIcone, tamanhoMinimoDoIcone } from './iconesDoConsole'

// #2258 (A1): o mapa de ícones mora em `componentes/iconesDoConsole.js`; aqui só se desenha.
export function IconeDoModulo({ modulo, telaId, tamanho = 24 }) {
  const especificacao = telaId ? iconeDaTela(modulo.id, telaId) : iconeDoModulo(modulo.id)
  const icone = partirIcone(especificacao)
  if (!icone) return <Icone nome={modulo.icone} tamanho={tamanho} />
  const medida = Math.max(tamanho, tamanhoMinimoDoIcone(especificacao))
  return icone.familia === 'marca'
    ? <MarcaIcone nome={icone.nome} tamanho={medida} />
    : <Icone nome={icone.nome} tamanho={medida === 32 ? 32 : tamanho} />
}
