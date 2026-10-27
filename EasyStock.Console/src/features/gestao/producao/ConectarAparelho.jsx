import { useEffect, useState } from 'react'
import { Botao } from '../../../componentes/Botao'
import { CampoSelecao } from '../../../componentes/Campo'
import { Modal } from '../../../componentes/Modal'
import { useAcessoModulos } from '../../../aplicacao/acessoModulos'
import { codigoEmGrupos, gerarCodigo, lojasParaParear, tempoRestante } from '../../../aplicacao/aparelhos'
import css from './producao.module.css'

// #2081 (A6): "Conectar aparelho". O APK piloto pede o código à equipe; a dona ou a gerente gera aqui
// (a API só aceita Gerente para cima, a mesma regra de `editarProducao`). O código aparece em
// dígitos grandes com o tempo que falta; fechar o modal o esquece.
export function ConectarAparelho() {
  const { acoes } = useAcessoModulos()
  const [aberto, setAberto] = useState(false)
  if (acoes?.editarProducao !== true) return null
  return (
    <>
      <Botao variante="secundario" icone="phone" aria-haspopup="dialog" onClick={() => setAberto(true)}>Conectar aparelho</Botao>
      {aberto && <ModalDoCodigo aoFechar={() => setAberto(false)} />}
    </>
  )
}

function ModalDoCodigo({ aoFechar }) {
  // { estado: 'lojas' | 'gerando' | 'pronto' | 'erro', lojas, lojaId, codigo, expiraEm, erro }
  const [passo, setPasso] = useState({ estado: 'carregando' })
  const [agora, setAgora] = useState(() => Date.now())

  useEffect(() => {
    let vivo = true
    lojasParaParear()
      .then(({ empresaId, lojas }) => {
        if (!vivo) return
        setPasso({ estado: 'lojas', empresaId, lojas, lojaId: lojas.length === 1 ? lojas[0].id : '' })
      })
      .catch((e) => { if (vivo) setPasso({ estado: 'erro', erro: e.message }) })
    return () => { vivo = false }
  }, [])

  // O relógio só anda enquanto há código na tela.
  useEffect(() => {
    if (passo.estado !== 'pronto') return undefined
    const relogio = setInterval(() => setAgora(Date.now()), 1000)
    return () => clearInterval(relogio)
  }, [passo.estado])

  async function gerar() {
    const { empresaId, lojaId, lojas } = passo
    setPasso((p) => ({ ...p, estado: 'gerando', erro: null }))
    try {
      const { codigo, expiraEm } = await gerarCodigo({ empresaId, lojaId })
      setAgora(Date.now())
      setPasso({ estado: 'pronto', empresaId, lojaId, lojas, codigo, expiraEm })
    } catch (e) {
      setPasso((p) => ({ ...p, estado: 'lojas', erro: e.message }))
    }
  }

  const resta = passo.estado === 'pronto' ? tempoRestante(passo.expiraEm, agora) : null
  const venceu = passo.estado === 'pronto' && resta === null
  const podeGerar = (passo.estado === 'lojas' && passo.lojaId) || venceu

  return (
    <Modal titulo="Conectar aparelho" aoFechar={aoFechar}
      descricao="No aparelho, abra o EasyStok e digite o código. Ele vale 10 minutos e serve uma vez só."
      rodape={<>
        <Botao variante="texto" onClick={aoFechar}>Fechar</Botao>
        {passo.estado !== 'pronto' || venceu
          ? <Botao variante="primario" icone="check" disabled={!podeGerar} onClick={gerar}>{passo.estado === 'gerando' ? 'Gerando…' : venceu ? 'Gerar outro código' : 'Gerar código'}</Botao>
          : null}
      </>}>
      <div className={css.pareamento}>
        {passo.estado === 'carregando' && <output className={css.detalheDoCodigo}>Buscando as lojas…</output>}
        {passo.estado === 'erro' && <p className={css.avisoDoCodigo} role="alert">{passo.erro}</p>}
        {(passo.estado === 'lojas' || passo.estado === 'gerando') && passo.lojas.length > 1 && (
          <CampoSelecao rotulo="Loja do aparelho" value={passo.lojaId} disabled={passo.estado === 'gerando'}
            onChange={(e) => setPasso((p) => ({ ...p, lojaId: e.target.value }))}
            opcoes={[{ valor: '', rotulo: 'Escolha a loja' }, ...passo.lojas.map((l) => ({ valor: l.id, rotulo: l.nome }))]} />
        )}
        {passo.erro && passo.estado !== 'erro' && <p className={css.avisoDoCodigo} role="alert">{passo.erro}</p>}
        {passo.estado === 'pronto' && (
          <>
            <p className={css.codigoDoAparelho} aria-label={`Código ${passo.codigo.split('').join(' ')}`}>{codigoEmGrupos(passo.codigo)}</p>
            {/* Sem região viva: o relógio muda a cada segundo e o leitor de tela repetiria tudo. */}
            <p className={css.detalheDoCodigo}>{venceu ? 'O código venceu. Gere outro.' : `Vale por mais ${resta}`}</p>
          </>
        )}
      </div>
    </Modal>
  )
}
