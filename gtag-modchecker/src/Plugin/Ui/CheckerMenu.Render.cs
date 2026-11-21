using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FanGame.ModChecker;
using UnityEngine;
using LY = GTagModChecker.Layout;

namespace GTagModChecker
{
    /// <summary>Data inn, visning av hver fane og alle (lokale) handlinger.</summary>
    internal sealed partial class CheckerMenu
    {
        public void Open()
        {
            gameObject.SetActive(true);
            _nextHandLookup = 0f;
            _kit.BlockFor(0.3f); // ikke trykk noe ved et uhell når menyen åpnes
            _hoverId = null;
            _triggerWasDown = true; // krev at avtrekkeren slippes først
            UpdatePose();
            ShowPage(CurrentPage);
        }

        public void Close()
        {
            if (_laser != null) _laser.enabled = false;
            _kit?.ClearHover();
            if (_dot != null) _dot.gameObject.SetActive(false);
            if (_cam != null) _cam.PreviewVisible = false;
            gameObject.SetActive(false);
        }

        public void SetData(List<PlayerReport> reports, bool inRoom, string movementStatus, string networkStatus, int modEntries)
        {
            _reports = reports ?? new List<PlayerReport>();
            _inRoom = inRoom;
            _movement = movementStatus ?? "-";
            _network = networkStatus ?? "-";
            _modEntries = modEntries;
            if (!_toastShown) Render();
        }

        public void SetRigs(List<RigInfo> rigs) { _rigs = rigs ?? new List<RigInfo>(); }

        public void ShowPage(Page p)
        {
            CurrentPage = p;
            bool cam = p == Page.Camera;
            for (int i = 0; i < _pages.Length; i++) _pages[i].gameObject.SetActive(i == (int)p);
            _left.gameObject.SetActive(!cam);
            _camPanel.gameObject.SetActive(cam);
            PlaceStrip(cam);
            string title = PageTitles[(int)p];
            _title.Cell = (title.Length <= 8 ? LY.TitleFont : LY.TitleFontLong) * U;
            _title.text = title;
            for (int i = 0; i < _tabs.Length; i++) _kit.SetStyle(_tabs[i], i == (int)p + 1 ? UiKit.Style.Active : UiKit.Style.Silver);
            if (_cam != null) _cam.PreviewVisible = cam && gameObject.activeSelf;
            if (_player != null) _player.gameObject.SetActive(_selected != null && !cam);
            _toastShown = false;
            Render();
        }

        private void Render()
        {
            if (_kit == null) return;
            RenderLobby();
            RenderTags();
            RenderWorld();
            RenderSpotify();
            RenderChecker();
            RenderCamera();
            RenderPlayer();
        }

        private void RenderLobby()
        {
            int flagged = _reports.Count(r => r.Verdict != Verdict.CLEAN);
            string room = PhotonFeed.RoomName;
            _lobbyStatus.gameObject.SetActive(_showStatus);
            _lobbyStatus.text = _inRoom
                ? $"ROOM {UiKit.Trunc(room ?? "?", 10)} · {_reports.Count + 1} PLAYERS · <color={(flagged > 0 ? UiKit.RedHex : UiKit.GreenHex)}>{flagged} FLAGGED</color>"
                : "<color=" + UiKit.DimHex + ">NOT IN A ROOM</color>";
            for (int i = 0; i < _playerBtns.Length; i++)
            {
                var b = _playerBtns[i];
                if (i < _reports.Count)
                {
                    var r = _reports[i];
                    b.Go.SetActive(true);
                    bool sel = r.PlayerId == _selected;
                    b.Label.text = PlayerText.Name(r, 12).ToUpperInvariant();
                    _kit.SetStyle(b, sel ? UiKit.Style.Active : UiKit.Style.Silver);
                    _flagMarks[i].gameObject.SetActive(r.Verdict != Verdict.CLEAN);
                    _flagMarks[i].sharedMaterial = _kit.Mat(r.Verdict == Verdict.CHEATING ? UiKit.RedOnSilver : UiKit.Hex("C98A00"));
                }
                else b.Go.SetActive(false);
            }
            _lobbyEmpty.text = _reports.Count == 0
                ? (_inRoom ? "NO OTHER PLAYERS" : "JOIN A ROOM") + "\n<color=" + UiKit.DimHex + ">POINT SELECT GUN AT A\nPLAYER OR TAP A NAME</color>"
                : "";
            _kit.SetStyle(_gunBtn, _gun && S.LaserEnabled.Value ? UiKit.Style.Active : UiKit.Style.Silver);
            _gunBtn.Label.text = S.LaserEnabled.Value ? "SELECT GUN" : "SELECT GUN (OFF)";
        }

