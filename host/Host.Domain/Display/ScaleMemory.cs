using System.Text.Json;

namespace ScreenPortal.Host.Domain.Display;

/// <summary>
/// Escala do Windows de cada monitor virtual, por headset e tela (spec 3A §4): o que o usuário escolheu em "Alterar o
/// tamanho do texto…" volta quando o monitor da mesma tela é criado de novo. Persistida em JSON (scales.json).
/// </summary>
public sealed class ScaleMemory
{
    private readonly Dictionary<string, int> _percent;
    private readonly object _lock = new();

    public ScaleMemory() : this(null) { }

    private ScaleMemory(Dictionary<string, int>? percent) =>
        _percent = percent != null ? new Dictionary<string, int>(percent, StringComparer.Ordinal) : new Dictionary<string, int>(StringComparer.Ordinal);

    private static string Key(string deviceId, uint screenKey) => deviceId + "/" + screenKey;

    /// <summary>Escala lida agora no monitor da tela. 0 (monitor não encontrado) é ignorado.</summary>
    /// <returns>true se mudou o que estava guardado (hora de salvar).</returns>
    public bool Observe(string deviceId, uint screenKey, int percent)
    {
        if (percent <= 0) return false;
        lock (_lock)
        {
            var key = Key(deviceId, screenKey);
            if (_percent.TryGetValue(key, out var old) && old == percent) return false;
            _percent[key] = percent;
            return true;
        }
    }

    public bool TryGet(string deviceId, uint screenKey, out int percent)
    {
        lock (_lock) return _percent.TryGetValue(Key(deviceId, screenKey), out percent);
    }

    public string ToJson()
    {
        lock (_lock) return JsonSerializer.Serialize(_percent, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Arquivo ausente, vazio ou corrompido → memória vazia (perder escalas não pode impedir o host de subir).</summary>
    public static ScaleMemory FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ScaleMemory();
        try { return new ScaleMemory(JsonSerializer.Deserialize<Dictionary<string, int>>(json)); }
        catch (JsonException) { return new ScaleMemory(); }
    }
}
