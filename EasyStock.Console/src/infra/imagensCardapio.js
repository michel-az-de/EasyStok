// Fotos de prato. Enquanto não existe foto de verdade, a casa manda uma carta
// do cardápio desenhada, com nome, porção e preço legíveis. É honesto: parece
// arte de cardápio, não finge ser fotografia.

// Integração 50: a carta mora em dominio/arteCardapio.js (uma só, com o título
// que quebra linha); aqui só se reexporta para quem já importava deste arquivo.
export { cartaDoItem } from '../dominio/arteCardapio'
