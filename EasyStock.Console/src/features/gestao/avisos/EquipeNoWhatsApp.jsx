// #2211: quem da equipe recebe no WhatsApp da loja os avisos do grupo "WhatsApp da equipe". Cada marcação
// grava na hora (uma pessoa por vez). Sem telefone confirmado a marcação fica, mas nada chega: a linha avisa.
import { useCallback, useEffect, useRef, useState } from 'react'
import { obterEquipeAvisada, salvarPessoaAvisada } from '../../../infra/api/avisosApi'
import { Botao } from '../../../componentes/Botao'
import css from './abaAvisos.module.css'

const AVISOS_DA_EQUIPE = [
  { campo: 'atencao', rotulo: 'Atenção no atendimento' },
  { campo: 'pedido', rotulo: 'Pedido com canhoto' },
  { campo: 'pagamento', rotulo: 'Problema no pagamento' },
]

function LinhaDaPessoa({ pessoa, aoMudar }) {
  const [situacao, setSituacao] = useState({ estado: 'ocioso' })

  async function marcar(campo, ligado) {
    const proxima = { ...pessoa, [campo]: ligado }
    setSituacao({ estado: 'salvando' })
    try {
      const salva = await salvarPessoaAvisada(pessoa.usuarioId, proxima)
      aoMudar(salva && typeof salva === 'object' ? salva : proxima)
      setSituacao({ estado: 'salvo' })
    } catch (erro) {
      setSituacao({ estado: 'erro', mensagem: erro.message })
    }
  }

  return (
    <li className={css.linha}>
      <fieldset className={css.pessoa}>
      <legend className={css.rotulo}>{pessoa.nome}</legend>
      {!pessoa.telefoneConfirmado && (
        <p className={css.dica}>Telefone não confirmado: nada chega até a pessoa confirmar o telefone no cadastro.</p>
      )}
      <div className={css.acoes}>
        {AVISOS_DA_EQUIPE.map((aviso) => (
          <label key={aviso.campo} className={css.interruptor}>
            <input
              type="checkbox"
              checked={pessoa[aviso.campo] === true}
              disabled={situacao.estado === 'salvando'}
              onChange={(evento) => marcar(aviso.campo, evento.target.checked)}
            />
            <span>{aviso.rotulo}</span>
          </label>
        ))}
      </div>
      {situacao.estado === 'salvo' && <output className={css.retorno}>Salvo.</output>}
      {situacao.estado === 'erro' && <output className={css.erro} role="alert">Não foi possível salvar. {situacao.mensagem}</output>}
      </fieldset>
    </li>
  )
}

export function EquipeNoWhatsApp() {
  const [carga, setCarga] = useState({ estado: 'carregando', pessoas: [] })
  const consulta = useRef(0)

  const buscar = useCallback(() => {
    const atual = ++consulta.current
    return obterEquipeAvisada()
      .then((dados) => { if (atual === consulta.current) setCarga({ estado: 'ok', pessoas: Array.isArray(dados) ? dados : [] }) })
      .catch((erro) => { if (atual === consulta.current) setCarga({ estado: 'erro', mensagem: erro.message, pessoas: [] }) })
  }, [])

  useEffect(() => {
    buscar()
    const leituras = consulta
    return () => { leituras.current++ }
  }, [buscar])

  const trocar = (salva) => setCarga((atual) => ({
    ...atual,
    pessoas: atual.pessoas.map((p) => (p.usuarioId === salva.usuarioId ? { ...p, ...salva } : p)),
  }))

  return (
    <fieldset className={css.grupo}>
      <legend className={css.tituloGrupo}>Quem recebe no WhatsApp</legend>
      <p className={css.descricao}>
        Os avisos do grupo &lsquo;WhatsApp da equipe&rsquo; saem pelo número da loja só para quem estiver marcado aqui.
      </p>
      {carga.estado === 'carregando' && <p className={css.descricao}>Carregando a equipe…</p>}
      {carga.estado === 'erro' && (
        <div className={css.secao} role="alert">
          <p className={css.erro}>A equipe não carregou: {carga.mensagem}</p>
          <div className={css.acoes}>
            <Botao className={css.toque} onClick={() => { setCarga({ estado: 'carregando', pessoas: [] }); buscar() }}>Tentar de novo</Botao>
          </div>
        </div>
      )}
      {carga.estado === 'ok' && carga.pessoas.length === 0 && <p className={css.descricao}>Nenhuma pessoa ativa na equipe.</p>}
      {carga.estado === 'ok' && carga.pessoas.length > 0 && (
        <ul className={css.lista}>
          {carga.pessoas.map((pessoa) => <LinhaDaPessoa key={pessoa.usuarioId} pessoa={pessoa} aoMudar={trocar} />)}
        </ul>
      )}
    </fieldset>
  )
}
