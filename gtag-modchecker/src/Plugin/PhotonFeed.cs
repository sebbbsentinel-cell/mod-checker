using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using FanGame.ModChecker;
using Photon.Pun;
using Photon.Realtime;
using MC = FanGame.ModChecker.ModChecker;

namespace GTagModChecker
{
    /// <summary>
    /// Leser spillere og custom properties fra PUN2, og teller innkommende nettverkshendelser per avsender.
    /// KUN LESING: sender ingenting, endrer ingen properties, kaller ingen RPC-er.
    /// </summary>
    internal sealed class PhotonFeed : IDisposable
    {
        // PUN2 interne event-koder (PunEvent): https://doc-api.photonengine.com/en/pun/v2/
        private const byte EvRpc = 200, EvSerialize = 201, EvInstantiate = 202, EvDestroy = 204,
                           EvSerializeReliable = 206, EvDestroyPlayer = 207, EvOwnershipRequest = 209,
                           EvOwnershipTransfer = 210, EvPropertiesChanged = 253;

        private readonly MC _checker;
        private readonly HashSet<string> _known = new HashSet<string>();
        private LoadBalancingClient _client;
        public bool NetworkChecksEnabled = true;
        public int PlayerCount => _known.Count;

        public PhotonFeed(MC checker) { _checker = checker; }

        private static readonly Dictionary<string, DateTime> _firstSeen = new Dictionary<string, DateTime>();
        /// <summary>Lokal klokkeslett da pluginen først så spilleren i rommet (null hvis ukjent).</summary>
        public static DateTime? FirstSeen(string id) => id != null && _firstSeen.TryGetValue(id, out var t) ? t : (DateTime?)null;

        /// <summary>True hvis spilleren er i rommet nå (fra siste Sync).</summary>
        public bool IsKnown(string id) => _known.Contains(id);

        public static string Id(int actorNumber) => "actor:" + actorNumber;

        public static bool InRoom
        {
            get { try { return PhotonNetwork.InRoom; } catch { return false; } }
        }

        public static int LocalActor
        {
            get { try { return PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1; } catch { return -1; } }
        }

        public static string LocalName
        {
            get { try { return Sanitize(PhotonNetwork.LocalPlayer?.NickName); } catch { return null; } }
        }

        /// <summary>Romnavn (kun lesing), eller null.</summary>
        public static string RoomName
        {
            get { try { return PhotonNetwork.CurrentRoom != null ? Sanitize(PhotonNetwork.CurrentRoom.Name) : null; } catch { return null; } }
        }

        /// <summary>Spillmodus fra rommets egne properties ("gameMode"), eller null hvis den ikke kan leses.</summary>
        public static string RoomGameMode()
        {
            try
            {
                var props = PhotonNetwork.CurrentRoom?.CustomProperties;
                if (props == null || !props.ContainsKey("gameMode")) return null;
                object v = props["gameMode"];
                if (v == null) return null;
                string s = v.ToString().Replace("<", "").Replace(">", "");
                return s.Length > 32 ? s.Substring(0, 32) : s;
            }
            catch { return null; }
        }

        /// <summary>Photon UserId for en spiller (i Gorilla Tag er dette PlayFab-ID-en), eller null. Kun lesing.</summary>
        public static string UserId(int actor)
        {
            try
            {
                var others = PhotonNetwork.PlayerListOthers;
                if (others == null) return null;
                foreach (Player p in others)
                    if (p != null && p.ActorNumber == actor)
                        return string.IsNullOrEmpty(p.UserId) ? null : p.UserId.Replace("<", "").Replace(">", "");
            }
            catch { }
            return null;
        }

        /// <summary>Gjør "actor:12" om til 12, ellers -1.</summary>
        public static int ActorOf(string id)
        {
            if (id != null && id.StartsWith("actor:") && int.TryParse(id.Substring(6), out int a)) return a;
            return -1;
        }

