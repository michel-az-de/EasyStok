import { useState } from 'react'
import { Icone } from './Icone'
import { iniciaisDoNome } from './iniciaisDoNome'
import css from './Avatar.module.css'

// Foto real quando fornecida pelo chamador. Sem foto, ou se a URL falhar,
// mostra as iniciais; nunca busca retratos em serviços externos.

const MATIZES = [8, 32, 96, 150, 190, 262, 300, 338]

function matizDe(nome) {
  const soma = [...nome].reduce((acc, ch) => acc + ch.charCodeAt(0), 0)
  return MATIZES[soma % MATIZES.length]
}

export function Avatar({ nome, foto, tamanho = 'medio', selo }) {
  const nomeDoContato = String(nome ?? '').trim() || 'Contato'
  // Foto que não carrega (link expirado, CDN fora) volta para a inicial em vez
  // de deixar o ícone de imagem quebrada. Guarda QUAL endereço falhou: outra
  // foto (outra conversa) tenta de novo.
  const [falhou, setFalhou] = useState(null)
  const iniciais = iniciaisDoNome(nome)
  const estilo = { '--matiz': matizDe(nomeDoContato) }
  const mostrarFoto = Boolean(foto) && falhou !== foto
  return (
    <span className={`${css.envelope} ${css[tamanho]}`}>
      {mostrarFoto
        ? <img className={css.foto} src={foto} alt={'Foto de ' + nomeDoContato} onError={() => setFalhou(foto)} />
        : (
          <span className={css.inicial} style={estilo} aria-hidden="true">
            {/* #2258 (T8): sem letra no nome (só telefone), o ícone de pessoa no lugar do "?". */}
            {iniciais || <Icone nome="ficha" tamanho="1.1em" />}
          </span>
        )}
      {selo && <span className={css.selo} title={selo.titulo}>{selo.icone}</span>}
    </span>
  )
}
