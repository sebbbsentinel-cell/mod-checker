using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FanGame.ModChecker;
using UnityEngine;

namespace GTagModChecker
{
    /// <summary>
    /// DISC-stil: gjennomsiktig glødende ring (fiolett eller rosa).
    ///  - Innstillings-disc foran hodet: rutenett med to kolonner av bokser (liten caption over, verdi under), PAGE og CLOSE.
    ///  - PLAYERS: spillerne som bokser rundt ringen, med statusfarge.
    ///  - Spiller-disc: ringen svever rundt valgt spillers kropp og følger riggen, med bokser rundt ringen
    ///    (STATUS, BACK, SCORE, FPS, PLATFORM, MODS, CHEATS, CREATED, REPORT, MUTE).
    /// Trykk med fingertuppen ELLER pek med laseren og trykk avtrekkeren. Ingen collidere.
    /// Ingenting påvirker andre spillere: REPORT er kun lokal logg, MUTE er kun lokal avspilling, ingen venneforespørsler.
    /// </summary>
    internal sealed class DiscMenu : MonoBehaviour
    {
        public enum View { Home, Players, Player }

        private const float HomeR = 0.26f, HomeT = 0.035f, PlayerR = 0.45f, PlayerT = 0.06f;
        private const int GridSlots = 10, RingSlots = 10;

        private sealed class Item
        {
            public string Caption;
            public Func<string> Value;
            public Action Press;
        }

        private UiKit _kit;
        private Transform _homeRoot, _gridGroup, _playersGroup, _playerRoot, _listBox;
        private UiKit.Btn[] _slots, _ringSlots;
        private UiKit.Btn _page, _close, _plPage, _plBack;
        private UiKit.Btn _bStatus, _bBack, _bScore, _bFps, _bPlatform, _bMods, _bCheats, _bCreated, _bReport, _bMute;
        private PText _title, _toastText, _plTitle, _pName, _listText;
        private LineRenderer _laser;
        private Transform _dot;
        private MeshRenderer _dotGlow, _dotCore;
        private Material _dotGlowMat, _dotGlowHotMat, _dotCoreMat, _dotCoreHotMat;
        private Material _laserMat, _laserHotMat;

        private List<Item> _items = new List<Item>();
        private int _homePage, _playersPage;
        private List<PlayerReport> _reports = new List<PlayerReport>();
        private List<RigInfo> _rigs = new List<RigInfo>();
        private bool _inRoom;
        private string _movement = "-", _network = "-";
        private int _modEntries;
        private string _selected, _expand, _toast;
        private float _toastUntil;

        private Transform _right;
        private float _nextHandLookup;
        private Vector3? _tip;
        private string _hoverPlayer;
        private float _hoverSince;
        private bool _triggerWasDown;
        private UiKit.Btn _laserBtn;
        private Vector3 _laserHit;

        public View CurrentView { get; private set; } = View.Home;
        public bool IsVisible => gameObject.activeSelf;
        public string SelectedPlayerId => CurrentView == View.Player ? _selected : null;
        public string Toast => Time.unscaledTime < _toastUntil ? _toast : null;

        public Func<string, PlayerReport> OnReport;
        public Func<float?> ReadTrigger;
        public VoiceControl Voice;
        /// <summary>Brukeren byttet MENU STYLE (til PANEL).</summary>
        public Action OnStyleChanged;

        private PluginSettings S => Plugin.Settings;

        public static DiscMenu Create()
        {
            var go = new GameObject("GTagModCheckerDisc") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(go);
            var d = go.AddComponent<DiscMenu>();
            d.Build();
            go.SetActive(false);
            return d;
        }

        private void OnEnable() { Application.onBeforeRender += FollowPlayer; }
        private void OnDisable()
        {
            Application.onBeforeRender -= FollowPlayer;
            if (_laser != null) _laser.enabled = false;
        }

        // =====================================================================
        //  Bygging
        // =====================================================================

        private Color AccentColor => S.Theme.Value == DiscTheme.Pink ? UiKit.Pink : S.Theme.Value == DiscTheme.Violet ? UiKit.Violet : UiKit.Silver;

        private void Build()
        {
            _kit = new UiKit { Accent = AccentColor };
            BuildItems();
            BuildHome();
            BuildPlayerDisc();
            BuildLaserAndDot();
            ShowView(View.Home);
        }