        private void RenderTags()
        {
            if (_tagRows == null) return;
            string[] labels =
            {
                "NAMETAGS: " + OnOff(S.Nametags.Value),
                "FONT: " + (S.TagFont.Value == NametagFont.Bold ? "BOLD" : "DEFAULT"),
                "SELF TAGS: " + OnOff(S.SelfTags.Value),
                "SHOW SPEC TAG: " + OnOff(!S.HideSpecTag.Value),
                "PLATFORMS: " + OnOff(S.TagPlatforms.Value),
                "HZ: " + OnOff(S.TagFpsHz.Value),
                "VERDICT: " + OnOff(S.TagVerdict.Value),
            };
            bool[] on = { S.Nametags.Value, S.TagFont.Value == NametagFont.Bold, S.SelfTags.Value, !S.HideSpecTag.Value, S.TagPlatforms.Value, S.TagFpsHz.Value, S.TagVerdict.Value };
            for (int i = 0; i < _tagRows.Length; i++)
            {
                _tagRows[i].Label.text = labels[i];
                _kit.SetStyle(_tagRows[i], on[i] ? UiKit.Style.Active : UiKit.Style.Silver);
            }
        }

        private void RenderWorld()
        {
            if (_rainBtn == null || CurrentPage != Page.World) return;
            bool timeOk = WorldSettings.TimeAvailable, rainOk = WorldSettings.RainAvailable;
            bool rain = WorldSettings.RainOn;
            _rainBtn.Label.text = rainOk ? "TOGGLE RAIN: " + OnOff(rain) : "TOGGLE RAIN (N/A)";
            _kit.SetStyle(_rainBtn, !rainOk ? UiKit.Style.Disabled : rain ? UiKit.Style.Active : UiKit.Style.Silver);
            int? set = WorldSettings.LastSetTime;
            if (!timeOk) _timeLabel.text = "TIME: N/A";
            else if (set == null) _timeLabel.text = "TIME: DEFAULT";
            else { string n = WorldSettings.TimeName(set.Value); _timeLabel.text = "TIME " + set.Value + (n != null ? " · " + n.ToUpperInvariant() : ""); }
            _kit.SetStyle(_timeMinus, timeOk ? UiKit.Style.Silver : UiKit.Style.Disabled);
            _kit.SetStyle(_timePlus, timeOk ? UiKit.Style.Silver : UiKit.Style.Disabled);
            for (int i = 0; i < _timeRows.Length; i++)
                _kit.SetStyle(_timeRows[i], !timeOk ? UiKit.Style.Disabled : set == TimePresets[i] ? UiKit.Style.Active : UiKit.Style.Silver);
            string block = WorldSettings.BlockReason;
            _worldNote.text = (!timeOk && !rainOk) ? "NOT FOUND IN THIS GORILLA TAG VERSION"
                : "LOCAL ONLY: ONLY YOU SEE IT\nTAP AGAIN = DEFAULT" + (block != null ? "\n<color=" + UiKit.YellowHex + ">" + block + "</color>" : "");
        }

        private void RenderSpotify()
        {
            if (_spotifyRows == null) return;
            string[] labels = _volumeMode
                ? new[] { "MUTE", "VOLUME +", "VOLUME -", "< SWITCH MODE" }
                : new[] { "PLAY/PAUSE", "NEXT TRACK >", "< PREVIOUS", "< SWITCH MODE" };
            for (int i = 0; i < 4; i++)
            {
                _spotifyRows[i].Label.text = labels[i];
                _kit.SetStyle(_spotifyRows[i], i == 0 ? UiKit.Style.Active : UiKit.Style.Silver);
            }
            string np = MediaKeys.NowPlaying;
            _nowPlaying.text = !string.IsNullOrEmpty(np) ? np.ToUpperInvariant() : (MediaKeys.SpotifyRunning ? "PAUSED" : "SPOTIFY NOT OPEN");
            _spotifyMode.text = _volumeMode ? "MODE: PC VOLUME" : "MODE: TRACKS";
        }

