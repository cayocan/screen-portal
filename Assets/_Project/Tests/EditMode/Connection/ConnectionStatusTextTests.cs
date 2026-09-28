using NUnit.Framework;
using ScreenPortal.App.Connection;

namespace ScreenPortal.Tests
{
    public class ConnectionStatusTextTests
    {
        [Test]
        public void PairingCode_IsShownGrouped() =>
            Assert.That(ConnectionStatusText.Format(new ConnectionStatus(ConnectionStatusKind.ConfirmOnPc, "CAYO-PC", "482913")),
                Does.Contain("482 913"));

        [Test]
        public void HostName_AppearsWhenConnecting() =>
            Assert.That(ConnectionStatusText.Format(new ConnectionStatus(ConnectionStatusKind.Connecting, "CAYO-PC")),
                Does.Contain("CAYO-PC"));

        [Test]
        public void EveryKind_HasText()
        {
            foreach (ConnectionStatusKind kind in System.Enum.GetValues(typeof(ConnectionStatusKind)))
                Assert.That(ConnectionStatusText.Format(new ConnectionStatus(kind, "PC", "123456")), Is.Not.Null, kind.ToString());
        }

        [Test]
        public void InstallHost_TellsWhatToDo() =>
            Assert.That(ConnectionStatusText.Format(new ConnectionStatus(ConnectionStatusKind.InstallHost)),
                Does.Contain(ConnectionStatusText.HostInstallHint));

        [Test]
        public void HostEndpoint_RoundTripsThroughString()
        {
            var e = new HostEndpoint("CAYO-PC", "192.168.0.64", 9200);
            Assert.That(HostEndpoint.TryParse(e.ToString(), out var parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(e));
            Assert.That(HostEndpoint.TryParse("lixo", out _), Is.False);
        }
    }
}
