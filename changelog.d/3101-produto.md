### Fixed
- **Nome de produto repetido** (#3101): o banco passa a recusar dois produtos da mesma empresa cujo nome só muda entre maiúsculas e minúsculas, mesmo em gravações simultâneas. Se já houver repetidos, o produto ativo (ou o mais antigo, no empate) mantém o nome e os outros ganham o sufixo "(dup ...)" para você renomear.
