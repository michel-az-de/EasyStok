namespace EasyStock.Infra.MongoDb.Data.Mappings
{
    public static class MappingsRegistrar
    {
        public static void RegisterAll()
        {
            EmpresaMap.Register();
            ProdutoMap.Register();
        }
    }
}
