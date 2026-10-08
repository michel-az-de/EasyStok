// Moldura do módulo (#1447): barra fina com a volta ao hall, o nome do módulo e
// o menu próprio dele (as telas do catálogo). O módulo e a tela vêm da rota
// (ADR-0046 D1); nada aqui guarda estado. Não lê contexto do Atendimento porque
// também envolve a fila da Cozinha e o painel de Entregas, que montam fora dele.
//
// `operacao`: tela de trabalho (Cozinha, Entregas) ocupa a altura toda; tela de
// ajuste ganha título e coluna de leitura. Módulo sem tela mostra "Em breve".
import { Icone } from '../../componentes/Icone'
import { Vazio } from '../../componentes/Vazio'
import { HASH_HALL } from '../../dominio/rota'
import { hashDoModulo, moduloPorId, telasDoMenu } from '../../dominio/modulos'
import css from './moldura.module.css'

export function MolduraDoModulo({ moduloId, telaId, fonteApi, operacao = false, children }) {
  const modulo = moduloPorId(moduloId)
  const telas = telasDoMenu(modulo, { fonteApi })
  const tela = telas.find((t) => t.id === telaId) ?? null

  return (
    <div className={css.moldura}>
      <header className={css.barra}>
        <a className={css.voltar} href={HASH_HALL}>
          <Icone nome="arrow-left" tamanho={18} /> <span>Módulos</span>
        </a>
        <span className={css.modulo}>
          <Icone nome={modulo.icone} tamanho={20} /> {modulo.nome}
        </span>
        {telas.length > 1 && (
          <nav className={css.menu} aria-label={`Telas de ${modulo.nome}`}>
            {telas.map((t) => (
              <a
                key={t.id}
                href={hashDoModulo(modulo.id, t.id)}
                className={`${css.item} ${t.id === telaId ? css.itemAtivo : ''}`}
                aria-current={t.id === telaId ? 'page' : undefined}
              >
                {t.rotulo}
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
                  <h1 className={css.titulo}>{tela.rotulo}</h1>
                  {children}
                </>
              ) : (
                <Vazio titulo={`${modulo.nome}: em breve`} acao={<a className={css.link} href={HASH_HALL}>Voltar aos módulos</a>}>
                  Este módulo ainda não tem tela no console. Ele entra pelo plano do ERP da Casa da Baba.
                </Vazio>
              )}
            </div>
          </main>
        )}
    </div>
  )
}
