import { useState } from 'react'
import { usePendenciaAvisada } from '../../aplicacao/formularioDeAjuste'
import { useEntregadoresApi } from '../../aplicacao/useEntregadoresApi'
import { Botao } from '../../componentes/Botao'
import { CampoSelecao, CampoTexto } from '../../componentes/Campo'
import { EMPRESAS_ENTREGADOR, TIPOS_ENTREGADOR } from '../../dominio/entregasApi'
import css from './entregasApi.module.css'

// Cadastro de entregadores (S44): o mesmo nas Entregas, em Configurações e nos Primeiros passos
// (#2070). Nas Entregas, lista e ações vêm do `useEntregasApi`; fora delas, `CadastroEntregadoresApi`
// lê e grava só os entregadores.
const ENTREGADOR_VAZIO = { nome: '', tipo: 'Motoboy', empresa: 'Propria', telefone: '', veiculo: '', placa: '' }
const mudouDoVazio = (form) => Object.keys(ENTREGADOR_VAZIO).some((campo) => form[campo] !== ENTREGADOR_VAZIO[campo])

export function CadastroEntregadores({ entregadores, ocupado, acoes, aoMudarPendencia, aoSalvo, compacto = false }) {
  const [form, setForm] = useState(ENTREGADOR_VAZIO)
  const [confirmacao, setConfirmacao] = useState(null)
  usePendenciaAvisada(mudouDoVazio(form), aoMudarPendencia)
  const campo = (k) => ({ value: form[k], disabled: ocupado, onChange: (e) => setForm({ ...form, [k]: e.target.value }) })
  const salvar = (e) => {
    e.preventDefault()
    if (!form.nome.trim()) return
    setConfirmacao(null)
    return acoes.criarEntregador({ ...form, nome: form.nome.trim() }).then((ok) => {
      if (ok) {
        setForm(ENTREGADOR_VAZIO)
        setConfirmacao('Entregador cadastrado. Você já pode selecioná-lo nas viagens.')
        aoSalvo?.()
      }
    })
  }
  return (
    <section className={css.secao} aria-label="Entregadores">
      {!compacto && <h3>Novo entregador</h3>}
      <form className={css.formulario} onSubmit={salvar}>
        <CampoTexto rotulo="Nome" {...campo('nome')} required />
        <CampoSelecao rotulo="Tipo" opcoes={TIPOS_ENTREGADOR} {...campo('tipo')} />
        <CampoSelecao rotulo="Empresa" opcoes={EMPRESAS_ENTREGADOR} {...campo('empresa')} />
        <CampoTexto rotulo="Telefone" tipo="tel" {...campo('telefone')} />
        <CampoTexto rotulo="Veículo" {...campo('veiculo')} />
        <CampoTexto rotulo="Placa" {...campo('placa')} />
        <Botao tipo="submit" variante="primario" disabled={ocupado || !form.nome.trim()}>Cadastrar</Botao>
      </form>
      {confirmacao && <output className={`${css.apoio} ${css.bloco}`}>{confirmacao}</output>}
      <h3>Ativos</h3>
      {entregadores.length === 0 && <p className={css.vazio}>Nenhum entregador cadastrado.</p>}
      <ul className={css.lista}>
        {entregadores.map((e) => (
          <li key={e.id} className={`${css.cartao} ${css.linha}`}>
            <span className={css.cresce}>{e.nome}</span>
            <span className={css.apoio}>{[e.veiculo, e.placa, e.telefone].filter(Boolean).join(' · ')}</span>
            <Botao variante="texto" disabled={ocupado} onClick={() => acoes.desativarEntregador(e.id).then((ok) => { if (ok) aoSalvo?.() })}>Desativar</Botao>
          </li>
        ))}
      </ul>
    </section>
  )
}

// Fora das Entregas: a lista e as ações só dos entregadores, com o erro da API à vista. "Tentar de
// novo" só relê a lista; cadastro recusado deixa o formulário como estava para enviar outra vez.
export function CadastroEntregadoresApi({ aoMudarPendencia, aoSalvo, compacto = false }) {
  const { entregadores, erro, leituraFalhou, ocupado, acoes, tentarDeNovo } = useEntregadoresApi()
  return <>
    {erro && (
      <p className={css.faixa} role="alert">
        <span className={css.cresce}>{erro}</span>
        {leituraFalhou && <Botao variante="secundario" disabled={ocupado} onClick={tentarDeNovo}>Tentar de novo</Botao>}
      </p>
    )}
    {entregadores === null
      ? !erro && <output className={css.vazio}>Carregando os entregadores…</output>
      : <CadastroEntregadores entregadores={entregadores} ocupado={ocupado} acoes={acoes}
        aoMudarPendencia={aoMudarPendencia} aoSalvo={aoSalvo} compacto={compacto} />}
  </>
}
