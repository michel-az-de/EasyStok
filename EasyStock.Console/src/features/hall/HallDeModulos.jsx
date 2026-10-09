// Hall de módulos (#1447, homologação de 07/10): a tela inicial depois do login,
// com os módulos do ERP da Casa da Baba numerados como no plano (ADR-0056). O
// módulo ligado abre pela rota; o que ainda não tem tela aparece "Em breve" e
// não navega. Os atalhos embaixo do nome são o menu do módulo (as outras telas).
import { Icone } from '../../componentes/Icone'
import { useState } from 'react'
import { Marca } from '../../componentes/Marca'
import { CampoTexto } from '../../componentes/Campo'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { Botao } from '../../componentes/Botao'
import { filtrarModulos, hashDoModulo, modulosDoHall, saudacaoDoHall } from '../../dominio/modulos'
import css from './hall.module.css'

const doisDigitos = (n) => String(n).padStart(2, '0')

function CartaoModulo({ modulo, buscando }) {
  const atalhos = buscando ? modulo.telas : modulo.telas.slice(1)
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
      </a>
      <span className={css.resumo}>{modulo.resumo}</span>
      {atalhos.length > 0 && (
        <ul className={css.atalhos} aria-label={`Outras telas de ${modulo.nome}`}>
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
  const modulos = filtrarModulos(modulosDoHall({ fonteApi }).filter((m) => permite(m.id)), busca)
  return (
    <div className={css.hall}>
      <header className={css.topo}>
        <Marca />
        <div className={css.busca}>
          <CampoTexto rotulo="Buscar módulo ou tela" tipo="search" value={busca} onChange={(e) => setBusca(e.target.value)} />
        </div>
        {notificacoes}
        {aoSair && <Botao variante="texto" onClick={aoSair}>{sessao?.persistente ? 'Sair deste aparelho' : 'Sair'}</Botao>}
      </header>
      <main className={css.conteudo}>
        <div className={css.cabeca}>
          <h1 className={css.titulo}>{saudacaoDoHall(agora, sessao?.usuario?.nome)}</h1>
          <p className={css.apoio}>Escolha o módulo para começar.</p>
          {sessao?.persistente && <p className={css.apoio}>A Cozinha permanece conectada neste aparelho. Use Sair ao trocar de pessoa.</p>}
          {modulos.length === 0 && !busca && <output>Seu perfil ainda não tem módulos liberados. Peça à dona para revisar seu acesso.</output>}
        </div>
        <ul className={css.grade} aria-label="Módulos">
          {modulos.map((modulo) => <CartaoModulo key={modulo.id} modulo={modulo} buscando={Boolean(busca.trim())} />)}
        </ul>
        <output className={css.resultado} aria-live="polite">{busca.trim() && (modulos.length ? `${modulos.length} módulo(s) encontrado(s).` : 'Nenhum módulo ou tela encontrado. Tente outro nome.')}</output>
      </main>
    </div>
  )
}
