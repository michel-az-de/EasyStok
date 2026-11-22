import { useEffect, useMemo, useRef, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { BotaoIcone } from '../../../componentes/BotaoIcone'
import { CampoTexto } from '../../../componentes/Campo'
import {
  DICIONARIO_SIGNIFICADO_MAXIMO, DICIONARIO_TERMO_MAXIMO, DICIONARIO_TERMOS_MAXIMO,
  dicionarioParaSalvar, erroDoDicionario, mesmoDicionario, temLinhaIncompleta,
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
  return <EditorDoDicionario salvo={dicionarioDaLoja.dicionario} painel={painel} definir={dicionarioDaLoja.definir} />
}

// Chave só da lista na tela (a API não a conhece).
let proximaChave = 0
const comChave = (linhas) => linhas.map((l) => ({ chave: proximaChave++, termo: l.termo, significado: l.significado }))

// Depois de salvar, o valor gravado vira o `salvo` (sem nova leitura). `foco` guarda para onde o foco vai quando a
// linha some ou a ação termina: 'novo' (termo da última linha), uma chave de linha ou 'acrescentar'.
function EditorDoDicionario({ salvo, painel, definir }) {
  const [linhas, setLinhas] = useState(() => comChave(salvo))
  const lista = useRef(null)
  const acrescentarRef = useRef(null)
  const foco = useRef(null)
  const incompleta = temLinhaIncompleta(linhas)
  const erro = useMemo(() => (incompleta ? null : erroDoDicionario(linhas)), [linhas, incompleta])
  const mudou = !mesmoDicionario(linhas, salvo)
  const salvando = painel.gravacao.salvando === 'dicionario'

  useEffect(() => {
    const alvo = foco.current
    if (alvo === null || salvando) return
    foco.current = null
    const campo = alvo === 'novo' ? lista.current?.querySelector('li:last-child [data-campo="termo"]')
      : alvo === 'acrescentar' ? acrescentarRef.current
        : lista.current?.querySelector(`[data-chave="${alvo}"]`)
    campo?.focus()
  })

  const mudar = (chave, campos) => setLinhas(linhas.map((l) => (l.chave === chave ? { ...l, ...campos } : l)))
  const acrescentar = () => { foco.current = 'novo'; setLinhas([...linhas, ...comChave([{ termo: '', significado: '' }])]) }
  const tirar = (chave) => {
    const resto = linhas.filter((l) => l.chave !== chave)
    const indice = linhas.findIndex((l) => l.chave === chave)
    foco.current = resto.length ? resto[Math.min(indice, resto.length - 1)].chave : 'acrescentar'
    setLinhas(resto)
  }
  const desfazer = () => { foco.current = 'acrescentar'; setLinhas(comChave(salvo)) }
  const gravar = async () => {
    const aparada = dicionarioParaSalvar(linhas)
    if (!(await painel.salvar('dicionario', { dicionario: aparada }))) return
    foco.current = 'acrescentar'
    definir(aparada)
    setLinhas(comChave(aparada))
  }

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
              <CampoTexto rotulo="Termo" value={l.termo} limite={DICIONARIO_TERMO_MAXIMO} data-campo="termo" data-chave={l.chave} disabled={salvando} onChange={(e) => mudar(l.chave, { termo: e.target.value })} />
              <CampoTexto rotulo="O que quer dizer" value={l.significado} limite={DICIONARIO_SIGNIFICADO_MAXIMO} disabled={salvando} onChange={(e) => mudar(l.chave, { significado: e.target.value })} />
              <BotaoIcone tom="secundario" icone="lixeira" rotulo={l.termo.trim() ? `Tirar o termo ${l.termo.trim()}` : `Tirar a linha ${i + 1}`}
                disabled={salvando} onClick={() => tirar(l.chave)} />
            </li>
          ))}
        </ul>
      )}
      <div className={css.acoes}>
        {linhas.length < DICIONARIO_TERMOS_MAXIMO && <Botao ref={acrescentarRef} icone="plus" disabled={salvando} onClick={acrescentar}>Acrescentar termo</Botao>}
        <span className={css.nota}>{linhas.length} de {DICIONARIO_TERMOS_MAXIMO} termos</span>
      </div>
      {incompleta && <p className={css.nota}>Preencha o termo e o que ele quer dizer em todas as linhas para salvar.</p>}
      {erro && <p role="alert" className={css.erro}>{erro}</p>}
      <div className={css.acoes}>
        {mudou && <Botao variante="primario" disabled={incompleta || Boolean(erro) || salvando} onClick={gravar}>Salvar dicionário</Botao>}
        {mudou && <Botao variante="secundario" disabled={salvando} onClick={desfazer}>Desfazer</Botao>}
        <Retorno gravacao={painel.gravacao} qual="dicionario" />
      </div>
      {mudou && <p className={css.pendencia}>Dicionário com mudanças não salvas.</p>}
    </div>
  )
}
