namespace ScreenPortal.Composition
{
    public sealed class AppOptions
    {
        /// <summary>Cria uma tela de demonstração ao iniciar (até o seletor "Novo monitor" da Fase 2 existir).</summary>
        public bool SeedDemoScreen { get; }

        public AppOptions(bool seedDemoScreen) => SeedDemoScreen = seedDemoScreen;
    }
}
