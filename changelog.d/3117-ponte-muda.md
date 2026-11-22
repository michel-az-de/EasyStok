### Added
- **Vigia da ponte WPPConnect muda**: o Worker compara o que a ponte enxerga com o que o webhook recebeu e abre incidente quando ela está conectada e não entrega. Nasce desligado (`Atendimento:WppConnect:VigiaPonteMuda:Habilitado`). O `vps-deploy.sh` passa a verificar a ponte e avisar, sem reiniciar. (#3117)