        /// <summary>Bygger alt på nytt (f.eks. når THEME byttes).</summary>
        private void Rebuild()
        {
            var view = CurrentView;
            var pos = _homeRoot.position; var rot = _homeRoot.rotation;
            foreach (Transform ch in transform.Cast<Transform>().ToList()) Destroy(ch.gameObject);
            _kit.Destroy();
            Build();
            _homeRoot.position = pos; _homeRoot.rotation = rot;
            ShowView(view);
        }

        private static string OnOff(bool b) => b ? "ON" : "OFF";

        private void BuildItems()
        {
            _items = new List<Item>
            {
                new Item { Caption = "MENU STYLE", Value = () => "DISC", Press = () => { S.Style.Value = MenuStyle.Panel; Plugin.Log.LogInfo("[GTag Mod Checker] MENU STYLE -> PANEL"); OnStyleChanged?.Invoke(); } },
                new Item { Caption = "THEME", Value = () => S.Theme.Value.ToString().ToUpperInvariant(), Press = () => { S.Theme.Value = S.Theme.Value == DiscTheme.Silver ? DiscTheme.Violet : S.Theme.Value == DiscTheme.Violet ? DiscTheme.Pink : DiscTheme.Silver; Rebuild(); } },
                new Item { Caption = "PLAYERS", Value = () => _inRoom ? _reports.Count.ToString() : "-", Press = () => ShowView(View.Players) },
                new Item { Caption = "NAMETAGS", Value = () => OnOff(S.Nametags.Value), Press = () => S.Nametags.Value = !S.Nametags.Value },
                new Item { Caption = "TAG FPS", Value = () => OnOff(S.TagFpsHz.Value), Press = () => S.TagFpsHz.Value = !S.TagFpsHz.Value },
                new Item { Caption = "TAG PLATFORM", Value = () => OnOff(S.TagPlatforms.Value), Press = () => S.TagPlatforms.Value = !S.TagPlatforms.Value },
                new Item { Caption = "TAG VERDICT", Value = () => OnOff(S.TagVerdict.Value), Press = () => S.TagVerdict.Value = !S.TagVerdict.Value },
                new Item { Caption = "CLIENT CHECKER", Value = () => OnOff(S.AutoCheck.Value), Press = () => S.AutoCheck.Value = !S.AutoCheck.Value },
                new Item { Caption = "SELF TAGS", Value = () => OnOff(S.SelfTags.Value), Press = () => S.SelfTags.Value = !S.SelfTags.Value },
                new Item { Caption = "LASER", Value = () => OnOff(S.LaserEnabled.Value), Press = () => S.LaserEnabled.Value = !S.LaserEnabled.Value },
                // side 2
                new Item { Caption = "TAG FONT", Value = () => S.TagFont.Value == NametagFont.Bold ? "BOLD" : "DEFAULT", Press = () => S.TagFont.Value = S.TagFont.Value == NametagFont.Bold ? NametagFont.Default : NametagFont.Bold },
                new Item { Caption = "FINGER DOT", Value = () => OnOff(S.ShowFingerDot.Value), Press = () => S.ShowFingerDot.Value = !S.ShowFingerDot.Value },
                new Item { Caption = "DEBUG TOUCH", Value = () => OnOff(S.DebugTouch.Value), Press = () => S.DebugTouch.Value = !S.DebugTouch.Value },
                new Item { Caption = "OPEN WITH X", Value = () => S.TriggerMode.Value == TriggerMode.DoublePress ? "DOUBLE" : "SINGLE", Press = () => ShowToast("Change in config file") },
                new Item { Caption = "FLAGGED", Value = () => _reports.Count(r => r.Verdict != Verdict.CLEAN).ToString(), Press = () => ShowView(View.Players) },
                new Item { Caption = "MOD LIST", Value = () => _modEntries + " ENTRIES", Press = null },
                new Item { Caption = "MOVEMENT", Value = () => _movement.ToUpperInvariant(), Press = null },
                new Item { Caption = "NETWORK", Value = () => _network.ToUpperInvariant(), Press = null },
            };
        }

