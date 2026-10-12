import { useId, useRef, useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { CampoSelecao, CampoTexto } from '../../componentes/Campo'
import { useCadastroEntregaApi } from '../../aplicacao/useCadastroEntregaApi'
import { faixaDeHorarios } from '../../dominio/entrega'
import { DIAS_DA_SEMANA, corpoBloqueio, corpoZona } from '../../dominio/entregasApi'
import { dataCurtaComSemana, faixaDeCep, moeda } from '../../dominio/formato'
import { JanelasDaLoja } from './JanelasDaLoja'
import css from './entregasApi.module.css'
import layout from './cadastroEntrega.module.css'

// Cadastro de entrega da loja (S45, `api/minha-vitrine/entrega`): janelas com
// capacidade, zonas de frete por CEP ou bairros e bloqueios de dia ou janela. Montado em
// Entregas › Janelas e frete e, pelo App, em Gestão › Janelas de entrega (#1440).
const rotuloDaJanela = (x) =>
  `${DIAS_DA_SEMANA[x.diaDaSemana]} ${faixaDeHorarios(String(x.horaInicio).slice(0, 5), String(x.horaFim).slice(0, 5))}`
const ZONA_VAZIA = { label: '', valor: '', tempoEstimadoMinutos: '40', ordem: '1', cobertura: 'bairros', cepInicio: '', cepFim: '', bairros: '' }
const BLOQUEIO_VAZIO = { data: '', motivo: '', janelaEspecificaId: '' }

function useFormulario(inicial, ocupado) {
  const [form, setForm] = useState(inicial)
  const campo = (k) => ({ value: form[k], disabled: ocupado, onChange: (e) => setForm({ ...form, [k]: e.target.value }) })
  return { form, campo, limpar: () => setForm(inicial) }
}

export function CadastroEntregaApi() {
  const { janelas, zonas, bloqueios, erro, enviando, acoes, recarregar, limparErro } = useCadastroEntregaApi()
  const z = useFormulario(ZONA_VAZIA, enviando)
  const b = useFormulario(BLOQUEIO_VAZIO, enviando)
  // #1584: remover o bloqueio reabre a data para pedidos; confirma em linha, como "Excluir janela".
  const [removendo, setRemovendo] = useState(null)
  const [aba, setAba] = useState('janelas')
  const [criandoZona, setCriandoZona] = useState(false)
  const [criandoBloqueio, setCriandoBloqueio] = useState(false)
  const id = useId()
  const abasRef = useRef({})
  const zonaRef = useRef(null)
  const bloqueioRef = useRef(null)
  const enviar = (e, acao, f, corpo, aoConcluir) => {
    e.preventDefault()
    acao(corpo(f.form)).then((ok) => { if (ok) { f.limpar(); aoConcluir() } })
  }

  if (janelas === null && !erro) return <p className={css.vazio}>Carregando o cadastro…</p>
  const aviso = erro && (
    <div className={`${css.faixa} ${css.faixaFixa}`} role="alert">
      <Icone nome="circle-x" tamanho={16} /> <span className={css.cresce}>{erro}</span>
      <Botao variante="secundario" disabled={enviando} onClick={recarregar}>Atualizar cadastro</Botao>
      {janelas !== null && <Botao variante="texto" onClick={limparErro}>Fechar aviso</Botao>}
    </div>
  )
  if (janelas === null) return aviso
  const rotuloJanelaPorId = (id) => {
    const janela = (janelas ?? []).find((w) => w.id === id)
    return janela ? rotuloDaJanela(janela) : 'Janela excluída'
  }
  const opcoesJanela = [{ valor: '', rotulo: 'O dia inteiro' }, ...(janelas ?? []).map((x) => ({ valor: x.id, rotulo: rotuloDaJanela(x) }))]
  const abas = [['janelas', 'Horários', janelas.length], ['zonas', 'Frete', zonas.length], ['bloqueios', 'Bloqueios', bloqueios.length]]
  const trocarAba = (chave) => { setAba(chave); abasRef.current[chave]?.focus() }
  const navegar = (e, indice) => {
    const destino = { ArrowRight: (indice + 1) % abas.length, ArrowLeft: (indice + abas.length - 1) % abas.length, Home: 0, End: abas.length - 1 }[e.key]
    if (destino === undefined) return
    e.preventDefault()
    trocarAba(abas[destino][0])
  }
  const fecharZona = () => { setCriandoZona(false); requestAnimationFrame(() => zonaRef.current?.focus()) }
  const fecharBloqueio = () => { setCriandoBloqueio(false); requestAnimationFrame(() => bloqueioRef.current?.focus()) }

  return (
    <div className={`${css.cadastro} ${layout.cadastro}`}>
      {aviso}
      <div className={layout.abas} role="tablist" aria-label="Cadastro de entrega">
        {abas.map(([chave, rotulo, quantidade], indice) => (
          <Botao key={chave} ref={(el) => { abasRef.current[chave] = el }} variante="texto"
            id={`${id}-${chave}`} role="tab" aria-selected={aba === chave} aria-controls={`${id}-painel-${chave}`}
            tabIndex={aba === chave ? 0 : -1} onKeyDown={(e) => navegar(e, indice)} onClick={() => trocarAba(chave)}>
            {rotulo} <span className={layout.contagem}>{quantidade}</span>
          </Botao>
        ))}
      </div>
      <div id={`${id}-painel-janelas`} role="tabpanel" aria-labelledby={`${id}-janelas`} hidden={aba !== 'janelas'} tabIndex={0}>
        <JanelasDaLoja janelas={janelas} acoes={acoes} ocupado={enviando} />
      </div>

      <section id={`${id}-painel-zonas`} className={css.secao} role="tabpanel" aria-labelledby={`${id}-zonas`} hidden={aba !== 'zonas'} tabIndex={0}>
        <div className={layout.cabecalho}>
          <div><h2>Zonas de frete</h2><p className={css.apoio}>Valor e prazo por bairro ou faixa de CEP.</p></div>
          <Botao ref={zonaRef} variante="primario" icone="plus" disabled={enviando} aria-expanded={criandoZona} aria-controls={`${id}-zona-nova`} onClick={() => setCriandoZona(true)}>Nova zona</Botao>
        </div>
        <form id={`${id}-zona-nova`} hidden={!criandoZona} className={`${css.formulario} ${layout.formularioZona}`} onSubmit={(e) => enviar(e, acoes.criarZona, z, corpoZona, fecharZona)}>
          <CampoTexto rotulo="Nome" {...z.campo('label')} required />
          <CampoTexto rotulo="Valor (R$)" inputMode="decimal" {...z.campo('valor')} required />
          <CampoTexto rotulo="Tempo (min)" tipo="number" min="1" {...z.campo('tempoEstimadoMinutos')} required />
          <CampoTexto rotulo="Ordem" tipo="number" min="0" {...z.campo('ordem')} />
          <CampoSelecao
            rotulo="Cobertura" opcoes={[{ valor: 'bairros', rotulo: 'Bairros' }, { valor: 'cep', rotulo: 'Faixa de CEP' }]}
            {...z.campo('cobertura')}
          />
          <div className={layout.cobertura}>
          {z.form.cobertura === 'cep'
            ? (
              <>
                <CampoTexto rotulo="CEP inicial" inputMode="numeric" {...z.campo('cepInicio')} required />
                <CampoTexto rotulo="CEP final" inputMode="numeric" {...z.campo('cepFim')} required />
              </>
            )
            : <CampoTexto rotulo="Bairros (separados por vírgula)" {...z.campo('bairros')} required />}
          </div>
          <div className={layout.acoesFormulario}>
            <Botao tipo="submit" variante="primario" disabled={enviando}>Criar zona</Botao>
            <Botao variante="texto" disabled={enviando} onClick={fecharZona}>Cancelar</Botao>
          </div>
        </form>
        <ul className={css.lista}>
          {zonas.map((x) => (
            <li key={x.id} className={`${css.cartao} ${layout.registro}`}>
              <div className={layout.identidade}><strong>{x.label}</strong><span className={css.apoio}>
                {x.cepInicio ? `CEP ${faixaDeCep(x.cepInicio, x.cepFim)}` : x.bairros.join(', ')}{x.ativa ? '' : ' · desativada'}
              </span></div>
              <div className={layout.valor}><strong>{moeda(Number(x.valor) || 0)}</strong><span className={css.apoio}>{x.tempoEstimadoMinutos} min</span></div>
              <Botao variante="texto" disabled={enviando} aria-label={`${x.ativa ? 'Desativar' : 'Ativar'} zona ${x.label}`} onClick={() => acoes.alternarZona(x)}>{x.ativa ? 'Desativar' : 'Ativar'}</Botao>
            </li>
          ))}
        </ul>
        {zonas.length === 0 && <p className={layout.vazio}>Nenhuma zona cadastrada. Crie uma zona para definir o frete.</p>}
      </section>

      <section id={`${id}-painel-bloqueios`} className={css.secao} role="tabpanel" aria-labelledby={`${id}-bloqueios`} hidden={aba !== 'bloqueios'} tabIndex={0}>
        <div className={layout.cabecalho}>
          <div><h2>Bloqueios</h2><p className={css.apoio}>Fechamentos de dia ou horário nos próximos 60 dias.</p></div>
          <Botao ref={bloqueioRef} variante="primario" icone="plus" disabled={enviando} aria-expanded={criandoBloqueio} aria-controls={`${id}-bloqueio-novo`} onClick={() => setCriandoBloqueio(true)}>Novo bloqueio</Botao>
        </div>
        <form id={`${id}-bloqueio-novo`} hidden={!criandoBloqueio} className={`${css.formulario} ${layout.formularioBloqueio}`} onSubmit={(e) => enviar(e, acoes.criarBloqueio, b, corpoBloqueio, fecharBloqueio)}>
          <CampoTexto rotulo="Data" tipo="date" {...b.campo('data')} required />
          <CampoSelecao rotulo="Janela" opcoes={opcoesJanela} {...b.campo('janelaEspecificaId')} />
          <CampoTexto rotulo="Motivo" {...b.campo('motivo')} required />
          <div className={layout.acoesFormulario}>
            <Botao tipo="submit" variante="primario" disabled={enviando}>Bloquear</Botao>
            <Botao variante="texto" disabled={enviando} onClick={fecharBloqueio}>Cancelar</Botao>
          </div>
        </form>
        <ul className={css.lista}>
          {bloqueios.map((x) => (
            <li key={x.id} className={`${css.cartao} ${layout.registro}`}>
              <div className={layout.identidade}><strong>{dataCurtaComSemana(x.data)}</strong><span>{x.motivo}</span>
                <span className={css.apoio}>{x.janelaEspecificaId ? rotuloJanelaPorId(x.janelaEspecificaId) : 'Dia inteiro'}</span>
              </div>
              {removendo === x.id ? (
                <div className={css.linha}>
                  <span className={`${css.apoio} ${css.cresce}`}>Remover este bloqueio? O horário volta a ficar disponível para os clientes.</span>
                  <Botao variante="secundario" disabled={enviando} onClick={() => acoes.removerBloqueio(x.id).then((ok) => {
                    if (ok) { setRemovendo(null); requestAnimationFrame(() => bloqueioRef.current?.focus()) }
                  })}>Remover bloqueio</Botao>
                  <Botao variante="texto" disabled={enviando} onClick={() => setRemovendo(null)}>Manter bloqueio</Botao>
                </div>
              ) : (
                <Botao variante="texto" disabled={enviando} aria-label={`Remover bloqueio de ${dataCurtaComSemana(x.data)}`} onClick={() => setRemovendo(x.id)}>Remover</Botao>
              )}
            </li>
          ))}
        </ul>
        {bloqueios.length === 0 && <p className={layout.vazio}>Nenhum bloqueio nos próximos 60 dias. Os horários seguem o cadastro semanal.</p>}
      </section>
    </div>
  )
}
