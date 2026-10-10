import { useEffect, useMemo, useRef, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { Vazio } from '../../../componentes/Vazio'
import { CampoArea, CampoTexto } from '../../../componentes/Campo'
import { useAcoes, useAtendimento, useCatalogo } from '../../../aplicacao/contextos'
import {
  VARIAVEIS_DA_API, avisoDaAutomaticaDeEntrada, variaveisForaDaApi,
} from '../../../dominio/automacao'
import { plural } from '../../../dominio/formato'
import { gerarAtalho, normalizarBusca } from '../../../dominio/respostas'
import css from './abaRespostas.module.css'

// Mesmo editor na janela contextual do Balcão e na rota direta de Atendimento.

// A ação do modo API devolve `false` quando não gravou (o aviso já foi para a faixa);
// a da demonstração não devolve nada. Nos dois casos sem `false`, o formulário fecha.
const depois = (resultado, fechar) => Promise.resolve(resultado).then((ok) => { if (ok !== false) fechar() })

const VARIAVEIS_TEXTO = VARIAVEIS_DA_API.map((v) => `{${v.chave}} ${v.descricao}`).join(' · ')

function FormResposta({ inicial, aoSalvar, aoCancelar }) {
  const [titulo, setTitulo] = useState(inicial?.titulo ?? '')
  const [atalho, setAtalho] = useState(inicial?.atalho ?? '')
  const [texto, setTexto] = useState(inicial?.texto ?? '')
  const [salvando, setSalvando] = useState(false)
  const pronto = titulo.trim() && texto.trim()

  function salvar(evento) {
    evento.preventDefault()
    if (!pronto) return
    setSalvando(true)
    const dados = { titulo: titulo.trim(), atalho: atalho.trim() || gerarAtalho(titulo), texto: texto.trim() }
    Promise.resolve(aoSalvar(dados)).finally(() => setSalvando(false))
  }

  return (
    <form className={css.form} onSubmit={salvar}>
      <div className={css.duas}>
        <CampoTexto rotulo="Título" value={titulo} maxLength={80} onChange={(e) => setTitulo(e.target.value)} autoFocus />
        <CampoTexto
          rotulo="Atalho"
          value={atalho}
          placeholder={titulo ? gerarAtalho(titulo) : '/pix'}
          onChange={(e) => setAtalho(e.target.value.replace(/\s/g, '-'))}
        />
      </div>
      <CampoArea rotulo="Texto" rows={3} value={texto} onChange={(e) => setTexto(e.target.value)} dica={VARIAVEIS_TEXTO} />
      <div className={css.acoes}>
        <Botao variante="primario" tipo="submit" icone="check" disabled={!pronto || salvando}>{salvando ? 'Salvando…' : 'Salvar'}</Botao>
        <Botao variante="texto" onClick={aoCancelar}>Cancelar</Botao>
      </div>
    </form>
  )
}

function SecaoRespostas() {
  const { respostasProntas } = useCatalogo()
  const { incluirRespostaPronta, editarRespostaPronta, alternarArquivamentoRespostaPronta } = useAcoes()
  const [busca, setBusca] = useState('')
  const buscaRef = useRef(null)
  const [verArquivadas, setVerArquivadas] = useState(false)
  // 'nova', o id da resposta em edição, ou null.
  const [editando, setEditando] = useState(null)

  const lista = useMemo(() => {
    const alvo = normalizarBusca(busca)
    return (respostasProntas ?? [])
      .filter((r) => verArquivadas || !r.arquivada)
      .filter((r) => !alvo || [r.titulo, r.atalho, r.texto].some((c) => normalizarBusca(c).includes(alvo)))
      .sort((a, b) => Number(a.arquivada) - Number(b.arquivada) || a.titulo.localeCompare(b.titulo, 'pt-BR'))
  }, [respostasProntas, busca, verArquivadas])
  const ativas = (respostasProntas ?? []).filter((r) => !r.arquivada).length

  return (
    <section className={css.secao} aria-labelledby="gestao-respostas">
      <header className={css.topo}>
        <div>
          <h3 id="gestao-respostas">Respostas prontas</h3>
          <p className={css.descricao}>{plural(ativas, 'ativa', 'ativas')}. No atendimento, digite / no campo ou toque em Respostas.</p>
        </div>
        <Botao variante="primario" icone="plus" onClick={() => setEditando('nova')} disabled={editando === 'nova'}>Nova resposta</Botao>
      </header>

      <div className={css.filtros}>
        <div className={css.filtroBusca}>
          <CampoTexto ref={buscaRef} tipo="search" rotulo="Buscar resposta pronta"
            placeholder="Título, atalho ou trecho da resposta" value={busca} onChange={(e) => setBusca(e.target.value)} />
          {busca && <div className={css.retornoBusca}>
            <output className={css.descricao}>{plural(lista.length, 'resposta encontrada', 'respostas encontradas')}</output>
            <Botao variante="texto" icone="x" onClick={() => { setBusca(''); buscaRef.current?.focus() }}>Limpar busca</Botao>
          </div>}
        </div>
        <label className={css.chave}>
          <input type="checkbox" checked={verArquivadas} onChange={(e) => setVerArquivadas(e.target.checked)} />
          Mostrar arquivadas
        </label>
      </div>

      {editando === 'nova' && (
        <FormResposta
          aoSalvar={(dados) => depois(incluirRespostaPronta(dados), () => setEditando(null))}
          aoCancelar={() => setEditando(null)}
        />
      )}

      {lista.length === 0 ? (
        <Vazio icone={busca ? 'search' : 'respostas'} titulo={busca ? 'Nada com essa busca.' : 'Nenhuma resposta pronta ainda.'}>
          {busca ? 'Tente outro título, atalho ou trecho da resposta.' : 'Use Nova resposta para guardar as mensagens que você envia com frequência.'}
        </Vazio>
      ) : (
        <ul className={css.lista}>
          {lista.map((r) => (
            <li key={r.id} className={`${css.linha} ${r.arquivada ? css.arquivada : ''}`}>
              {editando === r.id ? (
                <FormResposta
                  inicial={r}
                  aoSalvar={(dados) => depois(editarRespostaPronta(r.id, dados), () => setEditando(null))}
                  aoCancelar={() => setEditando(null)}
                />
              ) : (
                <>
                  <div className={css.resumo}>
                    <span className={css.titulo}>{r.titulo}</span>
                    <span className={css.atalho}>{r.atalho}</span>
                    {r.arquivada && <span className={css.marca}>arquivada</span>}
                    <span className={css.texto}>{r.texto}</span>
                  </div>
                  <div className={css.acoesLinha}>
                    {!r.arquivada && <Botao variante="texto" icone="lapis" onClick={() => setEditando(r.id)}>Editar</Botao>}
                    <Botao variante="texto" icone={r.arquivada ? 'undo-2' : 'archive'} onClick={() => alternarArquivamentoRespostaPronta(r.id)}>
                      {r.arquivada ? 'Restaurar' : 'Arquivar'}
                    </Botao>
                  </div>
                </>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

function LinhaAutomatica({ regra, fonteApi, emFoco }) {
  const { alternarRegra, editarRegra } = useAcoes()
  const linhaRef = useRef(null)
  const [rascunho, setRascunho] = useState(null)
  const [salvando, setSalvando] = useState(false)
  const editando = rascunho !== null
  const foraDaApi = fonteApi ? variaveisForaDaApi(rascunho ?? regra.texto) : []
  // #1474: no modo API a saudação de Horários e mensagens sai sempre na primeira mensagem.
  const avisoEntrada = fonteApi ? avisoDaAutomaticaDeEntrada(regra) : null

  useEffect(() => {
    if (!emFoco) return
    const quadro = requestAnimationFrame(() => {
      linhaRef.current?.focus({ preventScroll: true })
      linhaRef.current?.scrollIntoView({ block: 'center' })
    })
    return () => cancelAnimationFrame(quadro)
  }, [emFoco])

  function salvar() {
    setSalvando(true)
    depois(editarRegra(regra.id, { texto: rascunho.trim() }), () => setRascunho(null)).finally(() => setSalvando(false))
  }

  return (
    <li ref={linhaRef} tabIndex={emFoco ? -1 : undefined}
      className={`${css.linha} ${regra.ativa ? '' : css.desligada} ${emFoco ? css.emFoco : ''}`}>
      <div className={css.resumo}>
        <span className={css.titulo}>{regra.nome}</span>
        <label className={`${css.chave} ${css.chaveLinha}`}>
          <input
            type="checkbox"
            aria-label={`${regra.nome}, ${regra.ativa ? 'ligada' : 'desligada'}`}
            checked={regra.ativa}
            onChange={() => alternarRegra(regra.id)}
          />
          {regra.ativa ? 'Ligada' : 'Desligada'}
        </label>
        <span className={css.quando}>{regra.descricao}</span>
        {!editando && (
          regra.texto
            ? <span className={css.textoInteiro}>{regra.texto}</span>
            : !avisoEntrada && <span className={css.semTexto}>Sem texto: não sai nada para o cliente.</span>
        )}
        {avisoEntrada && <span className={css.semTexto}>{avisoEntrada}</span>}
        {foraDaApi.length > 0 && (
          <span className={css.alerta} role={editando ? 'alert' : undefined}>
            {foraDaApi.map((v) => `{${v}}`).join(', ')} não é preenchida pelo EasyStok e sai escrita assim para o cliente.
          </span>
        )}
      </div>

      {editando ? (
        <div className={css.form}>
          <CampoArea
            rotulo={`Texto de ${regra.nome}`}
            rotuloOculto
            rows={3}
            value={rascunho}
            onChange={(e) => setRascunho(e.target.value)}
            dica={fonteApi ? VARIAVEIS_TEXTO : null}
            autoFocus
          />
          <div className={css.acoes}>
            <Botao variante="primario" icone="check" disabled={!rascunho.trim() || salvando} onClick={salvar}>{salvando ? 'Salvando…' : 'Salvar'}</Botao>
            <Botao variante="texto" onClick={() => setRascunho(null)}>Cancelar</Botao>
          </div>
        </div>
      ) : (
        <div className={css.acoesLinha}>
          <Botao variante="texto" icone="lapis" onClick={() => setRascunho(regra.texto || regra.sugestao || '')}>
            {regra.texto ? 'Editar texto' : 'Escrever texto'}
          </Botao>
        </div>
      )}
    </li>
  )
}

function SecaoAutomaticas({ focoInicial }) {
  const { regras, fonteApi, cargaDasRegras } = useAtendimento()
  // #1474: lista vazia não é "carregando": separa a espera, o erro e o vazio de verdade.
  const vazio = cargaDasRegras?.estado === 'carregando'
    ? 'Carregando as automáticas do EasyStok…'
    : cargaDasRegras?.estado === 'erro'
      ? `As automáticas não carregaram: ${cargaDasRegras.mensagem ?? 'tente de novo.'}`
      : 'Nenhuma automática.'
  return (
    <section className={css.secao} aria-labelledby="gestao-automaticas">
      <header className={css.topo}>
        <div>
          <h3 id="gestao-automaticas">Mensagens automáticas</h3>
          <p className={css.descricao}>
            Saem sozinhas, só até 24 h depois da última mensagem do cliente e nunca para cliente bloqueado.
            Na conversa aparecem com a etiqueta automática.
          </p>
        </div>
      </header>
      {regras.length === 0 ? (
        <p className={css.vazio} role={cargaDasRegras?.estado === 'erro' ? 'alert' : undefined}>{vazio}</p>
      ) : (
        <ul className={css.lista}>
          {regras.map((r) => <LinhaAutomatica key={r.id} regra={r} fonteApi={fonteApi} emFoco={focoInicial === `regra-${r.id}`} />)}
        </ul>
      )}
    </section>
  )
}

export function AbaRespostas({ focoInicial = null }) {
  return (
    <div className={css.aba}>
      <SecaoRespostas />
      <SecaoAutomaticas focoInicial={focoInicial} />
    </div>
  )
}
