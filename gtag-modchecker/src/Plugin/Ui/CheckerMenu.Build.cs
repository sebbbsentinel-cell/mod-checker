using System;
using UnityEngine;
using LY = GTagModChecker.Layout;

namespace GTagModChecker
{
    /// <summary>Bygging av venstre panel, fanestripe og spillerpanel.</summary>
    internal sealed partial class CheckerMenu
    {
        private void Build()
        {
            _kit = new UiKit();
            BuildLeft();
            BuildStrip();
            BuildPlayer();
            BuildCamera();
            BuildLaser();
            BuildDot();
            ShowPage(Page.Lobby);
        }

        private void BuildLeft()
        {
            _left = _kit.Child(transform, "LeftPanel", Vector3.zero);
            _kit.Panel(_left, LY.PW * U, LY.PH * U, LY.PR * U, LY.Edge * U);
            _title = Txt(_left, L, LY.PW / 2f, LY.TitleY, LY.TitleFont, UiKit.White, "LOBBY", TextAnchor.MiddleCenter, LY.PW - 30f);
            Txt(_left, L, LY.PW / 2f, LY.FooterY, LY.FooterFont, UiKit.Dim, "GTAG MOD CHECKER - by Seb");

            _pages = new Transform[PageTitles.Length];
            for (int i = 0; i < _pages.Length; i++) _pages[i] = _kit.Child(_left, "Page_" + PageTitles[i], Vector3.zero);

            BuildLobby(_pages[(int)Page.Lobby]);
            BuildNametags(_pages[(int)Page.Nametags]);
            BuildWorld(_pages[(int)Page.World]);
            BuildSpotify(_pages[(int)Page.Spotify]);
            BuildChecker(_pages[(int)Page.Checker]);
        }

        private void TopIcons(Transform page, string prefix, string[] icons, Action[] actions)
        {
            for (int i = 0; i < icons.Length; i++)
            {
                float x = LY.TopIconX0 + i * LY.TopIconStep;
                IconBtn(page, L, prefix + "_icon_" + icons[i], x, LY.TopIconY, x + LY.TopIconW, LY.TopIconY + LY.TopIconH, icons[i], actions[i]);
            }
        }

        private void BuildLobby(Transform p)
        {
            TopIcons(p, "lobby", new[] { "info", "tower", "window", "target", "people" }, new Action[]
            {
                () => { _showStatus = !_showStatus; RenderLobby(); },
                () => { OnRefresh?.Invoke(); ShowToast("LIST REFRESHED"); },
                () => { if (_selected == null) ShowToast("PICK A PLAYER FIRST"); else _player.gameObject.SetActive(!_player.gameObject.activeSelf); },
                () => ToggleGun(),
                () => { ClosePlayer(); ShowToast("SELECTION CLEARED"); },
            });
            int slots = LY.PlayerRows * 2;
            _playerBtns = new UiKit.Btn[slots];
            _flagMarks = new MeshRenderer[slots];
            for (int i = 0; i < slots; i++)
            {
                int idx = i;
                int row = i / 2, col = i % 2;
                float x0 = col == 0 ? LY.BX0 : LY.BX1 - LY.PlayerBtnW;
                float y0 = LY.PlayerRowY0 + row * LY.PlayerRowStep;
                var b = Btn(p, L, "player" + i, x0, y0, x0 + LY.PlayerBtnW, y0 + LY.PlayerBtnH, "", () => SelectSlot(idx), UiKit.Style.Silver, LY.PlayerFont);
                b.Label.MaxWidth = (LY.PlayerBtnW - 16f) * U;
                // liten farget firkant ytterst til høyre når spilleren er SUSPICIOUS/CHEATING
                _flagMarks[i] = _kit.Quad(b.Go.transform, "Flag", new Vector3((LY.PlayerBtnW / 2f - 6f) * U, 0, -0.0010f), 5f * U, 5f * U, UiKit.RedOnSilver);
                _playerBtns[i] = b;
            }
            _lobbyStatus = Txt(p, L, LY.PW / 2f, LY.LobbyStatusY, LY.LobbyStatusFont, UiKit.Silver, "", TextAnchor.MiddleCenter, LY.PW - 40f);
            _lobbyEmpty = Txt(p, L, LY.PW / 2f, 130f, 7f, UiKit.Silver, "", TextAnchor.MiddleCenter, LY.PW - 40f);
            _lobbyEmpty.LineSpacing = 1.8f;
            _gunBtn = Btn(p, L, "select_gun", LY.GunX0, LY.GunY0, LY.GunX1, LY.GunY1, "SELECT GUN", ToggleGun, UiKit.Style.Active, LY.GunFont);
        }

