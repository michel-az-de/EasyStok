// Hall de módulos (#1447, homologação de 07/10): a tela inicial depois do login,
// com os módulos do ERP da Casa da Baba numerados como no plano (ADR-0056). O
// módulo ligado abre pela rota; o que ainda não tem tela aparece "Em breve" e
// não navega. Os atalhos embaixo do nome são o menu do módulo (as outras telas).
import { Icone } from '../../componentes/Icone'
import { useRef, useState } from 'react'
import { Marca } from '../../componentes/Marca'
import { CampoTexto } from '../../componentes/Campo'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { Botao } from '../../componentes/Botao'
import { InterruptorTema } from '../../componentes/InterruptorTema'
import { Vazio } from '../../componentes/Vazio'
import { filtrarModulos, hashDoModulo, modulosDoHall, saudacaoDoHall } from '../../dominio/modulos'
import css from './hall.module.css'

const doisDigitos = (n) => String(n).padStart(2, '0')

function CartaoModulo({ modulo }) {
  const atalhos = modulo.telas
  if (!modulo.disponivel) {
    return (
      <li className={`${css.cartao} ${css.emBreve}`}>
        <span className={css.numero}>{doisDigitos(modulo.numero)}</span>
        <span className={css.icone}><Icone nome={modulo.icone} tamanho={28} /></span>
        <span className={css.nome}>{modulo.nome}</span>
        <span className={css.resumo}>{modulo.resumo}</span>
        <span className={css.selo}><Icone nome="lock" tamanho={16} /> Em breve</span>
      </li>
    )
  }
  return (
    <li className={css.cartao}>
      <span className={css.numero}>{doisDigitos(modulo.numero)}</span>
      <span className={css.icone}><Icone nome={modulo.icone} tamanho={28} /></span>
      <a className={css.abrir} href={modulo.href}>
        <span className={css.nome}>{modulo.nome}</span>
        <Icone nome="chevron-right" tamanho={20} />
      </a>
      <span className={css.resumo}>{modulo.resumo}</span>
      {atalhos.length > 0 && (
        <ul className={css.atalhos} aria-label={`Telas de ${modulo.nome}`}>
          {atalhos.map((tela) => (
            <li key={tela.id}>
              <a className={css.atalho} href={hashDoModulo(modulo.id, tela.id)}>{tela.rotulo}</a>
            </li>
          ))}
        </ul>
      )}
    </li>
  )
}

export function HallDeModulos({ fonteApi = false, sessao = null, agora }) {
  const { permite, aoSair, notificacoes } = useAcessoModulos()
  const [busca, setBusca] = useState('')
  const buscaRef = useRef(null)
  const modulos = filtrarModulos(modulosDoHall({ fonteApi }).filter((m) => permite(m.id)), busca)
  const buscando = Boolean(busca.trim())
  const limparBusca = () => {
    setBusca('')
    buscaRef.current?.focus()
  }
  return (
    <div className={css.hall}>
      <header className={css.topo}>
        <Marca />
        <div className={css.acoes}>
          <InterruptorTema />
          {notificacoes && <div className={css.avisos}>{notificacoes}</div>}
          {aoSair && <Botao className={css.sair} variante="texto" onClick={aoSair}>{sessao?.persistente ? 'Sair deste aparelho' : 'Sair'}</Botao>}
        </div>
      </header>
      <main className={css.conteudo}>
        <div className={css.abertura}>
        <div className={css.cabeca}>
          <p className={css.legenda}>Módulos da casa</p>
          <h1 className={css.titulo}>{saudacaoDoHall(agora, sessao?.usuario?.nome)}</h1>
          <p className={css.apoio}>Escolha uma área ou vá direto à tela que precisa.</p>
          {sessao?.persistente && <p className={css.apoio}>A Cozinha permanece conectada neste aparelho. Use Sair ao trocar de pessoa.</p>}
          {modulos.length === 0 && !busca && <output>Seu perfil ainda não tem módulos liberados. Peça à dona para revisar seu acesso.</output>}
        </div>
        <div className={css.busca}>
          <CampoTexto ref={buscaRef} rotulo="Buscar módulo ou tela" tipo="search"
            placeholder="Ex.: insumos, caixa ou entregas" value={busca}
            onChange={(e) => setBusca(e.target.value)} />
          {buscando && <Botao variante="texto" onClick={limparBusca}>Limpar busca</Botao>}
        </div>
        </div>
        <output className={css.resultado} aria-live="polite">
          {buscando && (modulos.length === 1 ? '1 módulo encontrado.' : `${modulos.length} módulos encontrados.`)}
        </output>
        <ul className={css.grade} aria-label="Módulos">
          {modulos.map((modulo) => <CartaoModulo key={modulo.id} modulo={modulo} />)}
        </ul>
        {buscando && modulos.length === 0 && (
          <Vazio titulo="Nenhum módulo ou tela encontrado">
            Confira o nome ou use Limpar busca para ver seus módulos novamente.
          </Vazio>
        )}
      </main>
    </div>
  )
}