        private void BuildHome()
        {
            _homeRoot = _kit.Child(transform, "HomeDisc", Vector3.zero);
            _kit.Ring(_homeRoot, HomeR, HomeT, _kit.Accent);
            _title = _kit.Text(_homeRoot, new Vector3(0, 0.188f, -0.002f), 0.0105f, TextAnchor.MiddleCenter, TextAlignment.Center, Color.Lerp(_kit.Accent, Color.white, 0.4f), "<b>GTAG MOD CHECKER</b>");
            _toastText = _kit.Text(_homeRoot, new Vector3(0, -0.198f, -0.002f), 0.0085f, TextAnchor.MiddleCenter, TextAlignment.Center, UiKit.White, "");

            // Innstillinger: to kolonner
            _gridGroup = _kit.Child(_homeRoot, "Grid", Vector3.zero);
            _slots = new UiKit.Btn[GridSlots];
            for (int i = 0; i < GridSlots; i++)
            {
                int slot = i;
                float x = (i % 2 == 0) ? -0.080f : 0.080f;
                float y = 0.125f - (i / 2) * 0.055f;
                _slots[i] = _kit.Box(_gridGroup, "d_slot" + i, new Vector3(x, y, -0.006f), 0.150f, 0.046f, "", "", () => PressSlot(slot));
            }
            _page = _kit.Box(_gridGroup, "d_page", new Vector3(-0.055f, -0.155f, -0.006f), 0.095f, 0.040f, "", "PAGE 1/2", () => { _homePage = (_homePage + 1) % HomePages; RenderHome(); }, 0.012f);
            _close = _kit.Box(_gridGroup, "d_close", new Vector3(0.055f, -0.155f, -0.006f), 0.095f, 0.040f, "", "CLOSE", () => { Plugin.Log.LogInfo("[GTag Mod Checker] Disc: CLOSE"); Close(); }, 0.012f);

            // Spillerliste rundt ringen
            _playersGroup = _kit.Child(_homeRoot, "Players", Vector3.zero);
            _plTitle = _kit.Text(_playersGroup, new Vector3(0, 0.080f, -0.004f), 0.012f, TextAnchor.MiddleCenter, TextAlignment.Center, UiKit.White, "PLAYERS");
            _ringSlots = new UiKit.Btn[RingSlots];
            for (int i = 0; i < RingSlots; i++)
            {
                int slot = i;
                float a = Mathf.PI * 0.5f - i * 2f * Mathf.PI / RingSlots;
                var p = new Vector3(Mathf.Cos(a) * 0.245f, Mathf.Sin(a) * 0.245f, -0.006f);
                _ringSlots[i] = _kit.Box(_playersGroup, "d_pl" + i, p, 0.120f, 0.042f, "", "", () => PressRingSlot(slot), 0.0115f);
            }
            _plPage = _kit.Box(_playersGroup, "d_plpage", new Vector3(0, -0.035f, -0.006f), 0.100f, 0.038f, "", "PAGE 1/1", () => { _playersPage++; RenderPlayers(); }, 0.011f);
            _plBack = _kit.Box(_playersGroup, "d_plback", new Vector3(0, -0.085f, -0.006f), 0.100f, 0.038f, "", "BACK", () => ShowView(View.Home), 0.011f);
        }

