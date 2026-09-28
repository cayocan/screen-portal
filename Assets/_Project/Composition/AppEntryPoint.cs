using System;
using ScreenPortal.App;
using ScreenPortal.App.Screens;
using ScreenPortal.Domain.Commands;
using ScreenPortal.Domain.Screens;
using ScreenPortal.Domain.Spatial;
using VContainer.Unity;

namespace ScreenPortal.Composition
{
    /// <summary>Liga o sincronizador de views e, em modo demo, cria a tela padrão validada na Fase 0.</summary>
    public sealed class AppEntryPoint : IStartable, IDisposable
    {
        private readonly ScreenViewSynchronizer _synchronizer;
        private readonly ICommandBus _bus;
        private readonly AppOptions _options;

        public AppEntryPoint(ScreenViewSynchronizer synchronizer, ICommandBus bus, AppOptions options)
        {
            _synchronizer = synchronizer;
            _bus = bus;
            _options = options;
        }

        public void Start()
        {
            _synchronizer.Start();
            if (_options.SeedDemoScreen)
            {
                // Tamanho/distância padrão do teste de legibilidade (fase0-texto.md): 140 cm a 1,3 m.
                _bus.Send(new CreateScreen(Resolution.Create(2560, 1080), Pose.At(new Vec3(0f, 1.2f, 1.3f)), 1.4f, label: "Demo"));
            }
        }

        public void Dispose() => _synchronizer.Dispose();
    }
}
