// Mídia da conversa no modo API (#1287): foto, áudio ou arquivo que chegou pelo canal.
// O arquivo vem do endpoint autenticado do EasyStok (`useMidiaDaMensagem`); enquanto
// não chega, ou se falhar, a bolha mostra o rótulo ("Foto", "Áudio", "Arquivo").
// Se o EasyStok não conseguiu baixar o anexo do WhatsApp (#1397), a bolha diz isso.
// #1577: o arquivo só é pedido quando o balão chega a uma tela de distância da área visível;
// abrir a conversa baixava a mídia do fio inteiro, até a que ninguém ia ver.
import { useRef } from 'react'
import { Icone } from '../../componentes/Icone'
import { useChegouNaTela } from '../../hooks/useChegouNaTela'
import { useMidiaDaMensagem } from '../../aplicacao/useMidiaDaMensagem'
import css from './atendimento.module.css'
import cssAnexo from './bolhaAnexo.module.css'

export function BolhaMidiaApi({ mensagem }) {
  const falhou = mensagem.midia?.falhou === true
  const rotuloRef = useRef(null)
  const naTela = useChegouNaTela(rotuloRef)
  const { url, erro } = useMidiaDaMensagem(falhou ? null : mensagem.midia, naTela)
  const mime = mensagem.midia?.mime ?? ''

  if (falhou) {
    return <p title={mensagem.midia.erro ?? undefined}>{`${mensagem.texto} (não foi possível baixar o anexo)`}</p>
  }
  if (!url) {
    return <p ref={rotuloRef}>{erro ? `${mensagem.texto} (não abriu: ${erro})` : mensagem.texto}</p>
  }
  if (mime.startsWith('image/')) {
    return (
      <figure className={css.figura}>
        <img src={url} alt={mensagem.texto} />
        <figcaption>{mensagem.texto}</figcaption>
      </figure>
    )
  }
  if (mime.startsWith('audio/')) {
    // Áudio de voz do WhatsApp não tem legenda; a trilha vazia só cumpre o contrato do elemento.
    // #1398: a transcrição, quando já chegou, aparece logo abaixo do player.
    return (
      <>
        <audio controls src={url} aria-label="Áudio do cliente">
          <track kind="captions" />
        </audio>
        {mensagem.transcricao && <p className={css.transcricao}>{mensagem.transcricao}</p>}
      </>
    )
  }
  return (
    <a className={cssAnexo.arquivo} href={url} download>
      <span className={cssAnexo.arquivoIcone} aria-hidden="true"><Icone nome="nota" /></span>
      <span className={cssAnexo.arquivoTexto}><strong>{mensagem.texto}</strong></span>
    </a>
  )
}
