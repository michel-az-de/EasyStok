using System;

namespace EasyStock.Domain.Entities
{
    public class ConfiguracaoSistema
    {
        public string Chave { get; private set; } = null!;
        public string Valor { get; private set; } = null!;
        public string Descricao { get; private set; } = null!;
        public DateTime AlteradoEm { get; private set; }
        public string AlteradoPor { get; private set; } = null!;

        private ConfiguracaoSistema() { }

        public static ConfiguracaoSistema Criar(string chave, string valor, string descricao)
            => new()
            {
                Chave = chave,
                Valor = valor,
                Descricao = descricao,
                AlteradoEm = DateTime.UtcNow,
                AlteradoPor = "system"
            };

        public void Atualizar(string valor, string adminEmail)
        {
            Valor = valor;
            AlteradoPor = adminEmail;
            AlteradoEm = DateTime.UtcNow;
        }
    }
}
