using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FanGame.ModChecker;
using UnityEngine;
using LY = GTagModChecker.Layout;

namespace GTagModChecker
{
    /// <summary>
    /// Sigma-menyen i hånden ("GTAG MOD CHECKER - by Seb"), samme oppsett som referansemenyen:
    ///  - Venstre panel følger venstre hånd: 5 ikonknapper øverst, innholdet for fanen, stor tittel nederst.
    ///  - Fanestripe til høyre: POWER (lukk), LOBBY, NAMETAGS, WORLD SETTINGS, SPOTIFY, CAMERA, CHECKER.
    ///  - Spillerpanel til høyre for stripen (portrett, navn, plattform/FPS, farge, volum, REPORT, MODS/CHEATS).
    ///  - CAMERA bytter venstre panel med et bredt kameranettbrett med live forhåndsvisning.
    ///  - SELECT GUN: sølvlaser fra høyre hånd velger en spiller du peker på (avtrekker eller pek-og-vent).
    /// Ingen knapper påvirker andre spillere eller lobbyen. REPORT er kun lokal logg. VOLUME/MUTE er kun lokal avspilling.
    /// CLONE COSMETICS vises bare som deaktivert knapp.
    /// </summary>
    internal sealed partial class CheckerMenu : MonoBehaviour
    {
        public enum Page { Lobby, Nametags, World, Spotify, Camera, Checker }
        public enum PlayerTab { Profile, AllowedMods, Cheats, Info }

        private const float U = LY.U;
        private static readonly string[] TabIcons = { "power", "people", "tag", "sun", "spotify", "camera", "shield" };
        private static readonly string[] PageTitles = { "LOBBY", "NAMETAGS", "WORLD SETTINGS", "SPOTIFY", "CAMERA", "CHECKER" };
        // Tider på døgnet (spillets indekser) for knappene MORNING / NOON / EVENING / MIDNIGHT
        private static readonly int[] TimePresets = { 1, 3, 7, 0 };
        private static readonly string[] TimeNames = { "MORNING", "NOON", "EVENING", "MIDNIGHT" };

        private UiKit _kit;
        private Transform _left, _strip, _player, _camPanel;
        private Transform[] _pages;
        private PText _title;
        private UiKit.Btn[] _tabs;

        // lobby
        private UiKit.Btn[] _playerBtns;
        private MeshRenderer[] _flagMarks;
        private UiKit.Btn _gunBtn;
        private PText _lobbyStatus, _lobbyEmpty;
        private bool _gun = true, _showStatus = true;
        // nametags / checker / world / spotify
        private UiKit.Btn[] _tagRows, _checkerRows, _timeRows, _spotifyRows;
        private UiKit.Btn _rainBtn, _timeMinus, _timePlus;
        private PText _timeLabel, _worldNote, _checkerStatus, _nowPlaying, _spotifyMode;
        private bool _volumeMode;
        // spiller
        private MeshRenderer _portrait;
        private PText _pName, _pLine1, _pLine2, _pJoinedLabel, _pJoined, _pStatus, _volLabel, _volValue, _pList;
        private UiKit.Btn _volDown, _volUp, _infoBtn, _mute, _report, _clone, _modsBtn, _cheatsBtn;
        private UiKit.Btn[] _footerLinks;
        private Transform _profileGroup;
        // kamera
        private CameraMod _cam;
        private MeshRenderer _screen;
        private UiKit.Btn[] _modeBtns;
        private UiKit.Btn _camFlip, _camDesk, _camFov, _camTarget, _camSmooth, _camRecSmall, _scrRecenter, _scrDesk, _scrSmooth, _recBtn, _folderBtn;
        private PText _scrTop, _scrBottom, _recText, _recSub;
        private MeshRenderer _recDot;

        private LineRenderer _laser;
        private Transform _dot;
        private MeshRenderer _dotGlow, _dotCore;
        private Material _dotGlowMat, _dotGlowHotMat, _dotCoreMat, _dotCoreHotMat;
        private Vector3? _tip;
        private string _tipSource;
        private float _nextTouchDebug;
        private Material _laserMat, _laserHoverMat;

        // data
        private List<PlayerReport> _reports = new List<PlayerReport>();
        private List<RigInfo> _rigs = new List<RigInfo>();
        private bool _inRoom;
        private string _movement = "-", _network = "-";
        private int _modEntries;
        private string _selected;
        private string _toastText;
        private float _toastUntil;