        private void RenderChecker()
        {
            if (_checkerRows == null) return;
            string speed = SpeedCheckLabel?.Invoke() ?? "SAFE";
            string[] labels =
            {
                "MENU STYLE: PANEL",
                "AUTO SCAN: " + OnOff(S.AutoCheck.Value),
                "LASER: " + OnOff(S.LaserEnabled.Value),
                "FINGER DOT: " + OnOff(S.ShowFingerDot.Value),
                "SPEED CHECK: " + speed,
            };
            bool[] on = { true, S.AutoCheck.Value, S.LaserEnabled.Value, S.ShowFingerDot.Value, speed != "OFF" };
            for (int i = 0; i < _checkerRows.Length; i++)
            {
                _checkerRows[i].Label.text = labels[i];
                _kit.SetStyle(_checkerRows[i], on[i] ? UiKit.Style.Active : UiKit.Style.Silver);
            }
            int flagged = _reports.Count(r => r.Verdict != Verdict.CLEAN);
            var sb = new StringBuilder();
            sb.Append("PLAYERS ").Append(_reports.Count).Append(" · FLAGGED <color=").Append(flagged > 0 ? UiKit.RedHex : UiKit.GreenHex).Append('>').Append(flagged).Append("</color>\n");
            sb.Append("MOD LIST ").Append(_modEntries).Append(" · MOVE ").Append(_movement.ToUpperInvariant()).Append(" · NET ").Append(_network.ToUpperInvariant()).Append('\n');
            sb.Append("OPEN: X ").Append(S.TriggerMode.Value == TriggerMode.DoublePress ? "(DOUBLE)" : "(SINGLE)").Append(" · LASER ").Append(LaserModeText()).Append('\n');
            sb.Append("<color=").Append(UiKit.DimHex).Append(">READ-ONLY · SENDS NOTHING</color>");
            _checkerStatus.text = sb.ToString();
        }

        private string SelectedName()
        {
            var r = _reports.FirstOrDefault(x => x.PlayerId == _selected);
            return r != null ? PlayerText.Name(r, 12) : null;
        }

        private string LaserModeText()
        {
            if (!S.LaserEnabled.Value) return "OFF";
            switch (S.LaserSelect.Value)
            {
                case LaserSelectMode.Trigger: return "TRIGGER";
                case LaserSelectMode.Dwell: return "POINT";
                default: return ReadTriggerSafe() != null ? "TRIGGER" : "POINT";
            }
        }

        private RigInfo RigFor(string playerId) => playerId == null ? null : _rigs.FirstOrDefault(r => r != null && r.PlayerId == playerId && !r.IsLocal);

