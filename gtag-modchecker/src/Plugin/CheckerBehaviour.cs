using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using FanGame.ModChecker;
using UnityEngine;
using MC = FanGame.ModChecker.ModChecker;

namespace GTagModChecker
{
    /// <summary>Hovedløkken: leser data, håndterer X-knappen, kjører sjekker og viser panelet.</summary>
    internal sealed class CheckerBehaviour : MonoBehaviour
    {
        public const string ModListFileName = "GTagModChecker_mods.json";

        private PluginSettings S => Plugin.Settings;
        private MC _checker;
        private PhotonFeed _feed;
        private RigReader _rigs;
        private InputReader _input;
        private VRPanel _panel;
        private CheckerMenu _menu;
        private DiscMenu _disc;
        private Nametags _nametags;
        private readonly VoiceControl _voice = new VoiceControl();
        private List<PlayerReport> _last = new List<PlayerReport>();
        private List<RigInfo> _rigList = new List<RigInfo>();

        private float _nextSync, _nextSample, _nextAuto, _nextPanelRefresh, _nextRigList, _nextTagEval;
        private float _lastPressTime = -10f;
        private int _errors;

        private void Awake()
        {
            var cfg = new ModCheckerConfig
            {
                JoinGracePeriod = 5,
                MinViolationSamples = 3,
                ViolationWindow = 10,
                MaxSnapshotGap = 0.5,
                MaxSpeed = S.MaxSpeed.Value,
                SpeedWindow = 0.5,
                SpeedSustainSamples = S.SpeedSustainSamples.Value,
                SpeedJumpIgnore = 6f,
                SpeedSuppressAfterJump = 1.5,
                TeleportDistance = S.TeleportDistance.Value,
                MaxArmReach = S.MaxArmReach.Value,
                MaxAirtime = float.MaxValue, // grounded er ukjent for andre spillere
                MinScale = 0.25f,
                MaxScale = 2.2f,
                // Konservative grenser: vi vet ikke nøyaktig hvor mange hendelser Gorilla Tag sender normalt.
                MaxRpcPerWindow = 60,
                MaxCustomEventsPerWindow = 100,
                MaxInstantiatePerWindow = 10,
                MaxDestroyPerWindow = 10,
                MaxOwnershipTakesPerWindow = 10,
                MaxPropertyChangesPerWindow = 15,
                FlagUnknownRpcs = false
            };

            ModList embedded;
            try { embedded = ModList.LoadEmbeddedDefault(); }
            catch (Exception ex) { Plugin.Log.LogError("[GTag Mod Checker] Innebygd modliste er ugyldig: " + ex.Message); embedded = new ModList(); }

            _checker = new MC(cfg, embedded);
            _checker.OnModListReloaded += (ok, msg) => { if (ok) Plugin.Log.LogInfo("[GTag Mod Checker] " + msg); else Plugin.Log.LogError("[GTag Mod Checker] " + msg); };
            _checker.OnPlayerFlagged += (s, e) =>
                Plugin.Log.LogWarning($"[GTag Mod Checker] FLAGGET {e.Report.Info?.DisplayName ?? e.Report.PlayerId}: {e.Report.StatusText} " +
                                      string.Join("; ", e.Report.Detections.Select(d => d.Code + " " + d.Label)));
            _checker.OnReportRequested += r =>
                Plugin.Log.LogWarning($"[GTag Mod Checker] REPORT (kun lokalt, sendes ikke til spillet): {r.Info?.DisplayName ?? r.PlayerId} {r.StatusText} score={r.Score:0.00} " +
                                      string.Join("; ", r.Detections.Select(d => $"{d.Code} {d.Label} ({d.Confidence:0.00}): {d.Reason}")));
            _checker.OnUnknownProperty += u =>
            {
                if (S.LogUnknownProperties.Value)
                    Plugin.Log.LogInfo($"[GTag Mod Checker] Ukjent property-nøkkel: '{u.Key}' = '{u.SampleValue}'");
            };

            LoadModListFile();
            _feed = new PhotonFeed(_checker);
            _rigs = new RigReader();
            _input = new InputReader();
        }

