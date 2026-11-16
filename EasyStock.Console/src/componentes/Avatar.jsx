import { useState } from 'react'
import { Icone } from './Icone'
import { iniciaisDoNome } from './iniciaisDoNome'
import css from './Avatar.module.css'

// Foto real quando fornecida pelo chamador. Sem foto, ou se a URL falhar,
// mostra as iniciais; nunca busca retratos em serviços externos.

export function Avatar({ nome, foto, tamanho = 'medio', selo }) {
  const nomeDoContato = String(nome ?? '').trim() || 'Contato'
  // Foto que não carrega (link expirado, CDN fora) volta para a inicial em vez
  // de deixar o ícone de imagem quebrada. Guarda QUAL endereço falhou: outra
  // foto (outra conversa) tenta de novo.
  const [falhou, setFalhou] = useState(null)
  const iniciais = iniciaisDoNome(nome)
  const mostrarFoto = Boolean(foto) && falhou !== foto
  return (
    <span className={`${css.envelope} ${css[tamanho]}`}>
      {mostrarFoto
        ? <img className={css.foto} src={foto} alt={'Foto de ' + nomeDoContato} onError={() => setFalhou(foto)} />
        : (
          <span className={selo ? `${css.inicial} ${css.inicialComSelo}` : css.inicial} aria-hidden="true">
            {/* #2258 (T8): sem letra no nome (só telefone), o ícone de pessoa no lugar do "?". */}
            {iniciais || <Icone nome="ficha" tamanho={20} />}
          </span>
        )}
      {selo && <span className={css.selo} title={selo.titulo}>{selo.icone}</span>}
    </span>
  )
}