        private UiKit.Btn[] Rows(Transform p, string prefix, int n, float font, Action<int> press)
        {
            var arr = new UiKit.Btn[n];
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                float y0 = LY.RowY0 + i * LY.RowStep;
                arr[i] = Btn(p, L, prefix + i, LY.BX0, y0, LY.BX1, y0 + LY.RowH, "", () => press(idx), UiKit.Style.Silver, font);
            }
            return arr;
        }

        private void BuildNametags(Transform p)
        {
            TopIcons(p, "tags", new[] { "info", "tag", "grid" }, new Action[]
            {
                () => ShowToast("ONLY YOU SEE THESE TAGS"),
                () => { S.Nametags.Value = !S.Nametags.Value; RenderTags(); },
                () => { S.TagVerdict.Value = !S.TagVerdict.Value; RenderTags(); },
            });
            Action[] acts =
            {
                () => S.Nametags.Value = !S.Nametags.Value,
                () => S.TagFont.Value = S.TagFont.Value == NametagFont.Default ? NametagFont.Bold : NametagFont.Default,
                () => S.SelfTags.Value = !S.SelfTags.Value,
                () => S.HideSpecTag.Value = !S.HideSpecTag.Value,
                () => S.TagPlatforms.Value = !S.TagPlatforms.Value,
                () => S.TagFpsHz.Value = !S.TagFpsHz.Value,
                () => S.TagVerdict.Value = !S.TagVerdict.Value,
            };
            _tagRows = Rows(p, "tag", acts.Length, LY.RowFont, i => { acts[i](); RenderTags(); });
        }

        private void BuildWorld(Transform p)
        {
            TopIcons(p, "world", new[] { "info", "sun" }, new Action[]
            {
                () => ShowToast(WorldSettings.TimeAvailable ? "LOCAL ONLY: ONLY YOU SEE IT" : "NOT AVAILABLE IN THIS GT VERSION"),
                () => { if (WorldSettings.ResetTime()) ShowToast("TIME: DEFAULT"); RenderWorld(); },
            });
            _rainBtn = Btn(p, L, "rain", LY.BX0, LY.RainY0, LY.BX1, LY.RainY0 + LY.RainH, "TOGGLE RAIN", () =>
            {
                if (!WorldSettings.ToggleRain()) ShowToast(WorldSettings.BlockReason ?? "RAIN N/A");
                RenderWorld();
            }, UiKit.Style.Silver, LY.RowFont);
            _timeMinus = Btn(p, L, "time_minus", LY.BX0, LY.TimeY0, LY.BX0 + LY.TimeBtnW, LY.TimeY0 + LY.TimeH, "-", () => StepTime(-1), UiKit.Style.Silver, 14f);
            _timePlus = Btn(p, L, "time_plus", LY.BX1 - LY.TimeBtnW, LY.TimeY0, LY.BX1, LY.TimeY0 + LY.TimeH, "+", () => StepTime(1), UiKit.Style.Silver, 14f);
            _timeLabel = Txt(p, L, LY.PW / 2f, LY.TimeY0 + LY.TimeH / 2f, LY.TimeFont, UiKit.White, "TIME", TextAnchor.MiddleCenter, LY.BX1 - LY.BX0 - LY.TimeBtnW * 2f - 12f);
            _timeRows = new UiKit.Btn[TimeNames.Length];
            for (int i = 0; i < TimeNames.Length; i++)
            {
                int idx = i;
                float y0 = LY.WorldRowY0 + i * LY.RowStep;
                _timeRows[i] = Btn(p, L, "time_" + TimeNames[i].ToLowerInvariant(), LY.BX0, y0, LY.BX1, y0 + LY.RowH, TimeNames[i], () => PresetTime(idx), UiKit.Style.Silver, LY.WorldFont);
            }
            _worldNote = Txt(p, L, LY.PW / 2f, 322f, 5.5f, UiKit.Dim, "", TextAnchor.MiddleCenter, LY.PW - 40f);
            _worldNote.LineSpacing = 1.6f;
        }