        private void RenderPlayer()
        {
            if (_player == null) return;
            if (_selected == null) { _player.gameObject.SetActive(false); return; }
            for (int i = 0; i < _footerLinks.Length; i++)
            {
                _footerLinks[i].TextColor = i == (int)CurrentPlayerTab ? UiKit.White : UiKit.Dim;
                _kit.SetStyle(_footerLinks[i], UiKit.Style.Link);
            }

            var r = _reports.FirstOrDefault(x => x.PlayerId == _selected);
            var rig = RigFor(_selected);
            string toast = Toast;

            if (r == null)
            {
                _pName.text = "PLAYER LEFT";
                _pLine1.text = _pLine2.text = _pJoined.text = "";
                _pStatus.text = toast ?? "<color=" + UiKit.DimHex + ">NO LONGER IN ROOM</color>";
                SetPortraitColor(null);
                _profileGroup.gameObject.SetActive(false);
                _pList.text = "";
                return;
            }

            _pName.text = PlayerText.Name(r, 16).ToUpperInvariant();
            string plat = PlayerText.Platform(r, rig) ?? "?";
            int? fps = PlayerText.Fps(r, rig);
            _pLine1.text = plat + " | " + (fps != null ? fps + "FPS" : "?FPS");
            string col = PlayerText.Color09(rig, r);
            _pLine2.text = "COLOR " + (col ?? "?");
            var seen = PhotonFeed.FirstSeen(r.PlayerId);
            _pJoined.text = seen != null ? seen.Value.ToString("HH:mm") + " (ROOM)" : "-";
            _pStatus.text = toast != null ? "<color=" + UiKit.WhiteHex + ">" + toast + "</color>"
                : $"<color={UiKit.VerdictHex(r.Verdict)}>{r.StatusText}</color>{(r.InGracePeriod ? " <color=" + UiKit.DimHex + ">NEW</color>" : "")}";
            SetPortraitColor(PlayerText.RawColor(rig, r));

            _profileGroup.gameObject.SetActive(CurrentPlayerTab == PlayerTab.Profile);
            var sb = new StringBuilder();
            switch (CurrentPlayerTab)
            {
                case PlayerTab.Profile:
                {
                    float? vol = Voice?.Volume(rig);
                    bool hasVoice = vol != null && rig?.Voice != null;
                    _volValue.text = hasVoice ? Mathf.RoundToInt(vol.Value * 100f) + "%" : "N/A";
                    _kit.SetStyle(_volDown, hasVoice ? UiKit.Style.Silver : UiKit.Style.Disabled);
                    _kit.SetStyle(_volUp, hasVoice ? UiKit.Style.Silver : UiKit.Style.Disabled);
                    bool muted = Voice != null && Voice.Muted(rig);
                    _kit.SetIcon(_mute.Icon, muted ? "micoff" : "mic");
                    _kit.SetStyle(_mute, !hasVoice ? UiKit.Style.Disabled : muted ? UiKit.Style.Silver : UiKit.Style.Active);
                    _kit.SetStyle(_infoBtn, UiKit.Style.Silver);
                    _modsBtn.Label.text = r.AllowedModCount.ToString();
                    _cheatsBtn.Label.text = r.CheatCount.ToString();
                    _pList.text = "";
                    break;
                }
                case PlayerTab.AllowedMods:
                    sb.Append("<color=").Append(UiKit.SilverHex).Append(">ALLOWED MODS (").Append(r.AllowedModCount).Append(")</color>\n");
                    if (r.AllowedModCount == 0) sb.Append("<color=").Append(UiKit.DimHex).Append(">NONE</color>");
                    foreach (var m in r.AllowedMods.Take(16)) sb.Append(UiKit.Trunc(m, 34).ToUpperInvariant()).Append('\n');
                    if (r.AllowedModCount > 16) sb.Append("+").Append(r.AllowedModCount - 16).Append(" MORE");
                    _pList.text = sb.ToString().TrimEnd('\n');
                    break;
                case PlayerTab.Cheats:
                    sb.Append("<color=").Append(UiKit.SilverHex).Append(">CHEATS (").Append(r.CheatCount).Append(")</color>\n");
                    if (r.CheatCount == 0 && r.UnsureCount == 0) sb.Append("<color=").Append(UiKit.GreenHex).Append(">NONE DETECTED</color>");
                    foreach (var c in r.Cheats.Take(13)) sb.Append("<color=").Append(UiKit.RedHex).Append('>').Append(UiKit.Trunc(c, 34).ToUpperInvariant()).Append("</color>\n");
                    if (r.CheatCount > 13) sb.Append("+").Append(r.CheatCount - 13).Append(" MORE\n");
                    foreach (var u in r.Unsure.Take(3)) sb.Append("<color=").Append(UiKit.YellowHex).Append(">? ").Append(UiKit.Trunc(u, 32).ToUpperInvariant()).Append("</color>\n");
                    _pList.text = sb.ToString().TrimEnd('\n');
                    break;
                default: // Info
                {
                    string gm = PhotonFeed.RoomGameMode();
                    string uid = PhotonFeed.UserId(PhotonFeed.ActorOf(r.PlayerId));
                    sb.Append("<color=").Append(UiKit.SilverHex).Append(">INFO</color>\n");
                    sb.Append("SCORE ").Append(r.Score.ToString("0.00")).Append('\n');
                    sb.Append("GAMEMODE ").Append(gm != null ? UiKit.Trunc(gm.ToUpperInvariant(), 24) : "?").Append('\n');
                    sb.Append("USER ID ").Append(uid != null ? UiKit.Trunc(uid, 22) : "?").Append('\n');
                    foreach (var d in r.Detections.Take(8)) sb.Append("<color=").Append(UiKit.YellowHex).Append('>').Append(UiKit.Trunc(d.Code + " " + d.Label, 34).ToUpperInvariant()).Append("</color>\n");
                    _pList.text = sb.ToString().TrimEnd('\n');
                    break;
                }
            }
        }

        private void SetPortraitColor(Color? c)
        {
            if (_portrait == null) return;
            var mats = _portrait.sharedMaterials;
            if (mats.Length < 1) return;
            mats[0] = _kit.Mat(c != null ? new Color(c.Value.r, c.Value.g, c.Value.b, 1f) : UiKit.Hex("3C3E42"));
            _portrait.sharedMaterials = mats;
        }

        // =====================================================================
        //  Handlinger (alle lokale)
        // =====================================================================

        private void SelectSlot(int i)
        {
            if (i >= _reports.Count) return;
            string id = _reports[i].PlayerId;
            if (id == _selected) { ClosePlayer(); return; } // trykk igjen = lukk spillerpanelet
            SelectPlayer(id, "list");
        }

