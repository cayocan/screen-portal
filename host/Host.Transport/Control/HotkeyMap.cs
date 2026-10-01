using System.Text.Json;
using ScreenPortal.App.Connection;

namespace ScreenPortal.Host.Transport.Control;

/// <summary>Tecla com modificadores, nos códigos do RegisterHotKey (MOD_* e virtual-key).</summary>
public readonly record struct KeyChord(uint Modifiers, uint VirtualKey);

/// <summary>
/// Atalhos globais (spec 3A §5): texto ("Ctrl+Alt+Left") ↔ teclas, padrões e o hotkeys.json (ação → texto). Ação
/// ausente no arquivo fica com o padrão; texto vazio desliga a ação; texto inválido volta ao padrão (com log).
/// </summary>
public static class HotkeyMap
{
    public const uint Alt = 0x1, Ctrl = 0x2, Shift = 0x4, Win = 0x8;

    public static readonly IReadOnlyDictionary<HotkeyAction, string> Defaults = new Dictionary<HotkeyAction, string>
    {
        [HotkeyAction.MoveLeft] = "Ctrl+Alt+Left",
        [HotkeyAction.MoveRight] = "Ctrl+Alt+Right",
        [HotkeyAction.MoveUp] = "Ctrl+Alt+Up",
        [HotkeyAction.MoveDown] = "Ctrl+Alt+Down",
        [HotkeyAction.Bigger] = "Ctrl+Alt+Shift+Up",
        [HotkeyAction.Smaller] = "Ctrl+Alt+Shift+Down",
        [HotkeyAction.Closer] = "Ctrl+Alt+PageUp",
        [HotkeyAction.Farther] = "Ctrl+Alt+PageDown",
        [HotkeyAction.CurvatureToggle] = "Ctrl+Alt+C",
        [HotkeyAction.CurvatureLess] = "Ctrl+Alt+[",
        [HotkeyAction.CurvatureMore] = "Ctrl+Alt+]",
        [HotkeyAction.CycleFrameRate] = "Ctrl+Alt+F",
        [HotkeyAction.Undo] = "Ctrl+Alt+Z",
        [HotkeyAction.Redo] = "Ctrl+Alt+Y",
        [HotkeyAction.Recenter] = "Ctrl+Alt+R",
        [HotkeyAction.ToggleLayoutMode] = "Ctrl+Alt+L",
        [HotkeyAction.AlignPosture] = "Ctrl+Alt+T",
        [HotkeyAction.ToggleGrid] = "Ctrl+Alt+G",
        [HotkeyAction.ToggleEditMode] = "Ctrl+Alt+E",
        [HotkeyAction.FindCursor] = "Ctrl+Alt+Home",
    };

    /// <summary>Ações que repetem com a tecla segurada (mover, tamanho, distância, curvatura).</summary>
    public static bool Repeats(HotkeyAction action) => action is >= HotkeyAction.MoveLeft and <= HotkeyAction.Farther
        or HotkeyAction.CurvatureLess or HotkeyAction.CurvatureMore;

    private static readonly Dictionary<string, uint> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
        ["PageUp"] = 0x21, ["PgUp"] = 0x21, ["PageDown"] = 0x22, ["PgDn"] = 0x22,
        ["Home"] = 0x24, ["End"] = 0x23, ["Insert"] = 0x2D, ["Delete"] = 0x2E, ["Space"] = 0x20,
        ["["] = 0xDB, ["]"] = 0xDD, [";"] = 0xBA, ["="] = 0xBB, [","] = 0xBC, ["-"] = 0xBD, ["."] = 0xBE, ["/"] = 0xBF,
    };

    public static bool TryParse(string text, out KeyChord chord)
    {
        chord = default;
        uint mods = 0, key = 0;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i];
            bool last = i == parts.Length - 1;
            uint mod = p.ToLowerInvariant() switch { "ctrl" or "control" => Ctrl, "alt" => Alt, "shift" => Shift, "win" => Win, _ => 0 };
            if (mod != 0 && !last) { mods |= mod; continue; }
            if (!last || !TryKey(p, out key)) return false;
        }
        if (mods == 0) return false; // tecla sozinha como atalho global roubaria a digitação
        chord = new KeyChord(mods, key);
        return true;
    }

    private static bool TryKey(string p, out uint key)
    {
        if (Named.TryGetValue(p, out key)) return true;
        if (p.Length == 1 && char.IsAsciiLetterOrDigit(p[0])) { key = char.ToUpperInvariant(p[0]); return true; }
        if (p.Length is 2 or 3 && (p[0] is 'F' or 'f') && int.TryParse(p.AsSpan(1), out int f) && f is >= 1 and <= 24)
        {
            key = (uint)(0x70 + f - 1);
            return true;
        }
        key = 0;
        return false;
    }

    public static string Format(KeyChord chord)
    {
        var parts = new List<string>();
        if ((chord.Modifiers & Ctrl) != 0) parts.Add("Ctrl");
        if ((chord.Modifiers & Alt) != 0) parts.Add("Alt");
        if ((chord.Modifiers & Shift) != 0) parts.Add("Shift");
        if ((chord.Modifiers & Win) != 0) parts.Add("Win");
        string? named = null;
        foreach (var (name, vk) in Named) if (vk == chord.VirtualKey) { named = name; break; }
        parts.Add(named ?? (chord.VirtualKey is >= 0x70 and <= 0x87 ? "F" + (chord.VirtualKey - 0x70 + 1) : ((char)chord.VirtualKey).ToString()));
        return string.Join("+", parts);
    }

    /// <summary>O hotkeys.json com os padrões (gerado na primeira vez, para o usuário editar).</summary>
    public static string DefaultsJson() =>
        JsonSerializer.Serialize(Defaults.ToDictionary(p => p.Key.ToString(), p => p.Value), new JsonSerializerOptions { WriteIndented = true });

    public static Dictionary<HotkeyAction, KeyChord> Load(string? json, Action<string> log)
    {
        Dictionary<string, string>? file = null;
        if (!string.IsNullOrWhiteSpace(json))
        {
            try { file = JsonSerializer.Deserialize<Dictionary<string, string>>(json); }
            catch (JsonException e) { log($"atalhos: hotkeys.json inválido ({e.Message}); usando os padrões"); }
        }
        var texts = new Dictionary<HotkeyAction, string>(Defaults);
        if (file != null)
        {
            foreach (var (name, text) in file)
            {
                if (!Enum.TryParse<HotkeyAction>(name, true, out var action) || action == HotkeyAction.None)
                {
                    log($"atalhos: ação desconhecida '{name}' ignorada");
                    continue;
                }
                texts[action] = text ?? "";
            }
        }
        var map = new Dictionary<HotkeyAction, KeyChord>();
        foreach (var (action, text) in texts)
        {
            if (text.Trim().Length == 0) continue; // desligada pelo usuário
            if (TryParse(text, out var chord)) map[action] = chord;
            else if (TryParse(Defaults[action], out var fallback))
            {
                log($"atalhos: '{text}' ({action}) não é um atalho válido; usando {Defaults[action]}");
                map[action] = fallback;
            }
        }
        return map;
    }
}