        private void LoadModListFile()
        {
            try
            {
                string path = Path.Combine(Paths.ConfigPath, ModListFileName);
                if (!File.Exists(path))
                {
                    string json = ModList.ReadEmbeddedDefaultJson();
                    if (json != null) { File.WriteAllText(path, json); Plugin.Log.LogInfo("[GTag Mod Checker] Laget " + path); }
                }
                if (File.Exists(path)) _checker.WatchModListFile(path); // laster nå + live reload
            }
            catch (Exception ex) { Plugin.Log.LogError("[GTag Mod Checker] Modliste-fil: " + ex.Message + " (bruker innebygd liste)"); }
        }

        private void Start()
        {
            _input.TryAcquire();
            Plugin.Log.LogInfo($"[GTag Mod Checker] started, trigger={S.TriggerMode.Value}, XR device valid={_input.LeftValid} ({_input.LeftName}), " +
                               $"key={S.KeyboardKey.Value}, autoCheck={S.AutoCheck.Value}, entries={_checker.CurrentModList.Entries.Count}");
        }

        private void Update()
        {
            try
            {
                float now = Time.time;
                _feed.NetworkChecksEnabled = S.NetworkChecks.Value;

                string src = _input.Poll(S.UseXRController.Value, S.KeyboardKey.Value);
                if (src != null) HandlePress(Time.unscaledTime, src);

                if (now >= _nextSync)
                {
                    _nextSync = now + Mathf.Max(0.2f, S.PlayerSyncInterval.Value);
                    _feed.Sync(now);
                }

                if (S.MovementChecks.Value && now >= _nextSample)
                {
                    _nextSample = now + 0.1f; // 10 Hz
                    _rigs.Sample(_checker, _feed, now);
                }

                if (S.AutoCheck.Value && now >= _nextAuto)
                {
                    _nextAuto = now + Mathf.Max(1f, S.AutoCheckInterval.Value);
                    _last = SafeEvaluate();
                }

                // Rig-liste til laser, navnelapper og lokalt volum (2 ganger i sekundet, kun lesing)
                if (Time.unscaledTime >= _nextRigList)
                {
                    _nextRigList = Time.unscaledTime + 0.5f;
                    _rigList = _rigs.Collect(_feed, includeLocal: true);
                    if (_menu != null) _menu.SetRigs(_rigList);
                    if (_disc != null) _disc.SetRigs(_rigList);
                    UpdateNametags();
                    try { WorldSettings.Guard(); } catch { } // lokal regn/tid av hvis du blir master client
                }
                _voice.ApplyAll(_rigList);

                // Oppdater innholdet hvert sekund mens menyen/panelet er åpent
                if (IsUiVisible && Time.unscaledTime >= _nextPanelRefresh)
                {
                    _nextPanelRefresh = Time.unscaledTime + 1f;
                    _last = SafeEvaluate();
                    if (_menu != null && _menu.IsVisible) _menu.SetData(_last, PhotonFeed.InRoom, MovementStatus(), NetworkStatus(), ModEntries);
                    if (_disc != null && _disc.IsVisible) _disc.SetData(_last, PhotonFeed.InRoom, MovementStatus(), NetworkStatus(), ModEntries);
                    if (_panel != null && _panel.IsVisible) _panel.Show(BuildPanelText(), reposition: false);
                }
            }
            catch (Exception ex)
            {
                if (_errors++ < 20) Plugin.Log.LogError("[GTag Mod Checker] Feil i Update: " + ex);
            }
        }

