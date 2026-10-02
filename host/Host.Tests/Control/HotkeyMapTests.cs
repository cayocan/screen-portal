using ScreenPortal.App.Connection;
using ScreenPortal.Host.Transport.Control;

namespace ScreenPortal.Host.Tests.Control;

public class HotkeyMapTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Left", HotkeyMap.Ctrl | HotkeyMap.Alt, 0x25u)]
    [InlineData("ctrl + alt + shift + up", HotkeyMap.Ctrl | HotkeyMap.Alt | HotkeyMap.Shift, 0x26u)]
    [InlineData("Ctrl+Alt+PgDn", HotkeyMap.Ctrl | HotkeyMap.Alt, 0x22u)]
    [InlineData("Ctrl+Alt+[", HotkeyMap.Ctrl | HotkeyMap.Alt, 0xDBu)]
    [InlineData("Ctrl+Alt+z", HotkeyMap.Ctrl | HotkeyMap.Alt, (uint)'Z')]
    [InlineData("Win+F12", HotkeyMap.Win, 0x7Bu)]
    public void Parses(string text, uint mods, uint vk)
    {
        Assert.True(HotkeyMap.TryParse(text, out var chord));
        Assert.Equal(new KeyChord(mods, vk), chord);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Z")]            // sem modificador: roubaria a digitação
    [InlineData("Ctrl+Alt")]     // sem tecla
    [InlineData("Ctrl+Banana")]
    [InlineData("Ctrl+Z+Alt")]   // modificador depois da tecla
    public void Rejects(string text) => Assert.False(HotkeyMap.TryParse(text, out _));

    [Fact]
    public void EveryDefault_ParsesAndFormatsBack()
    {
        foreach (var (_, text) in HotkeyMap.Defaults)
        {
            Assert.True(HotkeyMap.TryParse(text, out var chord), text);
            Assert.Equal(text, HotkeyMap.Format(chord));
        }
        Assert.Equal(Enum.GetValues<HotkeyAction>().Length - 2, HotkeyMap.Defaults.Count); // todas menos None e Escape (fixo, só na edição)
    }

    [Fact]
    public void DefaultsJson_LoadsAsTheDefaults()
    {
        var map = HotkeyMap.Load(HotkeyMap.DefaultsJson(), _ => { });
        Assert.Equal(HotkeyMap.Defaults.Count, map.Count);
        Assert.Equal(new KeyChord(HotkeyMap.Ctrl | HotkeyMap.Alt, 0x25), map[HotkeyAction.MoveLeft]);
    }

    [Fact]
    public void File_OverridesDisablesAndFallsBack()
    {
        var logs = new List<string>();
        var map = HotkeyMap.Load("""{ "Undo": "Ctrl+Shift+F1", "Redo": "", "Recenter": "lixo", "Nada": "Ctrl+X" }""", logs.Add);
        Assert.Equal(new KeyChord(HotkeyMap.Ctrl | HotkeyMap.Shift, 0x70), map[HotkeyAction.Undo]);
        Assert.False(map.ContainsKey(HotkeyAction.Redo));
        Assert.Equal(new KeyChord(HotkeyMap.Ctrl | HotkeyMap.Alt, 'R'), map[HotkeyAction.Recenter]);
        Assert.True(map.ContainsKey(HotkeyAction.MoveLeft), "ausente no arquivo = padrão");
        Assert.Equal(2, logs.Count); // texto inválido e ação desconhecida
    }

    [Fact]
    public void BrokenFile_UsesTheDefaults()
    {
        var logs = new List<string>();
        Assert.Equal(HotkeyMap.Defaults.Count, HotkeyMap.Load("{ quebrado", logs.Add).Count);
        Assert.Single(logs);
    }

    [Fact]
    public void Router_ReplacedSessionLeavingLate_DoesNotRemoveTheNewOne()
    {
        var router = new HotkeyRouter();
        var oldSent = new List<ControlMessage>();
        var newSent = new List<ControlMessage>();
        object oldSession = new(), newSession = new();
        router.Register("dev-1", oldSession, oldSent.Add);
        router.Register("dev-1", newSession, newSent.Add);
        router.Unregister("dev-1", oldSession);
        Assert.True(router.Send("dev-1", new HotkeyMessage(HotkeyAction.Recenter, 0)));
        Assert.Empty(oldSent);
        Assert.Single(newSent);
    }
}
