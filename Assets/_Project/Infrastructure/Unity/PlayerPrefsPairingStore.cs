using System;
using ScreenPortal.App.Connection;
using UnityEngine;

namespace ScreenPortal.Infrastructure.Unity
{
    /// <summary>Identidade do headset e tokens de pareamento no armazenamento do app (spec 1B §2).</summary>
    public sealed class PlayerPrefsPairingStore : IPairingStore
    {
        private const string DeviceIdKey = "sp.device_id";
        private const string TokenPrefix = "sp.token.";
        private const string LastHostKey = "sp.last_host";

        public string DeviceId
        {
            get
            {
                string id = PlayerPrefs.GetString(DeviceIdKey, string.Empty);
                if (id.Length > 0) return id;
                id = Guid.NewGuid().ToString("N");
                PlayerPrefs.SetString(DeviceIdKey, id);
                PlayerPrefs.Save();
                return id;
            }
        }

        public string GetToken(string hostName) => PlayerPrefs.GetString(TokenPrefix + hostName, string.Empty);

        public void SaveToken(string hostName, string token)
        {
            PlayerPrefs.SetString(TokenPrefix + hostName, token ?? string.Empty);
            PlayerPrefs.Save();
        }

        public bool TryGetLastHost(out HostEndpoint host) =>
            HostEndpoint.TryParse(PlayerPrefs.GetString(LastHostKey, string.Empty), out host);

        public void SaveLastHost(HostEndpoint host)
        {
            PlayerPrefs.SetString(LastHostKey, host.ToString());
            PlayerPrefs.Save();
        }
    }
}