        private void BuildSpotify(Transform p)
        {
            TopIcons(p, "spotify", new[] { "spotify" }, new Action[] { () => ShowToast(MediaKeys.SpotifyRunning ? "SPOTIFY IS RUNNING" : "OPEN SPOTIFY ON YOUR PC") });
            _spotifyRows = Rows(p, "spotify", 4, LY.RowFont, PressSpotify);
            Txt(p, L, LY.PW / 2f, LY.NowPlayingY, LY.SpotifyFont, UiKit.Dim, "NOW PLAYING:");
            _nowPlaying = Txt(p, L, LY.PW / 2f, LY.TrackY, LY.SpotifyFont, UiKit.Silver, "-", TextAnchor.MiddleCenter, LY.PW - 40f);
            Txt(p, L, LY.PW / 2f, LY.PrivateY, LY.PrivateFont, UiKit.Dim, "PRIVATE AUDIO: ONLY YOU HEAR IT", TextAnchor.MiddleCenter, LY.PW - 40f);
            _spotifyMode = Txt(p, L, LY.PW / 2f, LY.PrivateY + 14f, 5f, UiKit.Dim, "", TextAnchor.MiddleCenter, LY.PW - 40f);
        }

        private void BuildChecker(Transform p)
        {
            TopIcons(p, "checker", new[] { "info", "shield", "gear", "swap", "people" }, new Action[]
            {
                () => ShowToast("GTAG MOD CHECKER v" + Plugin.Version + " by Seb"),
                () => { OnRefresh?.Invoke(); ShowToast("RE-SCANNED"); },
                () => { S.DebugTouch.Value = !S.DebugTouch.Value; ShowToast("TOUCH DEBUG LOG: " + OnOff(S.DebugTouch.Value)); },
                SwitchToDisc,
                () => ShowPage(Page.Lobby),
            });
            _checkerRows = Rows(p, "checker", 5, LY.CheckerRowFont, i =>
            {
                switch (i)
                {
                    case 0: SwitchToDisc(); return;
                    case 1: S.AutoCheck.Value = !S.AutoCheck.Value; break;
                    case 2: S.LaserEnabled.Value = !S.LaserEnabled.Value; break;
                    case 3: S.ShowFingerDot.Value = !S.ShowFingerDot.Value; break;
                    case 4: ShowToast("SPEED CHECK: " + (CycleSpeedCheck?.Invoke() ?? "?")); break;
                }
                RenderChecker();
            });
            Txt(p, L, LY.PW / 2f, LY.NoteY, LY.NoteFont, UiKit.Dim, "TAP TO SWITCH: PANEL <> DISC", TextAnchor.MiddleCenter, LY.PW - 40f);
            _checkerStatus = _kit.Text(p, L(LY.BX0, 298f, -0.0012f), 5f * U, TextAnchor.UpperLeft, TextAlignment.Left, UiKit.Silver, "", (LY.BX1 - LY.BX0) * U);
            _checkerStatus.LineSpacing = 1.5f;
        }

