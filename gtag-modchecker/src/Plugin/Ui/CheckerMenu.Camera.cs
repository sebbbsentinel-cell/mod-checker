using System;
using UnityEngine;
using LY = GTagModChecker.Layout;

namespace GTagModChecker
{
    /// <summary>CAMERA-fanen: bredt kameranettbrett med håndtak, ikonrutenett, live skjerm, modusknapper og opptaksdel.</summary>
    internal sealed partial class CheckerMenu
    {
        private float _screenAspect = 2.43f;
        private Material _screenMat;

        private void BuildCamera()
        {
            float s = LY.CamScale;
            float cx = -LY.PW / 2f + LY.CW * s / 2f, cy = LY.PH / 2f + LY.CBodyY0 * s - LY.CH * s / 2f;
            _camPanel = _kit.Child(transform, "CameraTablet", new Vector3(cx * U, cy * U, 0));
            _camPanel.localScale = new Vector3(s, s, s);
            var p = _camPanel;

            // stamme bak (litt lenger bak, så kroppen dekker toppen), kropp, ring øverst
            var stem = _kit.Child(p, "Stem", C((LY.StemX0 + LY.StemX1) / 2f, (LY.StemY0 + LY.CH) / 2f, 0.004f));
            _kit.Panel(stem, (LY.StemX1 - LY.StemX0) * U, (LY.CH - LY.StemY0) * U, LY.StemR * U, LY.Edge * U);
            var body = _kit.Child(p, "Body", C(LY.CW / 2f, (LY.CBodyY0 + LY.CBodyY1) / 2f));
            _kit.Panel(body, LY.CW * U, (LY.CBodyY1 - LY.CBodyY0) * U, LY.CR * U, LY.Edge * U);
            _kit.MeshObj(p, "Ring", C(LY.CW / 2f, 12f, 0.002f), _kit.Annulus(6f * U, 8f * U, 32), _kit.Mat(UiKit.Silver));

            // håndtak med grep-linjer
            foreach (float hx0 in new[] { LY.HandleX0, LY.CW - LY.HandleX0 - LY.HandleW })
            {
                float hcx = hx0 + LY.HandleW / 2f;
                _kit.RRect(p, "Handle", C(hcx, (LY.HandleY0 + LY.HandleY1) / 2f, -0.0004f), LY.HandleW * U, (LY.HandleY1 - LY.HandleY0) * U, 12f * U, _kit.Mat(UiKit.Silver));
                for (int k = 0; k < 5; k++) _kit.Quad(p, "Grip", C(hcx, 120f + k * 10f, -0.0008f), 24f * U, 1.5f * U, UiKit.Hex("969AA0"));
            }

            // venstre ikonrutenett (2 x 3): FLIP, PC-SKJERM, FOV, MÅL, SMOOTH, REC
            string[] kinds = { "rotate", "photo", "grid", "swap", "gear", "dot" };
            var grid = new UiKit.Btn[kinds.Length];
            Action[] acts =
            {
                () => { _cam.Flip = !_cam.Flip; ShowToast("FLIP: " + OnOff(_cam.Flip)); },
                () => { _cam.SetDesktop(!_cam.DesktopOut); ShowToast("PC SCREEN: " + OnOff(_cam.DesktopOut)); },
                () => { _cam.CycleFov(); ShowToast("FOV " + Mathf.RoundToInt(_cam.Fov)); },
                () => { _cam.TargetSelected = !_cam.TargetSelected; ShowToast(_cam.TargetSelected ? (_selected == null ? "PICK A PLAYER WITH SELECT GUN" : "TARGET: SELECTED PLAYER") : "TARGET: YOU"); },
                () => { _cam.CycleSmooth(); ShowToast("SMOOTH " + _cam.SmoothName); },
                () => _cam.ToggleRecording(),
            };
            for (int i = 0; i < kinds.Length; i++)
            {
                float x = LY.CGridX0 + (i % 2) * LY.CGridDX, y = LY.CGridY0 + (i / 2) * LY.CGridDY;
                var a = acts[i];
                grid[i] = IconBtn(p, C, "cam_" + kinds[i], x, y, x + LY.CGridSize, y + LY.CGridSize, kinds[i], () => { a(); RenderCamera(); });
            }
            _camFlip = grid[0]; _camDesk = grid[1]; _camFov = grid[2]; _camTarget = grid[3]; _camSmooth = grid[4]; _camRecSmall = grid[5];

            // skjerm: hvit ramme + live bilde
            _kit.RRect(p, "ScreenBezel", C((LY.ScreenX0 + LY.ScreenX1) / 2f, (LY.ScreenY0 + LY.ScreenY1) / 2f, -0.0004f), (LY.ScreenX1 - LY.ScreenX0) * U, (LY.ScreenY1 - LY.ScreenY0) * U, LY.ScreenR * U, _kit.Mat(UiKit.White));
            float sx0 = LY.ScreenX0 + LY.ScreenInset, sy0 = LY.ScreenY0 + LY.ScreenInset, sx1 = LY.ScreenX1 - LY.ScreenInset, sy1 = LY.ScreenY1 - LY.ScreenInset;
            _screen = _kit.Quad(p, "Screen", C((sx0 + sx1) / 2f, (sy0 + sy1) / 2f, -0.0008f), (sx1 - sx0) * U, (sy1 - sy0) * U, UiKit.Hex("2A2C31"));
            _screenAspect = (sx1 - sx0) / (sy1 - sy0);

            float b = LY.ScreenBtn;
            _scrRecenter = IconBtn(p, C, "scr_recenter", sx0 + 4f, sy0 + 4f, sx0 + 4f + b, sy0 + 4f + b, "rotate", () => { _cam.Recenter(); ShowToast("CAMERA RECENTERED"); }, UiKit.Style.Active);
            _scrDesk = IconBtn(p, C, "scr_desktop", sx1 - 4f - b * 2f - 6f, sy0 + 4f, sx1 - 4f - b - 6f, sy0 + 4f + b, "photo", () => { _cam.SetDesktop(!_cam.DesktopOut); ShowToast("PC SCREEN: " + OnOff(_cam.DesktopOut)); }, UiKit.Style.Active);
            _scrSmooth = IconBtn(p, C, "scr_settings", sx1 - 4f - b, sy0 + 4f, sx1 - 4f, sy0 + 4f + b, "gear", () => { _cam.CycleSmooth(); ShowToast("SMOOTH " + _cam.SmoothName); }, UiKit.Style.Active);
            foreach (var x in new[] { _scrRecenter, _scrDesk, _scrSmooth }) x.Go.transform.localPosition += new Vector3(0, 0, -0.0012f);
            _scrTop = _kit.Text(p, C((sx0 + sx1) / 2f, sy0 + 15f, -0.0016f), 6f * U, TextAnchor.MiddleCenter, TextAlignment.Center, UiKit.White, "", (sx1 - sx0 - 120f) * U);
            _scrBottom = _kit.Text(p, C(sx0 + 8f, sy1 - 12f, -0.0016f), 5f * U, TextAnchor.MiddleLeft, TextAlignment.Left, UiKit.White, "", (sx1 - sx0 - 90f) * U);
            _recDot = _kit.Icon(p, "RecDot", C(sx1 - 16f, sy1 - 12f, -0.0016f), 14f * U, "rec", UiKit.RecRed);
            var recLbl = Txt(p, C, sx1 - 28f, sy1 - 12f, 6f, UiKit.White, "REC", TextAnchor.MiddleRight);
            recLbl.transform.localPosition += new Vector3(0, 0, -0.0004f);

            // modusknapper
            string[] modes = { "FIRST\nPERSON", "THIRD\nPERSON", "FOLLOW\nMODE", "TRACK\nMONKEY", "LOCK", "GOPRO" };
            _modeBtns = new UiKit.Btn[modes.Length];
            float total = modes.Length * LY.ModeW + (modes.Length - 1) * LY.ModeGap;
            float mx = LY.CW / 2f - total / 2f;
            for (int i = 0; i < modes.Length; i++)
            {
                var m = (CamMode)i;
                bool two = modes[i].Contains("\n");
                var mb = Btn(p, C, "cam_mode_" + m.ToString().ToLowerInvariant(), mx, LY.ModeY0, mx + LY.ModeW, LY.ModeY1, modes[i], () => { _cam.SetMode(m); RenderCamera(); }, UiKit.Style.Silver, two ? LY.ModeFont : 7f);
                mb.Label.LineSpacing = 2.0f;
                mb.Label.MaxWidth = (LY.ModeW - 6f) * U;
                _modeBtns[i] = mb;
                mx += LY.ModeW + LY.ModeGap;
            }

            // stamme: opptaksinfo, mappe og stor REC/STOP-knapp
            _recText = _kit.Text(p, C(LY.RecTextX, LY.RecY0 - LY.RecFont * 7f / 16f, -0.0012f), LY.RecFont * U, TextAnchor.UpperCenter, TextAlignment.Center, UiKit.White, "", 150f * U);
            _recText.LineSpacing = LY.RecStep / LY.RecFont;
            _recSub = Txt(p, C, LY.RecTextX, LY.RecSubY, 6f, UiKit.Silver, "ON VR");
            _folderBtn = _kit.Button(p, "cam_folder", C(LY.FolderX, LY.FolderY), 32f * U, 32f * U, "", () => ShowToast("SAVE VIDEO: RECORD THE PC WINDOW (OBS / GAME BAR)"), UiKit.Style.Link);
            _kit.Icon(_folderBtn.Go.transform, "Icon", new Vector3(0, 0, -0.0012f), 28f * U, "folder", UiKit.Silver);
            _recBtn = _kit.Button(p, "cam_rec", C(LY.RecBtnX, LY.RecBtnY), LY.RecBtnSize * U, LY.RecBtnSize * U, "", () => { _cam.ToggleRecording(); RenderCamera(); }, UiKit.Style.Silver, 0.001f, LY.RecBtnSize / 2f * U);
            if (_recBtn.Bevel != null) _recBtn.Bevel.enabled = false;
            _recBtn.Icon = _kit.SwappableIcon(_recBtn.Go.transform, "Icon", new Vector3(0, 0, -0.0012f), 22f * U, "rec", UiKit.Dark);
            Txt(p, C, LY.CW / 2f, LY.CH - 14f, 5f, UiKit.Dim, "GTAG MOD CHECKER CAM - by Seb");

            _cam = CameraMod.Create();
            _cam.SelectedHead = () => RigFor(_selected)?.Head;
            _cam.TextureChanged += ApplyScreenTexture;
            ApplyScreenTexture();
            _camPanel.gameObject.SetActive(false);
        }