        // laser
        private string _hoverId;
        private float _hoverSince;
        private bool _triggerWasDown;

        private Transform _lh, _rh;
        private float _nextHandLookup;

        public Page CurrentPage { get; private set; } = Page.Lobby;
        public PlayerTab CurrentPlayerTab { get; private set; } = PlayerTab.Profile;
        public bool IsVisible => gameObject.activeSelf;
        public string SelectedPlayerId => _player != null && _player.gameObject.activeSelf ? _selected : null;
        public string HoveredPlayerId => _hoverId;
        public string Toast => Time.unscaledTime < _toastUntil ? _toastText : null;
        public bool LaserVisible => _laser != null && _laser.enabled;

        /// <summary>Lokal rapport (logg). Settes av CheckerBehaviour.</summary>
        public Func<string, PlayerReport> OnReport;
        /// <summary>Ber om ferske data (REFRESH LIST).</summary>
        public Action OnRefresh;
        /// <summary>Brukeren byttet MENU STYLE (til DISC).</summary>
        public Action OnStyleChanged;
        /// <summary>SPEED CHECK: bytter SAFE -> STRICT -> OFF og returnerer ny tekst.</summary>
        public Func<string> CycleSpeedCheck;
        public Func<string> SpeedCheckLabel;
        /// <summary>Leser høyre avtrekker (0-1) eller null.</summary>
        public Func<float?> ReadTrigger;
        public VoiceControl Voice;

        private PluginSettings S => Plugin.Settings;

        public static CheckerMenu Create()
        {
            var go = new GameObject("GTagModCheckerMenu");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            var m = go.AddComponent<CheckerMenu>();
            m.Build();
            go.SetActive(false);
            return m;
        }

        private void OnEnable() { Application.onBeforeRender += UpdatePose; }
        private void OnDisable()
        {
            Application.onBeforeRender -= UpdatePose;
            if (_laser != null) _laser.enabled = false;
            if (_cam != null) _cam.PreviewVisible = false;
        }

        // =====================================================================
        //  Koordinater: layout-enheter (fra preview-bildene) -> meter
        // =====================================================================

        /// <summary>Venstre panel: (x,y) fra øverste venstre hjørne, y nedover.</summary>
        private static Vector3 L(float x, float y, float z = 0f) => new Vector3((x - LY.PW / 2f) * U, (LY.PH / 2f - y) * U, z);
        private static Vector3 Q(float x, float y, float z = 0f) => new Vector3((x - LY.QW / 2f) * U, (LY.QH / 2f - y) * U, z);
        private static Vector3 C(float x, float y, float z = 0f) => new Vector3((x - LY.CW / 2f) * U, (LY.CH / 2f - y) * U, z);

        private UiKit.Btn Btn(Transform p, Func<float, float, float, Vector3> map, string id, float x0, float y0, float x1, float y1, string label, Action a, UiKit.Style st, float font)
            => _kit.Button(p, id, map((x0 + x1) / 2f, (y0 + y1) / 2f, 0f), (x1 - x0) * U, (y1 - y0) * U, label, a, st, font * U, 3f * U);

        private UiKit.Btn IconBtn(Transform p, Func<float, float, float, Vector3> map, string id, float x0, float y0, float x1, float y1, string icon, Action a, UiKit.Style st = UiKit.Style.Silver)
            => _kit.IconButton(p, id, map((x0 + x1) / 2f, (y0 + y1) / 2f, 0f), (x1 - x0) * U, (y1 - y0) * U, icon, a, st, 0.64f);

        private PText Txt(Transform p, Func<float, float, float, Vector3> map, float x, float y, float font, Color c, string text, TextAnchor anchor = TextAnchor.MiddleCenter, float maxW = 0f)
        {
            var align = anchor == TextAnchor.MiddleLeft || anchor == TextAnchor.UpperLeft ? TextAlignment.Left : anchor == TextAnchor.MiddleRight ? TextAlignment.Right : TextAlignment.Center;
            return _kit.Text(p, map(x, y, -0.0012f), font * U, anchor, align, c, text, maxW * U);
        }

        private static string OnOff(bool b) => b ? "ON" : "OFF";
    }
}
