### Changed
- **Menu e avisos sem barra colorida só à esquerda** (#3393): o item atual do menu lateral de todos os módulos passa a
  ter fundo tingido, contorno de 1px na cor da marca e negrito; os avisos (Toast) mostram o tom num contorno uniforme
  de 1px. A prova `prova-2734-sem-barra-lateral` passa a pegar barra desenhada por pseudo-elemento e por
  `inset var(--trilho)`, que escapavam dela.