        private void BuildPlayerDisc()
        {
            _playerRoot = _kit.Child(transform, "PlayerDisc", Vector3.zero);
            _kit.Ring(_playerRoot, PlayerR, PlayerT, _kit.Accent);
            const float w = 0.22f, h = 0.09f, vs = 0.032f, z = -0.012f;
            _bStatus = _kit.Box(_playerRoot, "d_status", new Vector3(-0.30f, 0.44f, z), w, h, "STATUS", "", null, vs);
            _bBack = _kit.Box(_playerRoot, "d_back", new Vector3(0f, 0.53f, z), w, h, "", "BACK", () => ShowView(View.Players), vs);
            _bScore = _kit.Box(_playerRoot, "d_score", new Vector3(0.30f, 0.44f, z), w, h, "SCORE", "", null, vs);
            _bFps = _kit.Box(_playerRoot, "d_fps", new Vector3(-0.53f, 0.18f, z), w, h, "FPS", "", null, vs);
            _bPlatform = _kit.Box(_playerRoot, "d_platform", new Vector3(0.53f, 0.18f, z), w, h, "PLATFORM", "", null, vs);
            _bMods = _kit.Box(_playerRoot, "d_mods", new Vector3(-0.55f, -0.08f, z), w, h, "MODS", "", () => ToggleExpand("mods"), vs);
            _bCheats = _kit.Box(_playerRoot, "d_cheats", new Vector3(0.55f, -0.08f, z), w, h, "CHEATS", "", () => ToggleExpand("cheats"), vs);
            _bCreated = _kit.Box(_playerRoot, "d_created", new Vector3(-0.32f, -0.43f, z), w, h, "CREATED", PlayerText.NotFound, null, vs * 0.85f);
            _bReport = _kit.Box(_playerRoot, "d_report", new Vector3(0f, -0.53f, z), w, h, "REPORT", "SELECT", DoReport, vs);
            _bMute = _kit.Box(_playerRoot, "d_mute", new Vector3(0.32f, -0.43f, z), w, h, "MUTE", "OFF", ToggleMute, vs);
            _pName = _kit.Text(_playerRoot, new Vector3(0, -0.665f, z), 0.042f, TextAnchor.MiddleCenter, TextAlignment.Center, UiKit.White, "");

            _listBox = _kit.Child(_playerRoot, "ListBox", new Vector3(0, -0.92f, z));
            _kit.MeshObj(_listBox, "Border", new Vector3(0, 0, 0.001f), _kit.RoundedRect(0.666f, 0.336f, 0.03f), _kit.Mat(_kit.Accent));
            _kit.MeshObj(_listBox, "Fill", Vector3.zero, _kit.RoundedRect(0.66f, 0.33f, 0.028f), _kit.Mat(UiKit.BoxFill));
            _listText = _kit.Text(_listBox, new Vector3(-0.30f, 0.145f, -0.002f), 0.032f, TextAnchor.UpperLeft, TextAlignment.Left, UiKit.White, "");
            _listBox.gameObject.SetActive(false);
            _playerRoot.gameObject.SetActive(false);
        }