        private void HandlePress(float now, string source)
        {
            var mode = S.TriggerMode.Value;
            bool fire;
            if (mode == TriggerMode.SinglePress) fire = true;
            else if (now - _lastPressTime <= S.DoublePressWindow.Value) { fire = true; _lastPressTime = -10f; }
            else { fire = false; _lastPressTime = now; }

            bool visible = IsUiVisible;
            Plugin.Log.LogInfo($"[GTag Mod Checker] X pressed (source={source}, trigger={mode}) -> {(fire ? (visible ? "hide" : "check") : "waiting for 2nd press")}");
            if (!fire) return;

            if (visible) { HideUi(); return; }
            OpenPanel();
        }

        private bool IsUiVisible => (_panel != null && _panel.IsVisible) || (_menu != null && _menu.IsVisible) || (_disc != null && _disc.IsVisible);

        private void HideUi()
        {
            if (_panel != null) _panel.Hide();
            if (_menu != null) _menu.Close();
            if (_disc != null) _disc.Close();
        }

        /// <summary>MENU STYLE ble byttet i spillet: lukk den ene stilen og åpne den andre.</summary>
        private void SwitchStyle()
        {
            Plugin.Log.LogInfo("[GTag Mod Checker] Bytter menystil til " + S.Style.Value);
            HideUi();
            OpenPanel();
        }

        /// <summary>SPEED CHECK i CHECKER-fanen: SAFE (22 m/s) -> STRICT (16 m/s) -> OFF (ingen bevegelsessjekker).</summary>
        private string SpeedCheckLabel() => !S.MovementChecks.Value ? "OFF" : S.MaxSpeed.Value < 20f ? "STRICT" : "SAFE";

        private string CycleSpeedCheck()
        {
            string cur = SpeedCheckLabel();
            if (cur == "SAFE") { S.MovementChecks.Value = true; S.MaxSpeed.Value = 16f; }
            else if (cur == "STRICT") { S.MovementChecks.Value = false; }
            else { S.MovementChecks.Value = true; S.MaxSpeed.Value = 22f; }
            try { _checker.Config.MaxSpeed = S.MaxSpeed.Value; } catch { }
            Plugin.Log.LogInfo("[GTag Mod Checker] SPEED CHECK -> " + SpeedCheckLabel() + " (MaxSpeed " + S.MaxSpeed.Value + ")");
            return SpeedCheckLabel();
        }

        private string MovementStatus() =>
            !S.MovementChecks.Value ? "off" : (_rigs.Available ? "on" : "unavailable");

        private string NetworkStatus() => S.NetworkChecks.Value ? "on" : "off";

        private int ModEntries
        {
            get { try { return _checker.CurrentModList.Entries.Count; } catch { return 0; } }
        }

        /// <summary>Navnelapper: lages ved behov, data hvert halve sekund, ny sjekk hvert 2. sekund mens de er på.</summary>
        private void UpdateNametags()
        {
            try
            {
                if (!S.Nametags.Value && _nametags == null) return;
                if (_nametags == null) _nametags = Nametags.Create(UiKit.LoadFont());
                if (S.Nametags.Value && Time.unscaledTime >= _nextTagEval)
                {
                    _nextTagEval = Time.unscaledTime + 2f;
                    _last = SafeEvaluate();
                }
                _nametags.SetData(_rigList, _last);
            }
            catch (Exception ex) { if (_errors++ < 20) Plugin.Log.LogError("[GTag Mod Checker] Navnelapper: " + ex.Message); }
        }

        private void RefreshNow()
        {
            _feed.Sync(Time.time);
            _last = SafeEvaluate();
            _rigList = _rigs.Collect(_feed, includeLocal: true);
            if (_menu != null) { _menu.SetRigs(_rigList); _menu.SetData(_last, PhotonFeed.InRoom, MovementStatus(), NetworkStatus(), ModEntries); }
            if (_disc != null) { _disc.SetRigs(_rigList); _disc.SetData(_last, PhotonFeed.InRoom, MovementStatus(), NetworkStatus(), ModEntries); }
            if (_nametags != null) _nametags.SetData(_rigList, _last);
            Plugin.Log.LogInfo($"[GTag Mod Checker] REFRESH LIST: {_last.Count} spillere, {_rigList.Count} rigger");
        }

