import { useState } from 'react'
import { Botao } from '../../componentes/Botao'
import { Icone } from '../../componentes/Icone'
import { CampoSelecao, CampoTexto } from '../../componentes/Campo'
import { useCadastroEntregaApi } from '../../aplicacao/useCadastroEntregaApi'
import { DIAS_DA_SEMANA, corpoBloqueio, corpoJanela, corpoZona } from '../../dominio/entregasApi'
import css from './entregasApi.module.css'

// Cadastro de entrega da loja (S45, `api/minha-vitrine/entrega`): janelas com
// capacidade, zonas de frete por CEP ou bairros e bloqueios de dia ou janela.
const OPCOES_DIA = DIAS_DA_SEMANA.map((rotulo, i) => ({ valor: String(i), rotulo }))
const JANELA_VAZIA = { diaDaSemana: '1', horaInicio: '18:00', horaFim: '20:00', capacidadeMaxima: '10', label: '' }
const ZONA_VAZIA = { label: '', valor: '', tempoEstimadoMinutos: '40', ordem: '1', cobertura: 'bairros', cepInicio: '', cepFim: '', bairros: '' }
const BLOQUEIO_VAZIO = { data: '', motivo: '', janelaEspecificaId: '' }

function useFormulario(inicial) {
  const [form, setForm] = useState(inicial)
  const campo = (k) => ({ value: form[k], onChange: (e) => setForm({ ...form, [k]: e.target.value }) })
  return { form, campo, limpar: () => setForm(inicial) }
}

export function CadastroEntregaApi() {
  const { janelas, zonas, bloqueios, erro, acoes, limparErro } = useCadastroEntregaApi()
  const j = useFormulario(JANELA_VAZIA)
  const z = useFormulario(ZONA_VAZIA)
  const b = useFormulario(BLOQUEIO_VAZIO)
  const enviar = (acao, f, corpo) => (e) => {
    e.preventDefault()
    acao(corpo(f.form)).then(f.limpar)
  }

  if (janelas === null && !erro) return <p className={css.vazio}>Carregando o cadastro…</p>
  const opcoesJanela = [{ valor: '', rotulo: 'O dia inteiro' }, ...(janelas ?? []).map((x) => ({ valor: x.id, rotulo: x.label }))]

  return (
    <>
      {erro && (
        <p className={css.faixa} role="alert">
          <Icone nome="circle-x" tamanho={16} /> <span className={css.cresce}>{erro}</span>
          <Botao variante="texto" onClick={limparErro}>Fechar</Botao>
        </p>
      )}
      <section className={css.secao} aria-label="Janelas de entrega">
        <h3>Janelas de entrega</h3>
        <form className={css.formulario} onSubmit={enviar(acoes.criarJanela, j, corpoJanela)}>
          <CampoSelecao rotulo="Dia" opcoes={OPCOES_DIA} {...j.campo('diaDaSemana')} />
          <CampoTexto rotulo="Início" tipo="time" {...j.campo('horaInicio')} required />
          <CampoTexto rotulo="Fim" tipo="time" {...j.campo('horaFim')} required />
          <CampoTexto rotulo="Capacidade" tipo="number" min="1" {...j.campo('capacidadeMaxima')} required />
          <CampoTexto rotulo="Nome" {...j.campo('label')} required />
          <Botao tipo="submit" variante="primario">Criar janela</Botao>
        </form>
        <ul className={css.lista}>
          {(janelas ?? []).map((x) => (
            <li key={x.id} className={`${css.cartao} ${css.linha}`}>
              <span className={css.cresce}>{x.label} · {DIAS_DA_SEMANA[x.diaDaSemana]} {x.horaInicio.slice(0, 5)}–{x.horaFim.slice(0, 5)}</span>
              <span className={css.apoio}>{x.capacidadeMaxima} vagas{x.ativa ? '' : ' · desativada'}</span>
              <Botao variante="texto" onClick={() => acoes.alternarJanela(x)}>{x.ativa ? 'Desativar' : 'Ativar'}</Botao>
            </li>
          ))}
        </ul>
      </section>

      <section className={css.secao} aria-label="Zonas de frete">
        <h3>Zonas de frete</h3>
        <form className={css.formulario} onSubmit={enviar(acoes.criarZona, z, corpoZona)}>
          <CampoTexto rotulo="Nome" {...z.campo('label')} required />
          <CampoTexto rotulo="Valor (R$)" inputMode="decimal" {...z.campo('valor')} required />
          <CampoTexto rotulo="Tempo (min)" tipo="number" min="1" {...z.campo('tempoEstimadoMinutos')} required />
          <CampoTexto rotulo="Ordem" tipo="number" min="0" {...z.campo('ordem')} />
          <CampoSelecao
            rotulo="Cobertura" opcoes={[{ valor: 'bairros', rotulo: 'Bairros' }, { valor: 'cep', rotulo: 'Faixa de CEP' }]}
            {...z.campo('cobertura')}
          />
          {z.form.cobertura === 'cep'
            ? (
              <>
                <CampoTexto rotulo="CEP inicial" inputMode="numeric" {...z.campo('cepInicio')} required />
                <CampoTexto rotulo="CEP final" inputMode="numeric" {...z.campo('cepFim')} required />
              </>
            )
            : <CampoTexto rotulo="Bairros (separados por vírgula)" {...z.campo('bairros')} required />}
          <Botao tipo="submit" variante="primario">Criar zona</Botao>
        </form>
        <ul className={css.lista}>
          {zonas.map((x) => (
            <li key={x.id} className={`${css.cartao} ${css.linha}`}>
              <span className={css.cresce}>{x.label} · R$ {Number(x.valor).toFixed(2).replace('.', ',')} · {x.tempoEstimadoMinutos} min</span>
              <span className={css.apoio}>
                {x.cepInicio ? `CEP ${x.cepInicio}–${x.cepFim}` : x.bairros.join(', ')}{x.ativa ? '' : ' · desativada'}
              </span>
              <Botao variante="texto" onClick={() => acoes.alternarZona(x)}>{x.ativa ? 'Desativar' : 'Ativar'}</Botao>
            </li>
          ))}
        </ul>
      </section>

      <section className={css.secao} aria-label="Bloqueios">
        <h3>Bloqueios (próximos 60 dias)</h3>
        <form className={css.formulario} onSubmit={enviar(acoes.criarBloqueio, b, corpoBloqueio)}>
          <CampoTexto rotulo="Data" tipo="date" {...b.campo('data')} required />
          <CampoSelecao rotulo="Janela" opcoes={opcoesJanela} {...b.campo('janelaEspecificaId')} />
          <CampoTexto rotulo="Motivo" {...b.campo('motivo')} required />
          <Botao tipo="submit" variante="primario">Bloquear</Botao>
        </form>
        <ul className={css.lista}>
          {bloqueios.map((x) => (
            <li key={x.id} className={`${css.cartao} ${css.linha}`}>
              <span className={css.cresce}>{x.data} · {x.motivo}</span>
              <span className={css.apoio}>{x.janelaEspecificaId ? (janelas ?? []).find((w) => w.id === x.janelaEspecificaId)?.label : 'Dia inteiro'}</span>
              <Botao variante="texto" onClick={() => acoes.removerBloqueio(x.id)}>Remover</Botao>
            </li>
          ))}
        </ul>
      </section>
    </>
  )
}
