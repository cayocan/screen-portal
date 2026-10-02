using System;

namespace ScreenPortal.Domain.Screens
{
    public enum ScreenSourceKind
    {
        /// <summary>Monitor virtual (SudoVDA) criado pelo host com a resolução exata da tela.</summary>
        Virtual = 0,
        /// <summary>Espelho de um monitor físico do PC.</summary>
        Mirror = 1,
        /// <summary>Tela de teste, sem vídeo (2B).</summary>
        Test = 2,
        /// <summary>3D: janela do Windows espelhada (ADR 0018).</summary>
        Window = 3,
    }

    /// <summary>De onde vem a imagem da tela (spec 2C §3). <c>default</c> = Virtual.</summary>
    public readonly struct ScreenSource : IEquatable<ScreenSource>
    {
        public static readonly ScreenSource Virtual = new ScreenSource(ScreenSourceKind.Virtual, 0);
        public static readonly ScreenSource Test = new ScreenSource(ScreenSourceKind.Test, 0);

        public readonly ScreenSourceKind Kind;
        /// <summary>Só em <see cref="ScreenSourceKind.Mirror"/>: id do monitor físico (lista do Welcome).</summary>
        public readonly uint PhysicalMonitorId;

        private ScreenSource(ScreenSourceKind kind, uint physicalMonitorId)
        {
            Kind = kind;
            PhysicalMonitorId = physicalMonitorId;
        }

        public static ScreenSource Mirror(uint physicalMonitorId) => new ScreenSource(ScreenSourceKind.Mirror, physicalMonitorId);

        /// <summary>Janela espelhada; o id da janela fica no mesmo campo (<see cref="WindowId"/>).</summary>
        public static ScreenSource Window(uint windowId) => new ScreenSource(ScreenSourceKind.Window, windowId);

        public uint WindowId => Kind == ScreenSourceKind.Window ? PhysicalMonitorId : 0;

        /// <summary>Virtual e espelho recebem vídeo do PC; a tela de teste não.</summary>
        public bool HasVideo => Kind != ScreenSourceKind.Test;

        public bool Equals(ScreenSource other) => Kind == other.Kind && PhysicalMonitorId == other.PhysicalMonitorId;
        public override bool Equals(object obj) => obj is ScreenSource other && Equals(other);
        public override int GetHashCode() => ((int)Kind * 397) ^ (int)PhysicalMonitorId;
        public override string ToString() => Kind == ScreenSourceKind.Mirror ? "Mirror(" + PhysicalMonitorId + ")"
            : Kind == ScreenSourceKind.Window ? "Window(" + PhysicalMonitorId + ")" : Kind.ToString();
    }
}