        private void OpenPanel()
        {
            _feed.Sync(Time.time); // ferske data
            _last = SafeEvaluate();
            Plugin.Log.LogInfo($"[GTag Mod Checker] Sjekk: {_last.Count} spillere, {_last.Count(r => r.Verdict != Verdict.CLEAN)} flagget, inRoom={PhotonFeed.InRoom}");
            try
            {
                if (S.Style.Value == MenuStyle.Disc)
                {
                    if (_disc == null)
                    {
                        _disc = DiscMenu.Create();
                        _disc.OnReport = id => _checker.RequestReport(id); // kun lokal hendelse + logg
                        _disc.ReadTrigger = _input.RightTrigger;
                        _disc.Voice = _voice;
                        _disc.OnStyleChanged = SwitchStyle;
                    }
                    _rigList = _rigs.Collect(_feed, includeLocal: true);
                    _disc.SetRigs(_rigList);
                    _disc.Open();
                    _disc.SetData(_last, PhotonFeed.InRoom, MovementStatus(), NetworkStatus(), ModEntries);
                }
                else if (S.HandheldMode.Value)
                {
                    if (_menu == null)
                    {
                        _menu = CheckerMenu.Create();
                        _menu.OnReport = id => _checker.RequestReport(id); // kun lokal hendelse + logg
                        _menu.OnRefresh = RefreshNow;
                        _menu.ReadTrigger = _input.RightTrigger;
                        _menu.Voice = _voice;
                        _menu.OnStyleChanged = SwitchStyle;
                        _menu.CycleSpeedCheck = CycleSpeedCheck;
                        _menu.SpeedCheckLabel = SpeedCheckLabel;
                    }
                    _rigList = _rigs.Collect(_feed, includeLocal: true);
                    _menu.SetRigs(_rigList);
                    _menu.Open();
                    _menu.SetData(_last, PhotonFeed.InRoom, MovementStatus(), NetworkStatus(), ModEntries);
                }
                else
                {
                    if (_panel == null) _panel = VRPanel.Create();
                    _panel.distance = S.PanelDistance.Value;
                    _panel.lineHeight = S.PanelLineHeight.Value;
                    _panel.followHead = S.PanelFollowHead.Value;
                    _panel.Show(BuildPanelText(), reposition: true);
                }
                _nextPanelRefresh = Time.unscaledTime + 1f;
            }
            catch (Exception ex) { Plugin.Log.LogError("[GTag Mod Checker] Kunne ikke vise panel: " + ex); }
            if (S.Haptics.Value) _input.Haptic(S.HapticAmplitude.Value, 0.1f);
        }

        private string BuildPanelText()
        {
            string movement = !S.MovementChecks.Value ? "off" : (_rigs.Available ? "on (" + _rigs.Status + ")" : "unavailable");
            return VRPanel.BuildText(_last, PhotonFeed.InRoom, S.TriggerMode.Value, movement);
        }

        private List<PlayerReport> SafeEvaluate()
        {
            try { return _checker.EvaluateAll(); }
            catch (Exception ex) { Plugin.Log.LogError("[GTag Mod Checker] EvaluateAll feilet: " + ex); return new List<PlayerReport>(); }
        }

        private void OnDestroy()
        {
            Plugin.Log.LogWarning("[GTag Mod Checker] CheckerBehaviour ble ødelagt.");
            _feed?.Dispose();
            _checker?.Dispose();
            if (_panel != null) Destroy(_panel.gameObject);
            if (_menu != null) Destroy(_menu.gameObject);
            if (_disc != null) Destroy(_disc.gameObject);
            if (_nametags != null) Destroy(_nametags.gameObject);
        }
    }
}
