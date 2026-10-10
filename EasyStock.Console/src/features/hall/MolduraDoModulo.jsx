// Moldura do módulo (#1447): barra fina com a volta ao hall, o nome do módulo e
// o menu próprio dele (as telas do catálogo). O módulo e a tela vêm da rota
// (ADR-0046 D1); nada aqui guarda estado. Não lê contexto do Atendimento porque
// também envolve a fila da Cozinha e o painel de Entregas, que montam fora dele.
//
// `operacao`: tela de trabalho (Cozinha, Entregas) ocupa a altura toda; tela de
// ajuste ganha título e coluna de leitura. Módulo sem tela mostra "Em breve".
import { useEffect, useRef } from 'react'
import { MarcaIcone } from '../../componentes/MarcaIcone'
import { Botao } from '../../componentes/Botao'
import { InterruptorTema } from '../../componentes/InterruptorTema'
import { useAcessoModulos } from '../../aplicacao/acessoModulos'
import { Vazio } from '../../componentes/Vazio'
import { HASH_HALL } from '../../dominio/rota'
import { hashDoModulo, moduloPorId, telasDoMenu } from '../../dominio/modulos'
import { IconeDoModulo } from './IconeDoModulo'
import css from './moldura.module.css'

const MODULO_DO_BALCAO = 'atendimento'
const HASH_BALCAO = hashDoModulo(MODULO_DO_BALCAO, 'balcao')

export function MolduraDoModulo({ moduloId, telaId, fonteApi, operacao = false, tituloVisivel = true, children }) {
  const { permite, aoSair, sessaoPersistente, notificacoes } = useAcessoModulos()
  const modulo = moduloPorId(moduloId)
  const telas = telasDoMenu(modulo, { fonteApi })
  const tela = telas.find((t) => t.id === telaId) ?? null
  const menuRef = useRef(null)

  useEffect(() => {
    const menu = menuRef.current
    // Move só a fileira de telas, sem rolar o documento ou a operação.
    const revelarAtivo = () => {
      const ativo = menu?.querySelector('[aria-current="page"]')
      if (ativo) menu.scrollLeft = ativo.offsetLeft - (menu.clientWidth - ativo.offsetWidth) / 2
    }
    revelarAtivo()
    if (!menu) return
    const tamanho = new ResizeObserver(revelarAtivo)
    tamanho.observe(menu)
    return () => tamanho.disconnect()
  }, [telaId])

  return (
    <div className={css.moldura}>
      <header className={css.barra}>
        <div className={css.contexto}>
          <a className={css.voltar} href={HASH_HALL}>
            <MarcaIcone nome="anterior" tamanho={20} /> <span>Módulos</span>
          </a>
          {/* #1474: quem veio da barra lateral do Balcão volta para ele num toque. */}
          {modulo.id !== MODULO_DO_BALCAO && permite(MODULO_DO_BALCAO) && (
            <a className={css.voltar} href={HASH_BALCAO}>
              <MarcaIcone nome="mensagem" tamanho={24} /> <span>Balcão</span>
            </a>
          )}
          <span className={css.modulo}>
            <IconeDoModulo modulo={modulo} /> {modulo.nome}
          </span>
        </div>
        <div className={css.acoes}>
          <InterruptorTema />
          {notificacoes && <div className={css.avisos}>{notificacoes}</div>}
          {aoSair && <Botao className={css.sair} variante="texto" icone="log-out" onClick={aoSair} title={sessaoPersistente ? 'A Cozinha permanece conectada ao fechar o navegador. Saia ao trocar de pessoa.' : undefined}>{sessaoPersistente ? 'Sair deste aparelho' : 'Sair'}</Botao>}
        </div>
        {telas.length > 1 && (
          <nav ref={menuRef} className={css.menu} aria-label={`Telas de ${modulo.nome}`}>
            {telas.map((t) => (
              <a
                key={t.id}
                href={hashDoModulo(modulo.id, t.id)}
                className={`${css.item} ${t.id === telaId ? css.itemAtivo : ''}`}
                aria-current={t.id === telaId ? 'page' : undefined}
              >
                <IconeDoModulo modulo={modulo} telaId={t.id} />
                <span>{t.rotulo}</span>
              </a>
            ))}
          </nav>
        )}
      </header>
      {operacao
        ? <div className={css.operacao}>{children}</div>
        : (
          <main className={css.ajuste}>
            <div className={css.folha}>
              {tela ? (
                <>
                  <h1 className={tituloVisivel ? css.titulo : 'sr'}>
                    <IconeDoModulo modulo={modulo} telaId={tela.id} />
                    {tela.rotulo}
                  </h1>
                  <div className={css.conteudo}>{children}</div>
                </>
              ) : (
                <div className={css.conteudo}>
                  <Vazio titulo={`${modulo.nome}: em breve`} acao={<a className={css.link} href={HASH_HALL}>Voltar aos módulos</a>}>
                    Este módulo ainda não funciona por esta tela.
                  </Vazio>
                </div>
              )}
            </div>
          </main>
        )}
    </div>
  )
}
