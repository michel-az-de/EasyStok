import { useEffect, useMemo, useRef, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { BotaoIcone } from '../../../componentes/BotaoIcone'
import { CampoTexto } from '../../../componentes/Campo'
import {
  DICIONARIO_SIGNIFICADO_MAXIMO, DICIONARIO_TERMO_MAXIMO, DICIONARIO_TERMOS_MAXIMO,
  dicionarioParaSalvar, erroDoDicionario, mesmoDicionario,
} from '../../../dominio/dicionarioDaLoja'
import { Retorno } from './Retorno'
import css from './painelAssistente.module.css'

// #3149: o dicionário da loja. Ensina ao Assistente as palavras da loja e da clientela. Só quem administra a loja
// lê e muda (a leitura é de quem administra); o rascunho vive aqui e se perde ao trocar de aba, como o do roteiro.
export function AbaDicionario({ podeEditar, painel, dicionarioDaLoja }) {
  if (!podeEditar) return <p className={css.nota}>Só quem administra a loja vê e muda o dicionário.</p>
  if (dicionarioDaLoja.erro && !dicionarioDaLoja.dicionario) {
    return (
      <div className={css.acoes}>
        <p role="alert" className={css.erro}>Não consegui ler o dicionário: {dicionarioDaLoja.erro}</p>
        <Botao icone="refresh-cw" onClick={dicionarioDaLoja.recarregar}>Tentar de novo</Botao>
      </div>
    )
  }
  if (!dicionarioDaLoja.dicionario) return <output className={css.nota}>Carregando…</output>
  return <EditorDoDicionario key={dicionarioDaLoja.leitura} salvo={dicionarioDaLoja.dicionario} painel={painel} recarregar={dicionarioDaLoja.recarregar} />
}

// Chave só da lista na tela (a API não a conhece).
let proximaChave = 0
const comChave = (linhas) => linhas.map((l) => ({ chave: proximaChave++, termo: l.termo, significado: l.significado }))

function EditorDoDicionario({ salvo, painel, recarregar }) {
  const [linhas, setLinhas] = useState(() => comChave(salvo))
  const lista = useRef(null)
  const focarNovo = useRef(false)
  const erro = useMemo(() => erroDoDicionario(linhas), [linhas])
  const mudou = !mesmoDicionario(linhas, salvo)
  const salvando = painel.gravacao.salvando === 'dicionario'

  useEffect(() => {
    if (!focarNovo.current) return
    focarNovo.current = false
    lista.current?.querySelector('li:last-child [data-campo="termo"]')?.focus()
  }, [linhas.length])

  const mudar = (chave, campos) => setLinhas(linhas.map((l) => (l.chave === chave ? { ...l, ...campos } : l)))
  const acrescentar = () => { focarNovo.current = true; setLinhas([...linhas, ...comChave([{ termo: '', significado: '' }])]) }
  const gravar = async () => { if (await painel.salvar('dicionario', { dicionario: dicionarioParaSalvar(linhas) })) recarregar() }

  return (
    <div className={css.dicionario}>
      <p className={css.nota}>
        Ensine ao Assistente as palavras da sua loja e da sua clientela, para ele entender o pedido do jeito que a pessoa fala.
        Exemplo: termo "PF", quer dizer "prato feito do dia".
      </p>
      {linhas.length === 0 && <p className={css.nota}>Nenhum termo ainda. Acrescente o primeiro.</p>}
      {linhas.length > 0 && (
        <ul className={css.termos} ref={lista}>
          {linhas.map((l, i) => (
            <li key={l.chave} className={css.termo}>
              <CampoTexto rotulo="Termo" value={l.termo} limite={DICIONARIO_TERMO_MAXIMO} data-campo="termo" onChange={(e) => mudar(l.chave, { termo: e.target.value })} />
              <CampoTexto rotulo="O que quer dizer" value={l.significado} limite={DICIONARIO_SIGNIFICADO_MAXIMO} onChange={(e) => mudar(l.chave, { significado: e.target.value })} />
              <BotaoIcone tom="secundario" icone="lixeira" rotulo={l.termo.trim() ? `Tirar o termo ${l.termo.trim()}` : `Tirar a linha ${i + 1}`}
                onClick={() => setLinhas(linhas.filter((x) => x.chave !== l.chave))} />
            </li>
          ))}
        </ul>
      )}
      <div className={css.acoes}>
        {linhas.length < DICIONARIO_TERMOS_MAXIMO && <Botao icone="plus" onClick={acrescentar}>Acrescentar termo</Botao>}
        <span className={css.nota}>{linhas.length} de {DICIONARIO_TERMOS_MAXIMO} termos</span>
      </div>
      {erro && <p role="alert" className={css.erro}>{erro}</p>}
      <div className={css.acoes}>
        {mudou && <Botao variante="primario" disabled={Boolean(erro) || salvando} onClick={gravar}>Salvar dicionário</Botao>}
        {mudou && <Botao variante="secundario" onClick={() => setLinhas(comChave(salvo))}>Desfazer</Botao>}
        <Retorno gravacao={painel.gravacao} qual="dicionario" />
      </div>
      {mudou && <p className={css.pendencia}>Dicionário com mudanças não salvas.</p>}
    </div>
  )
}
