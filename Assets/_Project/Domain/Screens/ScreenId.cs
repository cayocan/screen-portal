using System;

namespace ScreenPortal.Domain.Screens
{
    /// <summary>Identidade de uma tela na workspace. 0 = nenhuma.</summary>
    public readonly struct ScreenId : IEquatable<ScreenId>
    {
        public static readonly ScreenId None = default;

        public readonly int Value;

        public ScreenId(int value) => Value = value;

        public bool IsNone => Value == 0;

        public bool Equals(ScreenId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ScreenId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => IsNone ? "screen#none" : "screen#" + Value;

        public static bool operator ==(ScreenId a, ScreenId b) => a.Value == b.Value;
        public static bool operator !=(ScreenId a, ScreenId b) => a.Value != b.Value;
    }
}
