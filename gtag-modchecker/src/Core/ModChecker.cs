using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace FanGame.ModChecker
{
    /// <summary>
    /// Motoruavhengig mod-/cheat-sjekker for ditt eget spill.
    /// Kombinerer property-matching, bevegelsessjekker og nettverkssjekker.
    /// Trådsikker: alle offentlige metoder låser intern tilstand. Events fyres utenfor låsen.
    /// </summary>
    public sealed class ModChecker : IDisposable
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, PlayerState> _players = new Dictionary<string, PlayerState>();
        private readonly Dictionary<string, UnknownPropertyRecord> _unknown =
            new Dictionary<string, UnknownPropertyRecord>(StringComparer.OrdinalIgnoreCase);
        private ModList _modList;
        private FileSystemWatcher _watcher;
        private Timer _reloadDebounce;
        private string _watchedPath;

        public ModCheckerConfig Config { get; }
        /// <summary>Høyeste tidsstempel sett så langt (brukes som "nå" i Evaluate).</summary>
        public double CurrentTime { get; private set; }

        /// <summary>Fyres når REPORT-handlingen brukes (RequestReport). Spillet sender selv rapporten videre (server, Discord-webhook o.l.).</summary>
        public event Action<PlayerReport> OnReportRequested;

        /// <summary>REPORT-knappen: evaluerer spilleren og fyrer OnReportRequested med rapporten som bevis.</summary>
        public PlayerReport RequestReport(string playerId)
        {
            var r = Evaluate(playerId);
            OnReportRequested?.Invoke(r);
            return r;
        }

        /// <summary>Fyres når en spillers verdikt blir verre (CLEAN→SUS, SUS→CHEATING osv.).</summary>
        public event EventHandler<PlayerFlaggedEventArgs> OnPlayerFlagged;
        /// <summary>Fyres første gang en ukjent property-nøkkel sees.</summary>
        public event Action<UnknownPropertyRecord> OnUnknownProperty;
        /// <summary>Fyres etter (forsøk på) reload av modlista: (ok, melding).</summary>
        public event Action<bool, string> OnModListReloaded;

        public ModChecker(ModCheckerConfig config = null, ModList modList = null)
        {
            Config = config ?? new ModCheckerConfig();
            _modList = modList ?? ModList.LoadEmbeddedDefault();
        }

        public ModList CurrentModList { get { lock (_lock) return _modList; } }

        // =====================================================================
        //  Modliste / live reload
        // =====================================================================

        public void SetModList(ModList list)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            lock (_lock) { _modList = list; RecheckAllPropertiesLocked(); }
        }

        /// <summary>Laster modliste fra fil. Ved feil beholdes gammel liste og false returneres.</summary>
        public bool ReloadModList(string path)
        {
            try
            {
                var list = ModList.LoadFromFile(path);
                SetModList(list);
                OnModListReloaded?.Invoke(true, $"Lastet {list.Entries.Count} oppføringer fra {path}");
                return true;
            }
            catch (Exception ex)
            {
                OnModListReloaded?.Invoke(false, $"Kunne ikke laste {path}: {ex.Message}");
                return false;
            }
        }

        /// <summary>Laster fila nå og laster den på nytt automatisk hver gang den endres.</summary>
        public bool WatchModListFile(string path)
        {
            StopWatching();
            var full = Path.GetFullPath(path);
            bool ok = ReloadModList(full);
            _watchedPath = full;
            _reloadDebounce = new Timer(_ => ReloadModList(_watchedPath), null, Timeout.Infinite, Timeout.Infinite);
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(full), Path.GetFileName(full))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };
            FileSystemEventHandler h = (s, e) => _reloadDebounce?.Change(300, Timeout.Infinite);
            _watcher.Changed += h;
            _watcher.Created += h;
            _watcher.Renamed += (s, e) => _reloadDebounce?.Change(300, Timeout.Infinite);
            _watcher.EnableRaisingEvents = true;
            return ok;
        }

        public void StopWatching()
        {
            _watcher?.Dispose(); _watcher = null;
            _reloadDebounce?.Dispose(); _reloadDebounce = null;
        }

        public void Dispose() => StopWatching();

        // =====================================================================
        //  Spillere
        // =====================================================================

        /// <summary>Registrer at en spiller har joinet (starter grace-perioden). Kalles automatisk ved første data.</summary>
        public void RegisterPlayer(string playerId, double joinTime)
        {
            lock (_lock)
            {
                Touch(joinTime);
                if (!_players.TryGetValue(playerId, out var st))
                    _players[playerId] = new PlayerState { PlayerId = playerId, JoinTime = joinTime };
                else st.JoinTime = joinTime;
            }
        }

        public void RemovePlayer(string playerId) { lock (_lock) _players.Remove(playerId); }

        public IReadOnlyList<string> PlayerIds { get { lock (_lock) return _players.Keys.ToList(); } }

        /// <summary>Oppdater valgfri spillerinfo (FPS, Hz, ping, plattform, farge). Null-felt beholder forrige verdi.</summary>
        public void UpdatePlayerInfo(string playerId, PlayerInfo info, double timestamp)
        {
            if (info == null) return;
            lock (_lock)
            {
                Touch(timestamp);
                var st = GetOrCreate(playerId, timestamp);
                var cur = st.Info ?? new PlayerInfo();
                cur.DisplayName = info.DisplayName ?? cur.DisplayName;
                cur.Fps = info.Fps ?? cur.Fps;
                cur.RefreshRateHz = info.RefreshRateHz ?? cur.RefreshRateHz;
                cur.PingMs = info.PingMs ?? cur.PingMs;
                cur.Platform = info.Platform ?? cur.Platform;
                cur.Color = info.Color ?? cur.Color;
                st.Info = cur;
            }
        }

        // =====================================================================
        //  1) Property-matching
        // =====================================================================

        /// <summary>
        /// Oppdater custom properties for en spiller (f.eks. fra Photon OnPlayerPropertiesUpdate).
        /// Endringer flettes inn; verdi null fjerner nøkkelen. replaceAll=true erstatter hele settet.
        /// countAsNetworkEvent=false for periodisk full synk (teller da ikke mot PROPERTY_SPAM).
        /// </summary>
        public void UpdatePlayerProperties(string playerId, IDictionary<string, object> properties, double timestamp, bool replaceAll = false, bool countAsNetworkEvent = true)
        {
            if (properties == null) return;
            List<UnknownPropertyRecord> newUnknown;
            lock (_lock)
            {
                Touch(timestamp);
                var st = GetOrCreate(playerId, timestamp);
                if (replaceAll) st.Properties.Clear();
                foreach (var kv in properties)
                {
                    if (kv.Key == null) continue;
                    if (kv.Value == null) st.Properties.Remove(kv.Key);
                    else st.Properties[kv.Key] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
                }
                newUnknown = CollectUnknownLocked(st, properties.Keys, timestamp);
                if (countAsNetworkEvent) RecordEventLocked(st, NetworkEventType.PropertyChange, null, timestamp, null);
            }
            foreach (var u in newUnknown) OnUnknownProperty?.Invoke(u);
        }

        private List<UnknownPropertyRecord> CollectUnknownLocked(PlayerState st, IEnumerable<string> keys, double t)
        {
            var fresh = new List<UnknownPropertyRecord>();
            foreach (var key in keys)
            {
                if (key == null || !st.Properties.TryGetValue(key, out var val)) continue;
                if (_modList.IsAllowed(key) || _modList.Match(key, val).Any()) continue;
                if (!_unknown.TryGetValue(key, out var rec))
                {
                    rec = new UnknownPropertyRecord { Key = key, SampleValue = Truncate(val, 128), FirstSeen = t };
                    _unknown[key] = rec;
                    fresh.Add(rec);
                }
                rec.LastSeen = t;
                rec.Count++;
                rec.PlayerIds.Add(st.PlayerId);
            }
            return fresh;
        }

        private void RecheckAllPropertiesLocked()
        {
            // Fjern innboks-oppføringer som nå er kjent/allowlistet etter reload.
            foreach (var key in _unknown.Keys.ToList())
            {
                var sample = _unknown[key].SampleValue;
                if (_modList.IsAllowed(key) || _modList.Match(key, sample).Any()) _unknown.Remove(key);
            }
        }

        private List<Detection> PropertyDetections(PlayerState st, PlayerReport report)
        {
            var result = new List<Detection>();
            var seen = new HashSet<ModEntry>();
            foreach (var kv in st.Properties)
            {
                if (_modList.IsAllowed(kv.Key)) continue;
                foreach (var e in _modList.Match(kv.Key, kv.Value))
                {
                    if (!seen.Add(e)) continue;
                    var name = e.Label ?? e.Pattern;
                    var list = e.Category == ModCategory.Cheat ? report.Cheats : e.Category == ModCategory.Mod ? report.AllowedMods : report.Unsure;
                    if (!list.Contains(name)) list.Add(name);
                    if (e.Confidence <= 0f) continue; // f.eks. lovlige mods: vises, men påvirker ikke score
                    string code = e.Category == ModCategory.Cheat ? "PROP_CHEAT" : e.Category == ModCategory.Mod ? "PROP_MOD" : "PROP_UNSURE";
                    result.Add(new Detection
                    {
                        Category = DetectionCategory.Property,
                        Code = code,
                        Label = e.Label ?? e.Pattern,
                        Confidence = e.Confidence,
                        Reason = $"Property '{kv.Key}'='{Truncate(kv.Value, 40)}' matchet {e.MatchType.ToString().ToLowerInvariant()}-mønster '{e.Pattern}' ({e.Category.ToString().ToLowerInvariant()})"
                    });
                }
            }
            return result;
        }

        // Ukjent-innboks
        public IReadOnlyList<UnknownPropertyRecord> GetUnknownProperties()
        {
            lock (_lock) return _unknown.Values.OrderByDescending(u => u.Count).ToList();
        }
        public void ClearUnknownProperty(string key) { lock (_lock) _unknown.Remove(key); }
        public void ClearUnknownInbox() { lock (_lock) _unknown.Clear(); }

        /// <summary>Legg en nøkkel til allowlist i minnet (husk å oppdatere mods.json også).</summary>
        public void AddToAllowlist(string pattern, MatchType matchType = MatchType.Exact)
        {
            lock (_lock)
            {
                var json = _modList.ToJson();
                var copy = ModList.FromJson(json);
                copy.Allowlist.Add(new AllowEntry { Pattern = pattern, MatchType = matchType });
                _modList = ModList.FromJson(copy.ToJson());
                RecheckAllPropertiesLocked();
            }
        }

        // =====================================================================
        //  2) Bevegelse / rig
        // =====================================================================

        /// <summary>Send inn et snapshot av spillerens rig (body/hode/hender/skala/grounded).</summary>
        public void RecordSnapshot(string playerId, PlayerSnapshot snap)
        {
            if (snap == null) return;
            lock (_lock)
            {
                Touch(snap.Timestamp);
                var st = GetOrCreate(playerId, snap.Timestamp);
                var prev = st.Latest;
                if (prev != null && snap.Timestamp <= prev.Timestamp) return; // ute av rekkefølge
                st.Snapshots.AddLast(snap);
                while (st.Snapshots.Count > Math.Max(2, Config.SnapshotHistory)) st.Snapshots.RemoveFirst();

                bool grace = InGrace(st, snap.Timestamp);
                var c = Config;
                float scale = snap.Scale <= 0 ? 1f : snap.Scale;

                // Skala
                if (!grace && (snap.Scale < c.MinScale || snap.Scale > c.MaxScale))
                    AddViolation(st, "SCALE", snap.Timestamp, $"Skala {snap.Scale:0.00} utenfor [{c.MinScale:0.00}, {c.MaxScale:0.00}]");

                // Armlengde
                float reachL = Vec3.Distance(snap.HeadPosition, snap.LeftHandPosition);
                float reachR = Vec3.Distance(snap.HeadPosition, snap.RightHandPosition);
                float reach = Math.Max(reachL, reachR);
                float maxReach = c.MaxArmReach * scale;
                if (!grace && reach > maxReach)
                    AddViolation(st, "ARM_REACH", snap.Timestamp, $"Hånd {reach:0.00} m fra hodet (maks {maxReach:0.00} m)");

                // Luft-tid / flyging
                if (snap.Grounded || snap.LegitTeleport) st.AirborneSince = null;
                else if (st.AirborneSince == null) { st.AirborneSince = snap.Timestamp; st.AirborneStartY = snap.BodyPosition.Y; }
                else
                {
                    double air = snap.Timestamp - st.AirborneSince.Value;
                    float dy = snap.BodyPosition.Y - st.AirborneStartY;
                    if (!grace && air > c.MaxAirtime && dy > c.FlyMinRise)
                        AddViolation(st, "FLY", snap.Timestamp, $"I lufta {air:0.0} s uten å falle (ΔY {dy:+0.00;-0.00} m)");
                }

                // Fart / teleport
                if (snap.LegitTeleport) { st.SpeedStreak = 0; st.SpeedSuppressUntil = snap.Timestamp + c.SpeedSuppressAfterJump; }
                else if (prev != null)
                {
                    double dt = snap.Timestamp - prev.Timestamp;
                    if (dt > 0 && dt <= c.MaxSnapshotGap)
                    {
                        float dist = Vec3.Distance(snap.BodyPosition, prev.BodyPosition);
                        if (!grace && dist > c.TeleportDistance * scale)
                            AddViolation(st, "TELEPORT", snap.Timestamp, $"Flyttet {dist:0.0} m på {dt * 1000:0} ms");

                        if (dist > c.SpeedJumpIgnore * scale)
                        {
                            // Respawn, portal eller stort lagg-hopp: ikke en fartstopp. Men mange hopp tett på hverandre er ikke respawn.
                            st.SpeedStreak = 0;
                            st.SpeedSuppressUntil = snap.Timestamp + c.SpeedSuppressAfterJump;
                            st.JumpTimes.Enqueue(snap.Timestamp);
                            while (st.JumpTimes.Count > 0 && st.JumpTimes.Peek() < snap.Timestamp - c.SpeedJumpWindow) st.JumpTimes.Dequeue();
                            if (!grace && st.JumpTimes.Count >= Math.Max(2, c.SpeedRepeatedJumps))
                                AddViolation(st, "SPEED", snap.Timestamp, $"{st.JumpTimes.Count} hopp over {c.SpeedJumpIgnore * scale:0} m på {c.SpeedJumpWindow:0.#} s (siste {dist:0.0} m)");
                        }
                        else if (!grace && snap.Timestamp >= st.SpeedSuppressUntil)
                        {
                            float speed = WindowedSpeed(st, snap, c.SpeedWindow, c.MaxSnapshotGap);
                            if (speed > c.MaxSpeed * scale)
                            {
                                st.SpeedStreak++;
                                if (st.SpeedStreak >= Math.Max(1, c.SpeedSustainSamples))
                                    AddViolation(st, "SPEED", snap.Timestamp, $"Fart {speed:0.0} m/s i {st.SpeedStreak} målinger på rad (maks {c.MaxSpeed * scale:0.0})");
                            }
                            else st.SpeedStreak = 0;
                        }
                    }
                    else st.SpeedStreak = 0; // hull i dataene: start på nytt
                }
            }
        }

        /// <summary>Kall når en spiller tagger en annen. Sjekker avstanden fra taggerens hender til målet.</summary>
        public void RecordTag(string taggerId, string targetId, double timestamp)
        {
            lock (_lock)
            {
                Touch(timestamp);
                var tagger = GetOrCreate(taggerId, timestamp);
                if (!_players.TryGetValue(targetId, out var target)) return;
                var a = tagger.Latest; var b = target.Latest;
                if (a == null || b == null || InGrace(tagger, timestamp)) return;
                float d = Math.Min(Vec3.Distance(a.LeftHandPosition, b.BodyPosition), Vec3.Distance(a.RightHandPosition, b.BodyPosition));
                float max = Config.MaxTagDistance * (a.Scale <= 0 ? 1f : a.Scale);
                if (d > max)
                    AddViolation(tagger, "TAG_REACH", timestamp, $"Tagget {targetId} fra {d:0.00} m (maks {max:0.00} m)");
            }
        }

        // =====================================================================
        //  3) Nettverk
        // =====================================================================

        /// <summary>Registrer en nettverkshendelse (RPC, event, instantiate, destroy, ownership-overtakelse ...).</summary>
        public void RecordNetworkEvent(NetworkEventRecord ev)
        {
            if (ev == null || ev.PlayerId == null) return;
            lock (_lock)
            {
                Touch(ev.Timestamp);
                var st = GetOrCreate(ev.PlayerId, ev.Timestamp);
                RecordEventLocked(st, ev.Type, ev.Name, ev.Timestamp, ev.TargetPlayerId);
            }
        }

        private void RecordEventLocked(PlayerState st, NetworkEventType type, string name, double t, string target)
        {
            if (!st.Events.TryGetValue(type, out var q)) st.Events[type] = q = new Queue<double>();
            q.Enqueue(t);
            while (q.Count > 0 && q.Peek() < t - Config.RateWindow) q.Dequeue();
            if (InGrace(st, t)) return;

            int limit = LimitFor(type);
            if (q.Count > limit)
            {
                string code = RateCode(type);
                // Struping: maks ett brudd per halve vindu, så én kort burst ikke alene gir treff.
                if (!st.Violations.TryGetValue(code, out var vq) || vq.Count == 0 || t - LastOf(vq) >= Config.RateWindow / 2)
                    AddViolation(st, code, t, $"{q.Count} {type}-hendelser på {Config.RateWindow:0.#} s (maks {limit}){(target != null ? $", mål {target}" : "")}");
            }

            if (type == NetworkEventType.Rpc && Config.FlagUnknownRpcs && name != null
                && _modList.HasRpcAllowlist && !_modList.IsRpcAllowed(name))
                st.UnknownRpcs.Add(name);
        }

        private int LimitFor(NetworkEventType t)
        {
            var c = Config;
            switch (t)
            {
                case NetworkEventType.Rpc: return c.MaxRpcPerWindow;
                case NetworkEventType.CustomEvent: return c.MaxCustomEventsPerWindow;
                case NetworkEventType.Instantiate: return c.MaxInstantiatePerWindow;
                case NetworkEventType.Destroy: return c.MaxDestroyPerWindow;
                case NetworkEventType.OwnershipTake: return c.MaxOwnershipTakesPerWindow;
                case NetworkEventType.PropertyChange: return c.MaxPropertyChangesPerWindow;
                default: return int.MaxValue;
            }
        }

        private static string RateCode(NetworkEventType t)
        {
            switch (t)
            {
                case NetworkEventType.Rpc: return "RPC_RATE";
                case NetworkEventType.CustomEvent: return "EVENT_RATE";
                case NetworkEventType.Instantiate: return "INSTANTIATE_RATE";
                case NetworkEventType.Destroy: return "DESTROY_RATE";
                case NetworkEventType.OwnershipTake: return "OWNERSHIP_STEAL";
                case NetworkEventType.PropertyChange: return "PROPERTY_SPAM";
                default: return "NET_RATE";
            }
        }

        // =====================================================================
        //  Evaluering
        // =====================================================================

        /// <summary>Lag rapport for én spiller. Fyrer OnPlayerFlagged hvis verdiktet ble verre.</summary>
        public PlayerReport Evaluate(string playerId)
        {
            PlayerReport report; PlayerFlaggedEventArgs flagged = null;
            lock (_lock)
            {
                if (!_players.TryGetValue(playerId, out var st))
                    return new PlayerReport { PlayerId = playerId, Verdict = Verdict.CLEAN, EvaluatedAt = CurrentTime };
                report = BuildReportLocked(st, CurrentTime);
                if (report.Verdict > st.LastVerdict)
                    flagged = new PlayerFlaggedEventArgs { Report = report, PreviousVerdict = st.LastVerdict };
                st.LastVerdict = report.Verdict;
            }
            if (flagged != null) OnPlayerFlagged?.Invoke(this, flagged);
            return report;
        }

        /// <summary>Rapporter for alle kjente spillere, sortert med verste først.</summary>
        public List<PlayerReport> EvaluateAll()
        {
            return PlayerIds.Select(Evaluate).OrderByDescending(r => r.Score).ToList();
        }

        private PlayerReport BuildReportLocked(PlayerState st, double now)
        {
            var r = new PlayerReport { PlayerId = st.PlayerId, EvaluatedAt = now, InGracePeriod = InGrace(st, now) };
            r.Info = st.Info?.Clone();
            r.Detections.AddRange(PropertyDetections(st, r));

            var c = Config;
            foreach (var kv in st.Violations)
            {
                var q = kv.Value;
                while (q.Count > 0 && q.Peek() < now - c.ViolationWindow) q.Dequeue();
                if (q.Count < c.MinViolationSamples) continue;
                var (cat, label, baseConf) = Describe(kv.Key);
                float conf = Math.Min(c.MaxHeuristicConfidence, baseConf + c.ConfidencePerExtraViolation * (q.Count - c.MinViolationSamples));
                st.LastViolationReason.TryGetValue(kv.Key, out var last);
                r.Detections.Add(new Detection
                {
                    Category = cat, Code = kv.Key, Label = label, Confidence = conf,
                    Reason = $"{q.Count} brudd siste {c.ViolationWindow:0} s. Siste: {last}"
                });
            }

            if (st.UnknownRpcs.Count > 0)
                r.Detections.Add(new Detection
                {
                    Category = DetectionCategory.Network, Code = "UNKNOWN_RPC", Label = "Ukjent RPC",
                    Confidence = c.UnknownRpcConfidence,
                    Reason = "Sendte RPC-er som ikke står i allowedRpcs: " + string.Join(", ", st.UnknownRpcs.Take(5))
                });

            foreach (var d in r.Detections)
                if (d.Category != DetectionCategory.Property && !r.Cheats.Contains(d.Label)) r.Cheats.Add(d.Label);

            double prod = 1.0;
            foreach (var d in r.Detections) prod *= 1.0 - Math.Max(0f, Math.Min(1f, d.Confidence));
            r.Score = (float)(1.0 - prod);
            r.Verdict = r.Score >= c.CheatingThreshold ? Verdict.CHEATING : r.Score >= c.SusThreshold ? Verdict.SUS : Verdict.CLEAN;
            r.Detections.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
            return r;
        }

        private (DetectionCategory, string, float) Describe(string code)
        {
            var c = Config;
            switch (code)
            {
                case "SPEED": return (DetectionCategory.Movement, "Speed hack", c.SpeedConfidence);
                case "TELEPORT": return (DetectionCategory.Movement, "Teleport/warp", c.TeleportConfidence);
                case "FLY": return (DetectionCategory.Movement, "Flyging", c.FlyConfidence);
                case "ARM_REACH": return (DetectionCategory.Movement, "Lange armer", c.ArmReachConfidence);
                case "SCALE": return (DetectionCategory.Movement, "Unormal skala", c.ScaleConfidence);
                case "TAG_REACH": return (DetectionCategory.Movement, "Tag-rekkevidde", c.TagRangeConfidence);
                case "OWNERSHIP_STEAL": return (DetectionCategory.Network, "Ownership-tyveri", c.RateConfidence);
                case "INSTANTIATE_RATE": return (DetectionCategory.Network, "Objekt-spam", c.RateConfidence);
                case "DESTROY_RATE": return (DetectionCategory.Network, "Destroy-spam", c.RateConfidence);
                case "PROPERTY_SPAM": return (DetectionCategory.Network, "Property-spam", c.RateConfidence);
                case "EVENT_RATE": return (DetectionCategory.Network, "Event-spam", c.RateConfidence);
                default: return (DetectionCategory.Network, "RPC-spam", c.RateConfidence);
            }
        }

        /// <summary>Nullstill alle brudd for en spiller (f.eks. etter manuell gjennomgang).</summary>
        public void ClearViolations(string playerId)
        {
            lock (_lock)
                if (_players.TryGetValue(playerId, out var st))
                { st.Violations.Clear(); st.UnknownRpcs.Clear(); st.LastVerdict = Verdict.CLEAN; }
        }

        // =====================================================================
        //  Hjelpere
        // =====================================================================

        private PlayerState GetOrCreate(string id, double t)
        {
            if (!_players.TryGetValue(id, out var st))
                _players[id] = st = new PlayerState { PlayerId = id, JoinTime = t };
            return st;
        }

        /// <summary>Snittfart for kroppen fra snapshotet ca. <paramref name="window"/> sekunder tilbake til nå (uten hull større enn maxGap).</summary>
        private static float WindowedSpeed(PlayerState st, PlayerSnapshot now, double window, double maxGap)
        {
            PlayerSnapshot from = null;
            var node = st.Snapshots.Last;
            PlayerSnapshot newer = now;
            while (node != null)
            {
                var s = node.Value;
                if (!ReferenceEquals(s, now))
                {
                    if (newer.Timestamp - s.Timestamp > maxGap) break;
                    from = s;
                    newer = s;
                    if (now.Timestamp - s.Timestamp >= window) break;
                }
                node = node.Previous;
            }
            if (from == null) return 0f;
            double dt = now.Timestamp - from.Timestamp;
            return dt <= 0 ? 0f : (float)(Vec3.Distance(now.BodyPosition, from.BodyPosition) / dt);
        }

        private bool InGrace(PlayerState st, double t) => t - st.JoinTime < Config.JoinGracePeriod;

        private void Touch(double t) { if (t > CurrentTime) CurrentTime = t; }

        private void AddViolation(PlayerState st, string code, double t, string reason)
        {
            if (!st.Violations.TryGetValue(code, out var q)) st.Violations[code] = q = new Queue<double>();
            q.Enqueue(t);
            while (q.Count > 0 && q.Peek() < t - Config.ViolationWindow) q.Dequeue();
            st.LastViolationReason[code] = reason;
        }

        private static double LastOf(Queue<double> q) { double last = 0; foreach (var v in q) last = v; return last; }

        private static string Truncate(string s, int n) => s == null ? "" : s.Length <= n ? s : s.Substring(0, n) + "…";
    }
}
