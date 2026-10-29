import { useRef, useState } from 'react'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { Botao } from '../../componentes/Botao'
import { IconeDoModulo } from '../../componentes/IconeDoModulo'
import { MarcaIcone } from '../../componentes/MarcaIcone'
import { Modal } from '../../componentes/Modal'
import { guiaDoConsole } from '../../dominio/guiaDoConsole'
import { HASH_HALL } from '../../dominio/rota'
import { hashDoModulo } from '../../dominio/modulos'
import { contextoDaAjuda, copiarDiagnostico, diagnosticoDoConsole } from './diagnosticoDoConsole'
import css from './ajuda.module.css'

export function AjudaDoConsole({ rota, fonteApi, aoFechar }) {
  const { permite } = useAcessoModulos()
  const [aviso, setAviso] = useState('')
  const [diagnostico, setDiagnostico] = useState('')
  const dadosRef = useRef(null)
  const contexto = contextoDaAjuda(rota, fonteApi)
  const guia = guiaDoConsole({ rota, fonteApi, permite })

  async function copiar() {
    const texto = diagnosticoDoConsole({
      rota, fonteApi,
      versao: document.querySelector('meta[name="easystok-console-sha"]')?.content,
      onLine: navigator.onLine,
    })
    setDiagnostico(texto)
    const copiado = await copiarDiagnostico(texto, navigator.clipboard)
    setAviso(copiado ? 'Diagnóstico copiado. Envie à equipe junto com sua dúvida.' : 'Não foi possível copiar. Copie os dados selecionados abaixo.')
    if (!copiado) requestAnimationFrame(() => { dadosRef.current?.focus(); dadosRef.current?.select() })
  }

  return (
    <Modal titulo="Ajuda" descricao={contexto.titulo} aoFechar={aoFechar} largura="620px"
      rodape={<Botao onClick={aoFechar}>Fechar ajuda</Botao>}>
      <div className={css.corpo}>
        <section aria-labelledby="ajuda-por-aqui">
          <h3 id="ajuda-por-aqui" className={css.titulo}><MarcaIcone nome="informacao" /> Como usar esta tela</h3>
          <ol className={css.passos}>{contexto.passos.map(passo => <li key={passo}>{passo}</li>)}</ol>
          <div className={css.caminhos}>
            <a href={HASH_HALL} onClick={aoFechar}><MarcaIcone nome="grade" tamanho={20} />Ver módulos</a>
            {permite('atendimento') && rota.tipo !== 'principal' && <a href={hashDoModulo('atendimento', 'balcao')} onClick={aoFechar}><MarcaIcone nome="mensagem" tamanho={20} />Voltar às conversas</a>}
            {contexto.atalho && permite(contexto.atalho.modulo) && <a href={hashDoModulo(contexto.atalho.modulo, contexto.atalho.tela)} onClick={aoFechar}><MarcaIcone nome="producao" tamanho={20} />{contexto.atalho.rotulo}</a>}
          </div>
        </section>
        <section aria-labelledby="ajuda-guia" className={css.guia}>
          <h3 id="ajuda-guia" className={css.titulo}><MarcaIcone nome="grade" /> Guia do sistema</h3>
          <p className={css.apoio}>O que cada módulo faz e por onde começar.</p>
          {guia.map(modulo => (
            <details key={modulo.id} className={css.modulo} open={modulo.aberto} data-guia-modulo={modulo.id}>
              <summary><IconeDoModulo modulo={modulo} />{modulo.nome}</summary>
              <p>{modulo.faz}</p>
              <h4 className={css.subtitulo}>Primeiros passos</h4>
              <ol className={css.passos}>{modulo.passos.map(passo => <li key={passo}>{passo}</li>)}</ol>
              <h4 className={css.subtitulo}>Telas</h4>
              <ul className={css.telas}>{modulo.telas.map(tela => <li key={tela.id}><a href={tela.href} onClick={aoFechar}>{tela.rotulo}</a></li>)}</ul>
            </details>
          ))}
        </section>
        <details className={css.detalhes}>
          <summary>Usar pelo teclado</summary>
          <p><kbd>Tab</kbd> e <kbd>Shift</kbd> + <kbd>Tab</kbd> percorrem os controles. <kbd>Enter</kbd> abre links; <kbd>Enter</kbd> ou <kbd>Espaço</kbd> aciona botões. <kbd>Esc</kbd> fecha a janela ou menu aberto.</p>
          {rota.tipo === 'principal' && <><p>No campo da mensagem, <kbd>/</kbd> busca respostas prontas. Nas abas do celular, as setas movem o foco; <kbd>Enter</kbd> ou <kbd>Espaço</kbd> escolhe a aba.</p><p>No Assistente, <kbd>Enter</kbd> cria uma nova linha; <kbd>Ctrl</kbd> + <kbd>Enter</kbd> faz a pergunta.</p></>}
        </details>
        <details className={`${css.detalhes} ${css.suporte}`}>
          <summary>Pedir ajuda à equipe</summary>
          <p>Antes de repetir uma ação, confira se ela foi concluída. Se precisar de ajuda, descreva o que tentou fazer e envie o diagnóstico à equipe.</p>
          <Botao icone="copy" onClick={copiar}>Copiar diagnóstico</Botao>
          <p className={css.apoio}>Só inclui tela, versão, origem dos dados e estado da conexão. Nada é enviado automaticamente.</p>
          <output className={css.aviso} aria-live="polite">{aviso}</output>
          {diagnostico && <label className={css.dados}>Dados para a equipe<textarea ref={dadosRef} readOnly value={diagnostico} rows={6} /></label>}
        </details>
      </div>
    </Modal>
  )
}