        /// <summary>Full synk av spillerliste + properties. Kalles hvert PlayerSyncInterval.</summary>
        public void Sync(double now)
        {
            EnsureSubscribed();
            if (!InRoom)
            {
                if (_known.Count > 0) { foreach (var id in _known) _checker.RemovePlayer(id); _known.Clear(); _firstSeen.Clear(); }
                return;
            }

            var present = new HashSet<string>();
            Player[] others = PhotonNetwork.PlayerListOthers;
            if (others != null)
            {
                foreach (Player p in others)
                {
                    if (p == null) continue;
                    string id = Id(p.ActorNumber);
                    present.Add(id);
                    if (_known.Add(id)) { _checker.RegisterPlayer(id, now); _firstSeen[id] = DateTime.Now; } // starter grace-perioden

                    var props = ToDict(p.CustomProperties);
                    _checker.UpdatePlayerProperties(id, props, now, replaceAll: true, countAsNetworkEvent: false);
                    _checker.UpdatePlayerInfo(id, new PlayerInfo
                    {
                        DisplayName = Sanitize(p.NickName),
                        Platform = Get(props, "platform") ?? Get(props, "didPlatform")
                    }, now);
                }
            }

            _known.RemoveWhere(id =>
            {
                if (present.Contains(id)) return false;
                _checker.RemovePlayer(id);
                _firstSeen.Remove(id);
                return true;
            });
        }

        private void EnsureSubscribed()
        {
            try
            {
                var c = PhotonNetwork.NetworkingClient;
                if (c == null || c == _client) return;
                if (_client != null) _client.EventReceived -= OnEvent;
                _client = c;
                _client.EventReceived += OnEvent; // passiv lytter
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[GTag Mod Checker] Kunne ikke lytte på Photon-hendelser: " + ex.Message); }
        }

        private void OnEvent(EventData ev)
        {
            if (!NetworkChecksEnabled || ev == null) return;
            try
            {
                int sender = ev.Sender;
                if (sender <= 0) return;
                var local = PhotonNetwork.LocalPlayer;
                if (local != null && sender == local.ActorNumber) return;

                NetworkEventType type;
                switch (ev.Code)
                {
                    case EvRpc: type = NetworkEventType.Rpc; break;
                    case EvInstantiate: type = NetworkEventType.Instantiate; break;
                    case EvDestroy: case EvDestroyPlayer: type = NetworkEventType.Destroy; break;
                    case EvOwnershipRequest: case EvOwnershipTransfer: type = NetworkEventType.OwnershipTake; break;
                    case EvPropertiesChanged: type = NetworkEventType.PropertyChange; break;
                    case EvSerialize: case EvSerializeReliable: return; // vanlig posisjonsstrøm
                    default:
                        if (ev.Code >= 200) return; // andre interne Photon-hendelser
                        type = NetworkEventType.CustomEvent; break;
                }
                _checker.RecordNetworkEvent(new NetworkEventRecord
                {
                    PlayerId = Id(sender),
                    Type = type,
                    Name = "ev" + ev.Code,
                    Timestamp = UnityEngine.Time.time
                });
            }
            catch { /* aldri krasj Photon sin event-løkke */ }
        }

        private static Dictionary<string, object> ToDict(Hashtable table)
        {
            var d = new Dictionary<string, object>();
            if (table == null) return d;
            foreach (object key in table.Keys)
            {
                if (key == null) continue;
                object v = table[key];
                d[key.ToString()] = v == null ? null : SafeString(v);
            }
            return d;
        }

        private static string SafeString(object v)
        {
            try
            {
                if (v is Array arr) return "[" + arr.Length + " items]";
                string s = v.ToString();
                return s.Length > 256 ? s.Substring(0, 256) : s;
            }
            catch { return "?"; }
        }

        private static string Get(Dictionary<string, object> d, string k) =>
            d.TryGetValue(k, out var v) && v != null ? v.ToString() : null;

        /// <summary>Fjerner tegn som kan ødelegge rich text i panelet.</summary>
        public static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            s = s.Replace("<", "").Replace(">", "");
            return s.Length > 20 ? s.Substring(0, 20) : s;
        }

        public void Dispose()
        {
            try { if (_client != null) _client.EventReceived -= OnEvent; } catch { }
            _client = null;
        }
    }
}
