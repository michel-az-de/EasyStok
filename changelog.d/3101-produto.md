### Fixed
- **Nome de produto repetido** (#3101): o banco passa a recusar dois produtos da mesma empresa cujo nome só muda entre maiúsculas e minúsculas, mesmo em gravações simultâneas. Se já houver repetidos, o mais antigo mantém o nome e os outros ganham o sufixo "(dup ...)" para você renomear.