        /// <summary>Viser kameraets RenderTexture på skjermen, beskåret så 16:9-bildet fyller den brede skjermen.</summary>
        private void ApplyScreenTexture()
        {
            if (_screen == null || _cam == null || _cam.Texture == null) return;
            var tex = _cam.Texture;
            if (_screenMat == null) _screenMat = _kit.TexMat(tex);
            if (_screenMat == null) return;
            if (_screenMat.HasProperty("_BaseMap")) _screenMat.SetTexture("_BaseMap", tex);
            if (_screenMat.HasProperty("_MainTex")) _screenMat.SetTexture("_MainTex", tex);
            _screenMat.mainTexture = tex;
            float vis = Mathf.Clamp01(((float)tex.width / tex.height) / _screenAspect);
            var scale = new Vector2(1f, vis);
            var off = new Vector2(0f, (1f - vis) / 2f);
            _screenMat.mainTextureScale = scale;
            _screenMat.mainTextureOffset = off;
            if (_screenMat.HasProperty("_BaseMap")) { _screenMat.SetTextureScale("_BaseMap", scale); _screenMat.SetTextureOffset("_BaseMap", off); }
            _screen.sharedMaterial = _screenMat;
            _screen.enabled = true;
        }

        private void RenderCamera()
        {
            if (_cam == null || _modeBtns == null) return;
            for (int i = 0; i < _modeBtns.Length; i++) _kit.SetStyle(_modeBtns[i], (int)_cam.Mode == i ? UiKit.Style.Active : UiKit.Style.Silver);
            _kit.SetStyle(_camFlip, _cam.Flip ? UiKit.Style.Active : UiKit.Style.Silver);
            _kit.SetStyle(_camDesk, _cam.DesktopOut ? UiKit.Style.Active : UiKit.Style.Silver);
            _kit.SetStyle(_camTarget, _cam.TargetSelected ? UiKit.Style.Active : UiKit.Style.Silver);
            _kit.SetStyle(_camRecSmall, _cam.Recording ? UiKit.Style.Active : UiKit.Style.Silver);
            string[] names = { "FIRST PERSON", "THIRD PERSON", "FOLLOW", "TRACK MONKEY", "LOCK", "GOPRO" };
            string target = _cam.TargetSelected && _selected != null ? UiKit.Trunc(SelectedName() ?? "PLAYER", 10).ToUpperInvariant() : "YOU";
            if (!_toastShown) _scrTop.text = names[(int)_cam.Mode] + " · " + target;
            _scrBottom.text = "FOV " + Mathf.RoundToInt(_cam.Fov) + "  SMOOTH " + _cam.SmoothName + "  FLIP " + OnOff(_cam.Flip) + "  PC " + OnOff(_cam.DesktopOut);
            _recDot.sharedMaterial = _kit.Mat(_cam.Recording ? UiKit.RecRed : UiKit.Hex("5A5E64"));
            _recText.text = _cam.Recording ? "RECORDING\nPC SCREEN\n" + _cam.RecTime : "CAMERA READY\n" + (_cam.DesktopOut ? "PC SCREEN ON" : "PREVIEW") + "\n" + _cam.RecTime;
            _recSub.text = _cam.DesktopOut ? "ON PC" : "ON VR";
            _kit.SetIcon(_recBtn.Icon, _cam.Recording ? "stop" : "rec");
            _recBtn.TextColor = _cam.Recording ? UiKit.Dark : UiKit.RecRed;
            _kit.SetStyle(_recBtn, UiKit.Style.Silver);
        }
    }
}