        public void SelectPlayer(string playerId, string source)
        {
            if (playerId == null) return;
            _selected = playerId;
            CurrentPlayerTab = PlayerTab.Profile;
            _toastUntil = 0; _toastShown = false;
            _player.gameObject.SetActive(CurrentPage != Page.Camera);
            var r = _reports.FirstOrDefault(x => x.PlayerId == playerId);
            Plugin.Log.LogInfo($"[GTag Mod Checker] Meny: valgte {(r != null ? PlayerText.Name(r) : playerId)} ({playerId}) via {source}");
            Render();
        }

        private void ClosePlayer()
        {
            _selected = null;
            _player.gameObject.SetActive(false);
            Render();
        }

        private void ToggleGun()
        {
            if (!S.LaserEnabled.Value) { ShowToast("LASER IS OFF (CHECKER TAB)"); return; }
            _gun = !_gun;
            if (!_gun) { _hoverId = null; if (_laser != null) _laser.enabled = false; }
            RenderLobby();
        }

        private void SwitchToDisc()
        {
            S.Style.Value = MenuStyle.Disc;
            Plugin.Log.LogInfo("[GTag Mod Checker] MENU STYLE -> DISC");
            OnStyleChanged?.Invoke();
        }

        private void DoReport()
        {
            if (_selected == null) return;
            try { OnReport?.Invoke(_selected); } // KUN lokal logg
            catch (Exception ex) { Plugin.Log.LogError("[GTag Mod Checker] Lokal rapport feilet: " + ex.Message); }
            ShowToast("REPORTED (LOCAL LOG)");
        }

        private void VolumeStep(float delta)
        {
            var rig = RigFor(_selected);
            if (Voice == null || !Voice.Step(rig, delta)) { ShowToast("VOLUME NOT FOUND"); return; }
            RenderPlayer();
        }

        private void ToggleMute()
        {
            var rig = RigFor(_selected);
            if (Voice == null || !Voice.ToggleMute(rig)) { ShowToast("VOLUME NOT FOUND"); return; }
            RenderPlayer();
        }

        private void StepTime(int d)
        {
            int cur = WorldSettings.LastSetTime ?? WorldSettings.CurrentTime ?? 0;
            if (!WorldSettings.SetTime(cur + d)) ShowToast(WorldSettings.BlockReason ?? "TIME N/A");
            RenderWorld();
        }

        private void PresetTime(int i)
        {
            if (WorldSettings.LastSetTime == TimePresets[i]) { WorldSettings.ResetTime(); ShowToast("TIME: DEFAULT"); }
            else if (!WorldSettings.SetTime(TimePresets[i])) ShowToast(WorldSettings.BlockReason ?? "TIME N/A");
            RenderWorld();
        }

        private void PressSpotify(int i)
        {
            bool ok = true;
            if (i == 3) _volumeMode = !_volumeMode;
            else if (_volumeMode) ok = i == 0 ? MediaKeys.Mute() : i == 1 ? MediaKeys.VolumeUp() : MediaKeys.VolumeDown();
            else ok = i == 0 ? MediaKeys.PlayPause() : i == 1 ? MediaKeys.Next() : MediaKeys.Previous();
            RenderSpotify();
            if (!ok) ShowToast("MEDIA KEY FAILED");
            else if (i == 3) ShowToast(_volumeMode ? "MODE: PC VOLUME" : "MODE: TRACKS");
        }

        /// <summary>Kort melding (2,5 s) i statuslinjen til fanen som er åpen (eller på spillerpanelet).</summary>
        private void ShowToast(string text)
        {
            _toastText = text;
            _toastUntil = Time.unscaledTime + 2.5f;
            _toastShown = true;
            Plugin.Log.LogInfo("[GTag Mod Checker] Meny: " + text);
            if (_selected != null && _player.gameObject.activeSelf) { RenderPlayer(); return; }
            switch (CurrentPage)
            {
                case Page.Lobby: _lobbyStatus.gameObject.SetActive(true); _lobbyStatus.text = text; break;
                case Page.Spotify: _spotifyMode.text = text; break;
                case Page.Checker: _checkerStatus.text = text; break;
                case Page.World: _worldNote.text = text; break;
                case Page.Camera: _scrTop.text = text; break;
                default: _title.text = text.Length <= 18 ? text : PageTitles[(int)CurrentPage]; break;
            }
        }
        private bool _toastShown;
    }
}