        private void BuildStrip()
        {
            float h = LY.TabCount * LY.TabStep + 12f;
            _strip = _kit.Child(transform, "TabStrip", Vector3.zero);
            _kit.Panel(_strip, LY.StripW * U, h * U, LY.StripR * U, LY.Edge * U);
            _tabs = new UiKit.Btn[TabIcons.Length];
            for (int i = 0; i < TabIcons.Length; i++)
            {
                int idx = i;
                float cy = 8f + i * LY.TabStep + LY.TabSize / 2f; // fra toppen av stripen
                var pos = new Vector3(0, (h / 2f - cy) * U, 0);
                _tabs[i] = _kit.IconButton(_strip, "tab_" + TabIcons[i], pos, LY.TabSize * U, LY.TabSize * U, TabIcons[i], () =>
                {
                    if (idx == 0) { Plugin.Log.LogInfo("[GTag Mod Checker] Meny: POWER (lukk)"); Close(); }
                    else ShowPage((Page)(idx - 1));
                }, UiKit.Style.Silver, 0.64f);
            }
            PlaceStrip(false);
        }

        private void PlaceStrip(bool camera)
        {
            float h = LY.TabCount * LY.TabStep + 12f;
            float right = camera ? -LY.PW / 2f + LY.CW * LY.CamScale : LY.PW / 2f;
            float x = right + LY.StripGap + LY.StripW / 2f;
            float y = LY.PH / 2f - (LY.StripY + h / 2f);
            _strip.localPosition = new Vector3(x * U, y * U, 0);
        }

