import { Pilula } from './Pilula'

// #2146 (plano #2130, seção 3.3): status = cor + ícone + texto. É a Pilula com ícone (fundo pastel,
// a variante do cartão do Balcão), sem CSS próprio: o Selo só fecha os tons e exige o ícone, para a
// cor nunca informar sozinha e o estado se ler sem cor. O texto vai como filho.
const TONS_DO_SELO = Object.freeze({
  sucesso: 'ok', alerta: 'aviso', erro: 'perigo', marca: 'marca', neutro: 'neutro',
})

export function Selo({ tom = 'neutro', icone, children }) {
  if (import.meta.env?.DEV) {
    // oxlint-disable-next-line no-console -- aviso só de desenvolvimento.
    if (!icone) console.warn(`Selo "${children}" sem ícone: status precisa de cor, ícone e texto.`)
    // oxlint-disable-next-line no-console -- aviso só de desenvolvimento.
    if (!TONS_DO_SELO[tom]) console.warn(`Selo com tom "${tom}" fora de ${Object.keys(TONS_DO_SELO).join(', ')}.`)
  }
  return <Pilula tom={TONS_DO_SELO[tom] ?? 'neutro'} icone={icone}>{children}</Pilula>
}
