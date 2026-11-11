### Fixed
- **Injeção de prompt pela nota do agente** (#2285): a nota que o agente grava com `registrar_nota` nasce da conversa
  com o cliente; ao voltar no dossiê, as marcas do sistema (`[interno]`, `(dona)`) dentro dela são neutralizadas.
- **Marca do sistema disfarçada** (#2285): a neutralização procura as marcas no texto como o modelo o lê (NFKC, sem
  caractere invisível), então `[in` + zero-width + `terno]`, colchete fullwidth e letra em negrito matemático deixam de
  passar. Texto sem marca segue intacto, emoji incluso.
- **Texto de exceção no console** (#2285): falha ao guardar anexo do WhatsApp, falha na sincronização da coexistência e
  o 500 do cardápio da vitrine mostram uma frase estável; o detalhe (caminho, bucket, texto do driver) fica só no log.
