### Fixed
- **Vigia da ponte WPPConnect muda**: mensagem sincronizada numa reconexão, ou perdida num buraco antigo com entrega posterior, não abre mais incidente; ponte desconectada ou sem resposta por 30 minutos sai do vigia e o incidente fecha, em vez de ficar aberto para sempre. O vigia segue desligado por padrão. (#3138)
