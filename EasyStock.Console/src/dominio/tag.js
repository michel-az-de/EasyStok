// Tag do cliente (#1474, #2734). O EasyStok grava a tag normalizada: "Sem glúten" vira "sem_gluten".
// `rotuloDaTag` é o que a tela mostra; `chaveDaTag` é o que se compara (sem acento, caixa nem "_").
// Mora num arquivo só dela porque a Ficha (cliente.js) e a busca do Balcão (conversa.js) usam as duas.
export const rotuloDaTag = (tag) => String(tag ?? '').replace(/_/g, ' ')
export const chaveDaTag = (tag) => rotuloDaTag(tag).normalize('NFD').replace(/[̀-ͯ]/g, '')
  .toLowerCase().replace(/\s+/g, ' ').trim()