        private void BuildPlayer()
        {
            float cx = -LY.PW / 2f + LY.QX + LY.QW / 2f, cy = LY.PH / 2f - LY.QY - LY.QH / 2f;
            _player = _kit.Child(transform, "PlayerPanel", new Vector3(cx * U, cy * U, 0));
            var p = _player;
            _kit.Panel(p, LY.QW * U, LY.QH * U, LY.PR * U, LY.Edge * U);

            // portrett: sølvrute med pikselgorilla i spillerens farge
            float ps = LY.PortraitSize;
            _kit.RRect(p, "PortraitBg", Q(LY.PortraitX0 + ps / 2f, LY.PortraitY0 + ps / 2f, -0.0004f), ps * U, ps * U, 6f * U, _kit.Mat(UiKit.Silver));
            _portrait = _kit.PixelArt(p, "Portrait", Q(LY.PortraitX0 + ps / 2f, LY.PortraitY0 + ps / 2f + 2f, -0.0010f), LY.PortraitArt * U, PixelFont.Gorilla, "BFD",
                new[] { UiKit.Hex("3C3E42"), UiKit.Hex("969AA0"), UiKit.Hex("141416") });

            float tw = LY.QW - LY.InfoX - 12f;
            _pName = Txt(p, Q, LY.InfoX, LY.InfoY0, LY.InfoFont, UiKit.White, "", TextAnchor.MiddleLeft, tw);
            _pLine1 = Txt(p, Q, LY.InfoX, LY.InfoY0 + LY.InfoStep, LY.InfoFont, UiKit.White, "", TextAnchor.MiddleLeft, tw);
            _pLine2 = Txt(p, Q, LY.InfoX, LY.InfoY0 + LY.InfoStep * 2f, LY.InfoFont, UiKit.White, "", TextAnchor.MiddleLeft, tw);
            _pJoinedLabel = Txt(p, Q, LY.InfoX, LY.JoinedY, LY.InfoFont, UiKit.White, "Joined", TextAnchor.MiddleLeft, tw);
            _pJoined = Txt(p, Q, LY.InfoX, LY.JoinedValY, LY.InfoFont, UiKit.White, "", TextAnchor.MiddleLeft, tw);
            _pStatus = Txt(p, Q, LY.InfoX, LY.StatusY, 6f, UiKit.Silver, "", TextAnchor.MiddleLeft, tw);

            // PROFILE-innhold
            _profileGroup = _kit.Child(p, "Profile", Vector3.zero);
            var g = _profileGroup;
            float vy = LY.VolY0, vb = LY.VolBtn;
            _volDown = IconBtn(g, Q, "vol_down", 16f, vy, 16f + vb, vy + vb, "spk", () => VolumeStep(-0.1f));
            _volUp = IconBtn(g, Q, "vol_up", 46f, vy, 46f + vb, vy + vb, "spk2", () => VolumeStep(0.1f));
            _volLabel = Txt(g, Q, LY.VolLabelX, vy + 9f, LY.VolLabelFont, UiKit.White, "VOLUME");
            _volValue = Txt(g, Q, LY.VolLabelX, vy + 20f, LY.VolValueFont, UiKit.Silver, "");
            _infoBtn = IconBtn(g, Q, "player_info", LY.QW - 72f, vy, LY.QW - 46f, vy + vb, "chat", () => { CurrentPlayerTab = CurrentPlayerTab == PlayerTab.Info ? PlayerTab.Profile : PlayerTab.Info; RenderPlayer(); });
            _mute = IconBtn(g, Q, "mute", LY.QW - 42f, vy, LY.QW - 16f, vy + vb, "mic", ToggleMute, UiKit.Style.Active);
            _report = Btn(g, Q, "report", 16f, LY.ReportY0, LY.QW - 16f, LY.ReportY1, "REPORT", DoReport, UiKit.Style.Active, LY.ReportFont);
            _clone = Btn(g, Q, "clone", 16f, LY.CloneY0, LY.QW - 16f, LY.CloneY1, "CLONE COSMETICS", () => ShowToast("CLONE COSMETICS IS DISABLED"), UiKit.Style.Disabled, LY.CloneFont);
            _modsBtn = CountBtn(g, "mods", 16f, LY.QW / 2f - 5f, "MODS", UiKit.GreenOnSilver, () => { CurrentPlayerTab = PlayerTab.AllowedMods; RenderPlayer(); });
            _cheatsBtn = CountBtn(g, "cheats", LY.QW / 2f + 5f, LY.QW - 16f, "CHEATS", UiKit.RedOnSilver, () => { CurrentPlayerTab = PlayerTab.Cheats; RenderPlayer(); });

            // ALLOWED MODS / CHEATS / INFO: tekstliste i stedet for profilknappene
            _pList = _kit.Text(p, Q(16f, LY.ListY0, -0.0012f), LY.ListFont * U, TextAnchor.UpperLeft, TextAlignment.Left, UiKit.White, "", (LY.QW - 32f) * U);
            _pList.LineSpacing = 1.5f;

            // bunnlinje: PROFILE | ALLOWED MODS | CHEATS (trykkbare)
            string[] parts = { "PROFILE", "ALLOWED MODS", "CHEATS" };
            string full = "PROFILE  |  ALLOWED MODS  |  CHEATS";
            float cw = LY.PFooterFont; // monospace: én tegnrute per tegn
            float start = LY.QW / 2f - full.Length * cw / 2f;
            _footerLinks = new UiKit.Btn[parts.Length];
            int at = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                int k = full.IndexOf(parts[i], at, StringComparison.Ordinal); at = k + parts[i].Length;
                float x0 = start + k * cw, x1 = x0 + parts[i].Length * cw;
                var t = (PlayerTab)i;
                _footerLinks[i] = _kit.Button(p, "ptab_" + i, Q((x0 + x1) / 2f, LY.PFooterY), (x1 - x0 + 4f) * U, 12f * U, parts[i], () => { CurrentPlayerTab = t; RenderPlayer(); }, UiKit.Style.Link, LY.PFooterFont * U);
                _footerLinks[i].Label.MaxWidth = 0f;
            }
            foreach (int k in new[] { full.IndexOf('|'), full.LastIndexOf('|') })
                Txt(p, Q, start + k * cw + cw / 2f, LY.PFooterY, LY.PFooterFont, UiKit.Dim, "|");

            _player.gameObject.SetActive(false);
        }

        private UiKit.Btn CountBtn(Transform g, string id, float x0, float x1, string caption, Color c, Action a)
        {
            var b = Btn(g, Q, id, x0, LY.CountY0, x1, LY.CountY1, "0", a, UiKit.Style.Silver, LY.CountFont);
            b.TextColor = c;
            b.Label.transform.localPosition = new Vector3(0, 4f * U, -0.0012f);
            b.Caption = _kit.Text(b.Go.transform, new Vector3(0, -5f * U, -0.0012f), LY.CountFont * 0.8f * U, TextAnchor.MiddleCenter, TextAlignment.Center, c, caption);
            _kit.SetStyle(b, UiKit.Style.Silver);
            return b;
        }
    }
}