        private void BuildLaserAndDot()
        {
            var go = new GameObject("DiscLaser") { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(transform, false);
            _laser = go.AddComponent<LineRenderer>();
            _laser.useWorldSpace = true;
            _laser.positionCount = 2;
            _laser.numCapVertices = 2;
            _laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _laser.receiveShadows = false;
            _laserMat = _kit.Mat(_kit.Accent);
            _laserHotMat = _kit.Mat(Color.Lerp(_kit.Accent, Color.white, 0.45f));
            _laser.sharedMaterial = _laserMat;
            _laser.startColor = _kit.Accent; _laser.endColor = _kit.Accent;
            _laser.enabled = false;

            _dot = _kit.Child(transform, "FingerDot", Vector3.zero);
            var a = _kit.Accent;
            _dotGlowMat = _kit.AlphaMat(new Color(a.r, a.g, a.b, 0.55f));
            var ah = Color.Lerp(a, Color.white, 0.4f);
            _dotGlowHotMat = _kit.AlphaMat(new Color(ah.r, ah.g, ah.b, 0.75f));
            _dotCoreMat = _kit.Mat(Color.Lerp(a, Color.white, 0.6f));
            _dotCoreHotMat = _kit.Mat(Color.white);
            _dotGlow = _kit.Sphere(_dot, "Glow", 0.010f, _dotGlowMat);
            _dotCore = _kit.Sphere(_dot, "Core", 0.0055f, _dotCoreMat);
            _dot.gameObject.SetActive(false);
        }

        // =====================================================================
        //  Åpne/lukke og data
        // =====================================================================

        public void Open()
        {
            gameObject.SetActive(true);
            _nextHandLookup = 0f;
            _kit.BlockFor(0.3f);
            _triggerWasDown = true;
            _hoverPlayer = null;
            ShowView(View.Home);
            AnchorHome();
        }

        public void Close()
        {
            _kit?.ClearHover();
            if (_laser != null) _laser.enabled = false;
            if (_dot != null) _dot.gameObject.SetActive(false);
            gameObject.SetActive(false);
        }

        /// <summary>Plasserer innstillings-discen foran hodet og snur den mot kameraet.</summary>
        private void AnchorHome()
        {
            Camera cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            Vector3 fwd = cam != null ? cam.transform.forward : Vector3.forward;
            fwd.y = 0; if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward; fwd.Normalize();
            float s = Mathf.Max(0.2f, S.DiscScale.Value);
            _homeRoot.localScale = new Vector3(s, s, s);
            _homeRoot.position = camPos + fwd * Mathf.Max(0.2f, S.DiscDistance.Value) + Vector3.up * -0.05f;
            Vector3 look = _homeRoot.position - camPos;
            _homeRoot.rotation = look.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(look, Vector3.up) : Quaternion.LookRotation(fwd, Vector3.up);
        }

        public void SetData(List<PlayerReport> reports, bool inRoom, string movement, string network, int modEntries)
        {
            _reports = reports ?? new List<PlayerReport>();
            _inRoom = inRoom;
            _movement = movement ?? "-"; _network = network ?? "-";
            _modEntries = modEntries;
            Render();
        }

        public void SetRigs(List<RigInfo> rigs) { _rigs = rigs ?? new List<RigInfo>(); }

        public void ShowView(View v)
        {
            if (v == View.Home && CurrentView == View.Player) AnchorHome();
            if (v == View.Players && CurrentView == View.Player) AnchorHome();
            CurrentView = v;
            _homeRoot.gameObject.SetActive(v != View.Player);
            _gridGroup.gameObject.SetActive(v == View.Home);
            _playersGroup.gameObject.SetActive(v == View.Players);
            _title.gameObject.SetActive(v == View.Home);
            _playerRoot.gameObject.SetActive(v == View.Player);
            if (v != View.Player) _expand = null;
            Render();
        }

        private int HomePages => Math.Max(1, (_items.Count + GridSlots - 1) / GridSlots);

        private void Render()
        {
            if (_kit == null) return;
            if (CurrentView == View.Home) RenderHome();
            else if (CurrentView == View.Players) RenderPlayers();
            else RenderPlayer();
            _toastText.text = Toast ?? "";
        }

        private void RenderHome()
        {
            _homePage = Math.Max(0, Math.Min(_homePage, HomePages - 1));
            for (int i = 0; i < GridSlots; i++)
            {
                int idx = _homePage * GridSlots + i;
                var b = _slots[i];
                if (idx < _items.Count)
                {
                    b.Go.SetActive(true);
                    b.Caption.text = _items[idx].Caption;
                    string v = _items[idx].Value();
                    b.Label.text = v == "ON" ? "<color=#FFFFFF>ON</color>" : v == "OFF" ? "<color=#8E8AA8>OFF</color>" : v;
                }
                else b.Go.SetActive(false);
            }
            _page.Label.text = $"PAGE {_homePage + 1}/{HomePages}";
        }

        private int PlayerPages => Math.Max(1, (_reports.Count + RingSlots - 1) / RingSlots);

        private void RenderPlayers()
        {
            _playersPage = PlayerPages > 0 ? _playersPage % PlayerPages : 0;
            int flagged = _reports.Count(r => r.Verdict != Verdict.CLEAN);
            _plTitle.text = _inRoom
                ? $"<b>PLAYERS</b>\n<size=40>{_reports.Count} IN ROOM · <color={(flagged > 0 ? UiKit.RedHex : UiKit.GreenHex)}>{flagged} FLAGGED</color></size>"
                : "<b>PLAYERS</b>\n<size=40><color=" + UiKit.MutedHex + ">NOT IN A ROOM</color></size>";
            for (int i = 0; i < RingSlots; i++)
            {
                int idx = _playersPage * RingSlots + i;
                var b = _ringSlots[i];
                if (idx < _reports.Count)
                {
                    var r = _reports[idx];
                    b.Go.SetActive(true);
                    b.Caption.text = $"<color={UiKit.VerdictHex(r.Verdict)}>{r.StatusText}</color>";
                    b.Label.text = PlayerText.Name(r, 12);
                }
                else b.Go.SetActive(false);
            }
            _plPage.Go.SetActive(PlayerPages > 1);
            _plPage.Label.text = $"PAGE {_playersPage + 1}/{PlayerPages}";
        }

        private RigInfo RigFor(string id) => id == null ? null : _rigs.FirstOrDefault(r => r != null && r.PlayerId == id && !r.IsLocal);

        private void RenderPlayer()
        {
            var r = _reports.FirstOrDefault(x => x.PlayerId == _selected);
            var rig = RigFor(_selected);
            if (r == null)
            {
                _bStatus.Label.text = "<color=" + UiKit.MutedHex + ">LEFT</color>";
                _bScore.Label.text = _bFps.Label.text = _bPlatform.Label.text = _bMods.Label.text = _bCheats.Label.text = "-";
                _pName.text = "PLAYER LEFT";
                _listBox.gameObject.SetActive(false);
                return;
            }
            _bStatus.Label.text = $"<color={UiKit.VerdictHex(r.Verdict)}>{r.StatusText}</color>";
            _bScore.Label.text = r.Score.ToString("0.00") + (r.InGracePeriod ? "*" : "");
            int? fps = PlayerText.Fps(r, rig);
            _bFps.Label.text = fps?.ToString() ?? "?";
            string plat = PlayerText.Platform(r, rig);
            _bPlatform.Label.text = plat ?? "<size=48>" + PlayerText.NotFound + "</size>";
            _bMods.Label.text = r.AllowedModCount.ToString();
            _bCheats.Label.text = r.CheatCount > 0 ? $"<color={UiKit.RedHex}>{r.CheatCount}</color>" : (r.UnsureCount > 0 ? $"<color={UiKit.YellowHex}>0 (+{r.UnsureCount}?)</color>" : "0");
            bool hasVoice = rig?.Voice != null;
            _bMute.Label.text = !hasVoice ? "N/A" : (Voice != null && Voice.Muted(rig) ? "<color=" + UiKit.RedHex + ">ON</color>" : "OFF");
            var raw = PlayerText.RawColor(rig, r);
            string hex = raw != null ? "#" + ColorUtility.ToHtmlStringRGB(raw.Value) : "#FFFFFF";
            _pName.text = $"<color={hex}><b>{PlayerText.Name(r, 16).ToUpperInvariant()}</b></color>";

            _listBox.gameObject.SetActive(_expand != null);
            if (_expand != null)
            {
                var sb = new StringBuilder();
                if (_expand == "mods")
                {
                    sb.Append("<color=").Append(UiKit.CaptionGreyHex).Append(">ALLOWED MODS</color>\n");
                    if (r.AllowedModCount == 0) sb.Append("(none)");
                    foreach (var m in r.AllowedMods.Take(7)) sb.Append(UiKit.Trunc(m, 30).ToUpperInvariant()).Append('\n');
                }
                else
                {
                    sb.Append("<color=").Append(UiKit.CaptionGreyHex).Append(">CHEATS</color>\n");
                    if (r.CheatCount == 0 && r.UnsureCount == 0) sb.Append("(none)");
                    foreach (var c in r.Cheats.Take(6)) sb.Append("<color=").Append(UiKit.RedHex).Append('>').Append(UiKit.Trunc(c, 30).ToUpperInvariant()).Append("</color>\n");
                    foreach (var u in r.Unsure.Take(2)) sb.Append("<color=").Append(UiKit.YellowHex).Append(">? ").Append(UiKit.Trunc(u, 28).ToUpperInvariant()).Append("</color>\n");
                }
                _listText.text = sb.ToString().TrimEnd('\n');
            }
        }

        // =====================================================================
        //  Handlinger (alle lokale)
        // =====================================================================

        private void PressSlot(int slot)
        {
            int idx = _homePage * GridSlots + slot;
            if (idx >= _items.Count) return;
            var it = _items[idx];
            if (it.Press == null) { ShowToast(it.Caption + ": info only"); return; }
            it.Press();
            if (this != null && gameObject.activeSelf && CurrentView == View.Home) RenderHome();
        }

        private void PressRingSlot(int slot)
        {
            int idx = _playersPage * RingSlots + slot;
            if (idx < _reports.Count) SelectPlayer(_reports[idx].PlayerId, "players ring");
        }

        public void SelectPlayer(string id, string source)
        {
            if (id == null) return;
            _selected = id;
            _expand = null;
            var r = _reports.FirstOrDefault(x => x.PlayerId == id);
            Plugin.Log.LogInfo($"[GTag Mod Checker] Disc: valgte {(r != null ? PlayerText.Name(r) : id)} ({id}) via {source}");
            ShowView(View.Player);
            FollowPlayer();
        }

        private void ToggleExpand(string which) { _expand = _expand == which ? null : which; RenderPlayer(); }

        private void DoReport()
        {
            if (_selected == null) return;
            try { OnReport?.Invoke(_selected); } // KUN lokal logg
            catch (Exception ex) { Plugin.Log.LogError("[GTag Mod Checker] Lokal rapport feilet: " + ex.Message); }
            ShowToast("Reported locally");
            _bReport.Label.text = "DONE";
        }

        private void ToggleMute()
        {
            var rig = RigFor(_selected);
            if (Voice == null || !Voice.ToggleMute(rig)) { ShowToast("Voice not found"); return; }
            RenderPlayer();
        }

        private void ShowToast(string t)
        {
            _toast = t; _toastUntil = Time.unscaledTime + 2.5f;
            if (_toastText != null) _toastText.text = t;
            if (_pName != null && CurrentView == View.Player) Plugin.Log.LogInfo("[GTag Mod Checker] Disc: " + t);
        }

        // =====================================================================
        //  Oppdatering: følge spiller, berøring, laser
        // =====================================================================

        /// <summary>Spiller-discen sentreres på kroppen til spilleren og snur seg mot kameraet, hver ramme.</summary>
        private void FollowPlayer()
        {
            if (this == null || !gameObject.activeSelf || CurrentView != View.Player || _playerRoot == null) return;
            var rig = RigFor(_selected);
            if (rig == null || !rig.Alive) return; // blir stående der den var
            float sc = rig.Scale;
            Vector3 center = rig.HeadPos - Vector3.up * (0.25f * sc);
            float s = Mathf.Max(0.2f, S.PlayerDiscScale.Value) * sc;
            _playerRoot.localScale = new Vector3(s, s, s);
            _playerRoot.position = center;
            Camera cam = Camera.main;
            Vector3 look = center - (cam != null ? cam.transform.position : Vector3.zero);
            if (look.sqrMagnitude > 1e-4f) _playerRoot.rotation = Quaternion.LookRotation(look, Vector3.up);
        }

        private void Update()
        {
            try
            {
                if (Time.unscaledTime >= _nextHandLookup || _right == null) { _nextHandLookup = Time.unscaledTime + 1f; _right = GtRefs.RightHand; }
                FollowPlayer();

                _tip = GtRefs.RightFingerTip(_right, S.FingerTipOffset.Value, out _);
                if (_tip != null) _kit.Touch(_tip.Value, Mathf.Max(0f, S.PressRadius.Value), Mathf.Max(0.002f, S.PressDepth.Value), S.PressCooldown.Value, S.Haptics.Value, S.HapticAmplitude.Value);
                else _kit.ClearHover();
                bool fingerHover = _kit.Hovered != null;

                UpdateLaser(fingerHover);
                if (_toastText != null && _toastText.text.Length > 0 && Time.unscaledTime >= _toastUntil) _toastText.text = "";
            }
            catch (Exception ex) { Plugin.Log.LogError("[GTag Mod Checker] Disc-feil: " + ex.Message); }
        }

        private Vector3 LaserOrigin => _right != null ? _right.position : Vector3.zero;
        private Vector3 LaserDir => _right != null ? (_right.rotation * Quaternion.Euler(S.LaserRotX.Value, S.LaserRotY.Value, S.LaserRotZ.Value) * Vector3.forward).normalized : Vector3.forward;

        private void UpdateLaser(bool fingerHover)
        {
            _laserBtn = null;
            if (!S.LaserEnabled.Value || _right == null) { _hoverPlayer = null; return; }

            _laserBtn = _kit.RayPick(LaserOrigin, LaserDir, 40f, 0.004f, out _laserHit);
            if (_laserBtn != null && !fingerHover) _kit.SetHovered(_laserBtn);

            RigInfo target = null;
            if (_laserBtn == null) target = CheckerMenu.PickTarget(LaserOrigin, LaserDir, _rigs, S.LaserConeAngle.Value, S.LaserMaxDistance.Value, out _);
            string id = target?.PlayerId;
            if (id != _hoverPlayer) { _hoverPlayer = id; _hoverSince = Time.unscaledTime; }

            float? trig = null;
            try { trig = ReadTrigger?.Invoke(); } catch { }
            bool useTrigger = S.LaserSelect.Value == LaserSelectMode.Trigger || (S.LaserSelect.Value == LaserSelectMode.Auto && trig != null);
            bool down = trig != null && trig.Value > 0.6f;
            bool edge = down && !_triggerWasDown;
            _triggerWasDown = down;

            if (_laserBtn != null)
            {
                if (edge) _kit.PressExternal(_laserBtn, "laser", S.Haptics.Value, S.HapticAmplitude.Value, S.PressCooldown.Value);
                return; // peker på en knapp: velg ikke spillere
            }
            bool fire = useTrigger ? (edge && id != null)
                                   : (id != null && id != SelectedPlayerId && Time.unscaledTime - _hoverSince >= S.LaserDwellTime.Value);
            if (fire)
            {
                if (S.Haptics.Value) GtRefs.Vibrate(false, S.HapticAmplitude.Value, 0.05f);
                SelectPlayer(id, useTrigger ? "laser (trigger)" : "laser (point)");
            }
        }

        private void LateUpdate()
        {
            FollowPlayer();
            _tip = GtRefs.RightFingerTip(_right, S.FingerTipOffset.Value, out _);
            // fingerprikk
            bool show = S.ShowFingerDot.Value && _tip != null;
            if (_dot.gameObject.activeSelf != show) _dot.gameObject.SetActive(show);
            if (show)
            {
                bool hot = _kit.Hovered != null && _laserBtn != _kit.Hovered;
                float k = hot ? 1.4f : 1f;
                _dot.position = _tip.Value;
                _dot.localScale = new Vector3(k, k, k);
                if (_dotGlowMat != null) _dotGlow.sharedMaterial = hot ? _dotGlowHotMat : _dotGlowMat;
                if (_dotCoreMat != null) _dotCore.sharedMaterial = hot ? _dotCoreHotMat : _dotCoreMat;
            }
            // laserlinje
            if (!S.LaserEnabled.Value || _right == null) { _laser.enabled = false; return; }
            var target = _hoverPlayer != null ? RigFor(_hoverPlayer) : null;
            Vector3 a = LaserOrigin;
            Vector3 b = _laserBtn != null ? _laserHit : target != null ? target.HeadPos : a + LaserDir * 6f;
            bool hit = _laserBtn != null || target != null;
            float w = Mathf.Max(0.001f, S.LaserWidth.Value) * (hit ? 1.6f : 1f);
            _laser.enabled = true;
            _laser.SetPosition(0, a);
            _laser.SetPosition(1, b);
            _laser.startWidth = w;
            _laser.endWidth = w * (hit ? 1f : 0.4f);
            _laser.sharedMaterial = hit ? _laserHotMat : _laserMat;
        }

        // ---- for testing ----
        internal Vector3? ButtonWorldPos(string id) => _kit.Buttons.FirstOrDefault(x => x.Id == id && x.Go != null)?.Go.transform.position;
        internal string ButtonValue(string id) => _kit.Buttons.FirstOrDefault(x => x.Id == id)?.Label.text;
        internal string ButtonCaption(string id) => _kit.Buttons.FirstOrDefault(x => x.Id == id)?.Caption?.text;
        internal bool ButtonVisible(string id) => _kit.Buttons.FirstOrDefault(x => x.Id == id)?.Go.activeInHierarchy ?? false;
        internal string HoveredButtonId => _kit.Hovered?.Id;
        internal string LaserButtonId => _laserBtn?.Id;
        internal Vector3 PlayerDiscPos => _playerRoot.position;
        internal Quaternion PlayerDiscRot => _playerRoot.rotation;
        internal Vector3 HomeDiscPos => _homeRoot.position;
        internal bool ListBoxVisible => _listBox.gameObject.activeInHierarchy;
        internal string ListText => _listText.text;
        internal string PlayerName => _pName.text;
        internal Color Accent => _kit.Accent;
        internal Vector3? DotPosition => _dot != null && _dot.gameObject.activeSelf ? _dot.position : (Vector3?)null;
        internal LineRenderer Laser => _laser;

        private void OnDestroy()
        {
            Application.onBeforeRender -= FollowPlayer;
            _kit?.Destroy();
        }
    }
}
