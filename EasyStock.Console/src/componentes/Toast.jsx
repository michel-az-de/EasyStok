import { Icone } from './Icone'
import css from './Toast.module.css'

// #1807: pilha de avisos curtos no canto da tela (toast). A região viva existe sempre, mesmo
// vazia, para o leitor de tela anunciar o que entra (`role="status"`, educado: não interrompe).
// Quem decide quanto tempo cada aviso fica é a fila (`aplicacao/useFilaDeAvisos.js`); aqui só o
// desenho, o X e o clique que leva aonde o aviso aponta.
//
// itens: [{ id, titulo, corpo, icone?, tom?: 'marca' | 'sucesso' | 'alerta' | 'info', abrivel? }]
export function Toast({ itens, aoFechar, aoAbrir, rotulo = 'Avisos', rodape = null }) {
  return (
    <section className={css.pilha} role="status" aria-live="polite" aria-label={rotulo}>
      {itens.map((item) => (
        <div key={item.id} className={`${css.aviso} ${css[item.tom ?? 'marca'] ?? ''}`}>
          {item.icone && <span className={css.icone} aria-hidden="true"><Icone nome={item.icone} tamanho={20} /></span>}
          {item.abrivel && aoAbrir ? (
            <button type="button" className={css.texto} onClick={() => aoAbrir(item)}>
              <strong>{item.titulo}</strong>
              <span>{item.corpo}</span>
            </button>
          ) : (
            <p className={css.texto}>
              <strong>{item.titulo}</strong>
              <span>{item.corpo}</span>
            </p>
          )}
          <button type="button" className={css.fechar} aria-label={`Fechar aviso: ${item.titulo}`} title="Fechar" onClick={() => aoFechar(item.id)}>
            <Icone nome="x" tamanho={18} />
          </button>
        </div>
      ))}
      {itens.length > 0 && rodape}
    </section>
  )
}
