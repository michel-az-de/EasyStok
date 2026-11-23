import { useEffect, useMemo, useRef, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { Vazio } from '../../../componentes/Vazio'
import { CampoArea, CampoTexto } from '../../../componentes/Campo'
import { useAtendimento } from '../../../aplicacao/contextos'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { useMensagensPadraoGestao } from '../../../aplicacao/useMensagensPadraoGestao'
import { respostasEditaveis } from '../../../dominio/acessoModulos'
import { plural } from '../../../dominio/formato'
import {
  CABECALHO_MAXIMO, CORPO_MAXIMO, RODAPE_MAXIMO, SUGESTOES,
  acrescentarCampo, corpoParaApi, nomeTecnico, previaDoCorpo, temErro, tituloDoNome, validarMensagemPadrao,
} from './mensagemPadrao'
import css from './abaRespostas.module.css'

// #3206: mensagem padrão é o que dá para enviar quando o cliente ficou mais de 24 horas sem escrever. Idioma (pt_BR)
// e categoria (UTILITY) são fixos: não aparecem na tela.
function FormMensagemPadrao({ inicial, aoSalvar, aoCancelar }) {
  const [titulo, setTitulo] = useState(inicial ? tituloDoNome(inicial.nome) : '')
  const [cabecalho, setCabecalho] = useState(inicial?.cabecalho ?? '')
  const [corpo, setCorpo] = useState(inicial?.corpo ?? '')
  const [rodape, setRodape] = useState(inicial?.rodape ?? '')
  const [tentou, setTentou] = useState(false)
  const [salvando, setSalvando] = useState(false)
  const [erroDoServidor, setErroDoServidor] = useState('')
  const campos = { titulo, cabecalho, corpo, rodape }
  const erros = validarMensagemPadrao(campos)
  const ver = (campo) => (tentou ? erros[campo] : undefined)
  const nome = inicial && titulo === tituloDoNome(inicial.nome) ? inicial.nome : nomeTecnico(titulo)

  function sugerir(s) { setTitulo(s.titulo); setCorpo(s.corpo); setRodape(s.rodape); setCabecalho(''); setErroDoServidor('') }

  async function salvar(evento) {
    evento.preventDefault()
    if (salvando) return
    setTentou(true)
    if (temErro(erros)) return
    setSalvando(true); setErroDoServidor('')
    const retorno = await aoSalvar({ ...corpoParaApi(campos), nome })
    setSalvando(false)
    if (!retorno.ok) setErroDoServidor(retorno.erro)
  }

  return (
    <form className={css.form} onSubmit={salvar} noValidate aria-label={inicial ? 'Editar mensagem padrão' : 'Nova mensagem padrão'}>
      {!inicial && (
        <div className={css.sugestoes}>
          <p className={css.rotuloPrevia} id="mp-sugestoes">Sugestões prontas</p>
          <div className={css.acoes} role="group" aria-labelledby="mp-sugestoes">
            {SUGESTOES.map((s) => <Botao key={s.id} disabled={salvando} onClick={() => sugerir(s)}>{s.titulo}</Botao>)}
          </div>
        </div>
      )}
      <CampoTexto rotulo="Nome da mensagem" value={titulo} disabled={salvando || Boolean(inicial)} autoFocus
        erro={ver('titulo')} onChange={(e) => setTitulo(e.target.value)}
        dica={nome ? `Nome técnico: ${nome}` : 'O nome técnico aparece aqui.'} />
      <CampoTexto rotulo="Cabeçalho (opcional)" value={cabecalho} disabled={salvando} limite={CABECALHO_MAXIMO}
        erro={ver('cabecalho')} onChange={(e) => setCabecalho(e.target.value)} />
      <CampoArea rotulo="Texto da mensagem" rows={4} value={corpo} disabled={salvando} limite={CORPO_MAXIMO}
        erro={ver('corpo')} onChange={(e) => setCorpo(e.target.value)}
        dica="Use Inserir campo para o nome do cliente, o número do pedido e outros dados que mudam." />
      <div className={css.acoes}>
        <Botao icone="plus" disabled={salvando} onClick={() => setCorpo((c) => acrescentarCampo(c))}>Inserir campo</Botao>
      </div>
      <CampoTexto rotulo="Rodapé (opcional)" value={rodape} disabled={salvando} limite={RODAPE_MAXIMO}
        erro={ver('rodape')} onChange={(e) => setRodape(e.target.value)} />
      <div className={css.previaMensagem}>
        <p className={css.rotuloPrevia} id="mp-previa">Prévia, com exemplos no lugar dos campos</p>
        <div className={css.balao} role="group" aria-labelledby="mp-previa">
          {cabecalho.trim() && <strong>{previaDoCorpo(cabecalho)}</strong>}
          <span>{corpo.trim() ? previaDoCorpo(corpo) : 'O texto aparece aqui.'}</span>
          {rodape.trim() && <small>{previaDoCorpo(rodape)}</small>}
        </div>
      </div>
      {erroDoServidor && <p className={css.alerta} role="alert">{erroDoServidor}</p>}
      <div className={css.acoes}>
        <Botao variante="primario" tipo="submit" icone="check" disabled={salvando}>{salvando ? 'Salvando…' : 'Salvar'}</Botao>
        <Botao variante="texto" disabled={salvando} onClick={aoCancelar}>Cancelar</Botao>
      </div>
    </form>
  )
}

export function SecaoMensagensPadrao() {
  const { fonteApi } = useAtendimento()
  const { acoes } = useAcessoModulos()
  const editavel = respostasEditaveis({ fonteApi, acoes })
  const { itens, erroDaLista, criar, editar, arquivar } = useMensagensPadraoGestao(fonteApi)
  const [verArquivadas, setVerArquivadas] = useState(false)
  const [editando, setEditando] = useState(null)
  const [gravando, setGravando] = useState(null)
  const [aviso, setAviso] = useState('')
  const novaRef = useRef(null)
  const voltarFoco = useRef(false)

  useEffect(() => {
    if (editando === null && voltarFoco.current) { voltarFoco.current = false; novaRef.current?.focus() }
  }, [editando])

  const lista = useMemo(() => (itens ?? [])
    .filter((m) => verArquivadas || !m.arquivado)
    .sort((a, b) => Number(a.arquivado) - Number(b.arquivado) || a.nome.localeCompare(b.nome, 'pt-BR')), [itens, verArquivadas])
  const ativas = (itens ?? []).filter((m) => !m.arquivado).length

  if (!fonteApi) return null

  const fechar = () => { voltarFoco.current = true; setEditando(null) }
  async function alternar(m) {
    if (gravando) return
    setGravando(m.id); setAviso('')
    const retorno = await arquivar(m.id, !m.arquivado)
    setAviso(retorno.ok ? `Mensagem ${tituloDoNome(m.nome)} ${m.arquivado ? 'restaurada' : 'arquivada'}.` : retorno.erro)
    setGravando(null)
  }

  return (
    <section className={css.secao} aria-labelledby="gestao-mensagens-padrao">
      <header className={css.topo}>
        <div>
          <h3 id="gestao-mensagens-padrao">Mensagens padrão</h3>
          <p className={css.descricao}>É o que dá para enviar quando o cliente ficou mais de 24 horas sem escrever.</p>
          <p className={css.descricao}>{plural(ativas, 'mensagem ativa', 'mensagens ativas')}.</p>
          {!editavel && <p className={css.descricao}>Somente a gerência pode criar, editar e arquivar mensagens padrão.</p>}
        </div>
        {editavel && <Botao ref={novaRef} variante="primario" icone="plus" onClick={() => setEditando('nova')} disabled={editando !== null}>Nova mensagem padrão</Botao>}
      </header>

      <label className={css.chave}>
        <input type="checkbox" disabled={editando !== null} checked={verArquivadas} onChange={(e) => setVerArquivadas(e.target.checked)} />
        Mostrar arquivadas
      </label>
      <p className={css.descricao} role="status">{aviso}</p>
      {erroDaLista && <p className={css.alerta} role="alert">{erroDaLista}</p>}

      {editando === 'nova' && (
        <FormMensagemPadrao aoSalvar={async (dados) => { const r = await criar(dados); if (r.ok) fechar(); return r }} aoCancelar={fechar} />
      )}

      {itens !== null && lista.length === 0 ? (
        <Vazio icone="respostas" titulo="Nenhuma mensagem padrão ainda">
          {editavel ? 'Use Nova mensagem padrão para cadastrar a primeira, ou comece por uma sugestão pronta.' : 'Peça à gerência para cadastrar mensagens padrão.'}
        </Vazio>
      ) : (
        <ul className={css.lista}>
          {lista.map((m) => (
            <li key={m.id} className={`${css.linha} ${m.arquivado ? css.arquivada : ''}`}>
              {editando === m.id ? (
                <FormMensagemPadrao inicial={m}
                  aoSalvar={async (dados) => { const r = await editar(m.id, dados); if (r.ok) fechar(); return r }} aoCancelar={fechar} />
              ) : (
                <>
                  <div className={css.resumo}>
                    <span className={css.titulo}>{tituloDoNome(m.nome)}</span>
                    <span className={css.atalho}>{m.nome}</span>
                    {m.arquivado && <span className={css.selo}>Arquivada</span>}
                    <span className={css.texto}>{previaDoCorpo(m.corpo)}</span>
                  </div>
                  {editavel && <div className={css.acoesLinha}>
                    {!m.arquivado && <Botao variante="texto" icone="lapis" disabled={editando !== null || gravando !== null}
                      aria-label={`Editar mensagem padrão ${tituloDoNome(m.nome)}`} onClick={() => setEditando(m.id)}>Editar</Botao>}
                    <Botao variante="texto" icone={m.arquivado ? 'undo-2' : 'archive'} disabled={editando !== null || gravando !== null}
                      aria-label={`${m.arquivado ? 'Restaurar' : 'Arquivar'} mensagem padrão ${tituloDoNome(m.nome)}`} onClick={() => alternar(m)}>
                      {m.arquivado ? 'Restaurar' : 'Arquivar'}
                    </Botao>
                  </div>}
                </>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
