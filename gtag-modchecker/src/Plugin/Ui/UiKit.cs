using System;
using System.Collections.Generic;
using UnityEngine;

namespace GTagModChecker
{
    /// <summary>
    /// Felles byggeklosser for menyene i "sigma"-stil: nesten svarte paneler med sølvkant og myk sølvglød,
    /// sølv/hvite knapper med mørk pikseltekst. Kun egne mesher (pikseltekst, avrundede rektangler).
    /// INGEN collidere og ingen raycasts: knapper trykkes ved å måle avstanden fra høyre pekefinger til knappen,
    /// og den NÆRMESTE knappen vinner.
    /// </summary>
    internal sealed class UiKit
    {
        // ---- Sigma-palett ----
        public static readonly Color PanelBg = Hex("0B0B0D"), Panel2 = Hex("16171A"), Silver = Hex("C8CCD2"), White = Hex("E9ECEF"),
            Dark = Hex("0B0B0D"), Dim = Hex("5A5E64"), InnerLine = Hex("282A2F"), Bevel = Hex("8C9096"),
            DisabledFill = Hex("6E7278"), DisabledText = Hex("282A2E"), SilverHover = Hex("DDE0E4"), WhiteHover = Hex("FFFFFF"),
            GreenOnSilver = Hex("1F8A48"), RedOnSilver = Hex("BE2837"), RecRed = Hex("D22832"), Black = Hex("050506");
        public static readonly Color SilverGlow = new Color(0.86f, 0.88f, 0.91f, 0.10f), SilverGlowInner = new Color(0.86f, 0.88f, 0.91f, 0.20f);
        public const string GreenHex = "#3DDC84", YellowHex = "#FFC830", RedHex = "#FF4D4D", SilverHex = "#C8CCD2", DimHex = "#7A7E85", WhiteHex = "#E9ECEF";
        public const string MutedHex = DimHex, CaptionGreyHex = SilverHex, PinkHex = SilverHex; // gamle navn (disc/navnelapper)

        // ---- gamle tema-farger (disc kan fortsatt velge VIOLET/PINK) ----
        public static readonly Color Pink = Hex("FF3EA5"), Violet = Hex("8B5CFF"), CaptionGrey = Hex("A9A6C4"), Grey = Hex("3E363C");
        public static readonly Color BoxFill = Hex("0E0E11");

        public enum Style { Silver, Active, Disabled, Box, Link }

        /// <summary>Aksentfarge (kant/glød). Sølv som standard; disc kan bruke VIOLET/PINK.</summary>
        public Color Accent = Silver;

        public sealed class Btn
        {
            public string Id;
            public GameObject Go;
            public float W, H;
            public Action OnPress;
            public MeshRenderer Border, Fill, Bevel, Icon;
            public PText Label;
            public PText Caption;
            public Style Style;
            /// <summary>Overstyrer tekstfargen (f.eks. grønn/rød tall på MODS/CHEATS). Null = stilens farge.</summary>
            public Color? TextColor;
        }

        public readonly List<Btn> Buttons = new List<Btn>();
        private readonly Dictionary<string, Material> _mats = new Dictionary<string, Material>();
        private readonly List<Material> _texMats = new List<Material>();
        private readonly Mesh _quad;
        private readonly Shader _shader, _alphaShader;

        private Btn _lastInside;
        private float _cooldownUntil;

        public bool HasShader => _shader != null;
        public Shader BaseShader => _shader;

        public UiKit()
        {
            _quad = BuildQuad();
            _shader = PickShader();
            _alphaShader = PickAlphaShader();
            if (_shader == null) Plugin.Log.LogWarning("[GTag Mod Checker] Fant ingen unlit-shader; menyen vises uten bakgrunner.");
        }

        // ------------------------------------------------------------------ bygging

        public Transform Child(Transform parent, string name, Vector3 localPos)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }

        public MeshRenderer Quad(Transform parent, string name, Vector3 localPos, float w, float h, Color c)
        {
            var t = Child(parent, name, localPos);
            t.localScale = new Vector3(w, h, 1f);
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = _quad;
            var mr = t.gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = Mat(c);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.enabled = _shader != null;
            return mr;
        }

        public MeshRenderer RRect(Transform parent, string name, Vector3 localPos, float w, float h, float r, Material m) =>
            MeshObj(parent, name, localPos, RoundedRect(w, h, r), m);

        /// <summary>
        /// Sigma-panel (sentrert i parent): myk sølvglød bak, sølvkant, nesten svart flate og en tynn mørk innerlinje.
        /// <paramref name="edge"/> = kanttykkelse i meter, <paramref name="r"/> = hjørneradius.
        /// </summary>
        public void Panel(Transform parent, float w, float h, float r = 0.012f, float edge = 0.0011f)
        {
            float g = edge * 4f;
            if (_alphaShader != null)
            {
                RRect(parent, "Glow", new Vector3(0, 0, 0.007f), w + g * 2.4f, h + g * 2.4f, r + g * 1.2f, AlphaMat(SilverGlow));
                RRect(parent, "GlowInner", new Vector3(0, 0, 0.0065f), w + g, h + g, r + g * 0.5f, AlphaMat(SilverGlowInner));
            }
            RRect(parent, "Border", new Vector3(0, 0, 0.005f), w, h, r, Mat(Accent));
            RRect(parent, "Bg", new Vector3(0, 0, 0.0045f), w - edge * 2f, h - edge * 2f, Mathf.Max(0.001f, r - edge), Mat(PanelBg));
            float inset = edge * 2.4f;
            RRect(parent, "InnerLine", new Vector3(0, 0, 0.004f), w - inset * 2f, h - inset * 2f, Mathf.Max(0.001f, r - inset), Mat(InnerLine));
            RRect(parent, "InnerBg", new Vector3(0, 0, 0.0035f), w - inset * 2f - edge * 1.2f, h - inset * 2f - edge * 1.2f, Mathf.Max(0.001f, r - inset - edge * 0.6f), Mat(PanelBg));
        }

        /// <summary>Pikseltekst. <paramref name="cell"/> = høyden på én tegnrute (meter).</summary>
        public PText Text(Transform parent, Vector3 localPos, float cell, TextAnchor anchor, TextAlignment align, Color c, string text, float maxWidth = 0f)
        {
            var t = Child(parent, "Text", localPos);
            var pt = t.gameObject.AddComponent<PText>();
            pt.Init(this, cell, anchor, align, c, text);
            if (maxWidth > 0f) pt.MaxWidth = maxWidth;
            return pt;
        }

        /// <summary>Sølvknapp med mørk pikseltekst og en mørkere "bevel"-stripe nederst.</summary>
        public Btn Button(Transform parent, string id, Vector3 localPos, float w, float h, string label, Action onPress, Style style = Style.Silver, float textCell = 0f, float radius = -1f)
        {
            var t = Child(parent, "Btn_" + id, localPos);
            var b = new Btn { Id = id, Go = t.gameObject, W = w, H = h, OnPress = onPress };
            if (style != Style.Link)
            {
                float r = radius >= 0f ? radius : Mathf.Min(0.002f, Mathf.Min(w, h) * 0.2f);
                b.Fill = RRect(t, "Fill", Vector3.zero, w, h, r, Mat(Silver));
                float bh = Mathf.Min(0.0009f, h * 0.06f);
                b.Bevel = Quad(t, "Bevel", new Vector3(0, -h / 2f + bh / 2f, -0.0004f), Mathf.Max(0.001f, w - r * 2f), bh, Bevel);
            }
            float cell = textCell > 0 ? textCell : Mathf.Min(0.0075f, h * 0.36f);
            b.Label = Text(t, new Vector3(0, 0, -0.0012f), cell, TextAnchor.MiddleCenter, TextAlignment.Center, Dark, label, w * 0.92f);
            SetStyle(b, style);
            Buttons.Add(b);
            return b;
        }

        /// <summary>Firkantet ikonknapp (sølv eller hvit) med mørkt pikselikon.</summary>
        public Btn IconButton(Transform parent, string id, Vector3 localPos, float w, float h, string icon, Action onPress, Style style = Style.Silver, float iconFrac = 0.62f)
        {
            var b = Button(parent, id, localPos, w, h, "", onPress, style, 0.001f);
            b.Icon = SwappableIcon(b.Go.transform, "Icon", new Vector3(0, 0, -0.0012f), Mathf.Min(w, h) * iconFrac, icon, Dark);
            SetStyle(b, style);
            return b;
        }

        /// <summary>Pikselikon (12 x 12) sentrert i localPos med bredde <paramref name="size"/>.</summary>
        public MeshRenderer Icon(Transform parent, string name, Vector3 localPos, float size, string icon, Color c)
        {
            if (!PixelFont.Icons.TryGetValue(icon, out var rows)) rows = PixelFont.Icons["dot"];
            var mesh = new Mesh { name = "Icon_" + icon };
            var v = new List<Vector3>(); var tris = new List<int>();
            float px = size / rows[0].Length;
            PixelFont.AddBitmap(v, tris, rows, '#', -size / 2f, size / 2f, px);
            mesh.SetVertices(v); mesh.SetTriangles(tris, 0); mesh.RecalculateBounds();
            _ownedMeshes.Add(mesh);
            return MeshObj(parent, name, localPos, mesh, Mat(c));
        }

        public void SetIcon(MeshRenderer icon, string name)
        {
            if (icon == null || !PixelFont.Icons.TryGetValue(name, out var rows)) return;
            var mf = icon.GetComponent<MeshFilter>();
            var mesh = mf.sharedMesh;
            if (!_iconSizes.TryGetValue(mf, out float size)) size = 0.01f; // størrelsen ble lagret ved bygging
            var v = new List<Vector3>(); var tris = new List<int>();
            PixelFont.AddBitmap(v, tris, rows, '#', -size / 2f, size / 2f, size / rows[0].Length);
            mesh.Clear(); mesh.SetVertices(v); mesh.SetTriangles(tris, 0); mesh.RecalculateBounds();
        }
        private readonly Dictionary<MeshFilter, float> _iconSizes = new Dictionary<MeshFilter, float>();
        private readonly List<Mesh> _ownedMeshes = new List<Mesh>();

        /// <summary>Ikon som kan byttes senere (lagrer størrelsen).</summary>
        public MeshRenderer SwappableIcon(Transform parent, string name, Vector3 localPos, float size, string icon, Color c)
        {
            var mr = Icon(parent, name, localPos, size, icon, c);
            _iconSizes[mr.GetComponent<MeshFilter>()] = size;
            return mr;
        }

        /// <summary>Flerfarget pikselbilde (f.eks. gorilla-portrettet). Én undermesh per symbol.</summary>
        public MeshRenderer PixelArt(Transform parent, string name, Vector3 localPos, float width, string[] rows, string symbols, Color[] colors)
        {
            var mesh = new Mesh { name = name };
            var v = new List<Vector3>();
            float px = width / rows[0].Length;
            float h = px * rows.Length;
            var all = new List<int>[symbols.Length];
            for (int i = 0; i < symbols.Length; i++)
            {
                all[i] = new List<int>();
                PixelFont.AddBitmap(v, all[i], rows, symbols[i], -width / 2f, h / 2f, px);
            }
            mesh.SetVertices(v);
            mesh.subMeshCount = symbols.Length;
            for (int i = 0; i < symbols.Length; i++) mesh.SetTriangles(all[i], i);
            mesh.RecalculateBounds();
            _ownedMeshes.Add(mesh);
            var mr = MeshObj(parent, name, localPos, mesh, Mat(colors[0]));
            var mats = new Material[colors.Length];
            for (int i = 0; i < colors.Length; i++) mats[i] = Mat(colors[i]);
            mr.sharedMaterials = mats;
            return mr;
        }

        public void SetStyle(Btn b, Style s)
        {
            b.Style = s;
            bool hot = b == Hovered;
            Color fill, text;
            switch (s)
            {
                case Style.Box:
                    if (b.Fill != null) b.Fill.sharedMaterial = Mat(hot ? Color.Lerp(BoxFill, Accent, 0.35f) : BoxFill);
                    if (b.Border != null) b.Border.sharedMaterial = Mat(hot ? Color.white : Accent);
                    if (b.Label != null) b.Label.color = b.TextColor ?? White;
                    return;
                case Style.Link:
                    if (b.Label != null) b.Label.color = hot ? White : (b.TextColor ?? Dim);
                    return;
                case Style.Active: fill = hot ? WhiteHover : White; text = Dark; break;
                case Style.Disabled: fill = DisabledFill; text = DisabledText; break;
                default: fill = hot ? SilverHover : Silver; text = Dark; break;
            }
            if (b.Fill != null) b.Fill.sharedMaterial = Mat(fill);
            if (b.Bevel != null) b.Bevel.sharedMaterial = Mat(s == Style.Disabled ? Hex("54575D") : Bevel);
            if (b.Label != null) b.Label.color = s == Style.Disabled ? text : (b.TextColor ?? text);
            if (b.Icon != null) b.Icon.sharedMaterial = Mat(s == Style.Disabled ? DisabledText : (b.TextColor ?? Dark));
        }

        // ------------------------------------------------------------------ berøring

        /// <summary>Debug-info om knappen nærmest fingeren.</summary>
        public struct HitInfo { public Btn Btn; public float Plane; public bool InRect; public float RectDist; }

        public Btn Hovered { get; private set; }
        public HitInfo Nearest { get; private set; }

        /// <summary>Blokkerer trykk en kort stund (f.eks. rett etter at menyen åpnes).</summary>
        public void BlockFor(float seconds) { _cooldownUntil = Time.unscaledTime + seconds; _lastInside = null; }

        /// <summary>
        /// Ren matte, ingen collidere: fingertuppen projiseres inn i hver knapps plan (verdensmål, tar hensyn til lossyScale).
        /// Treff = innenfor knappens rektangel utvidet med <paramref name="margin"/> på hver side OG innenfor ±<paramref name="depth"/>
        /// fra planet. Nærmeste knapp (planavstand, så avstand til rektangel, så senter) vinner. Fingeren må ut av sonen før
        /// samme knapp kan trykkes igjen. Hover-sonen er litt større og brukes til markering.
        /// </summary>
        public Btn Touch(Vector3 tip, float margin, float depth, float cooldown, bool haptics, float hapticAmp)
        {
            Btn press = null, hover = null;
            float bestPress = float.MaxValue, bestHover = float.MaxValue;
            var nearest = new HitInfo { Plane = float.MaxValue, RectDist = float.MaxValue };
            float nearestScore = float.MaxValue;
            float hoverMargin = margin + 0.008f, hoverDepth = depth + 0.035f;

            foreach (var b in Buttons)
            {
                if (b.Go == null || !b.Go.activeInHierarchy || b.OnPress == null) continue;
                Transform t = b.Go.transform;
                Vector3 ls = t.lossyScale;
                float hw = b.W * Mathf.Abs(ls.x) * 0.5f, hh = b.H * Mathf.Abs(ls.y) * 0.5f;
                if (hw <= 0f || hh <= 0f) continue;
                Vector3 d = tip - t.position;
                float u = Vector3.Dot(d, t.right), v = Vector3.Dot(d, t.up), n = Vector3.Dot(d, t.forward);
                float ox = Mathf.Max(0f, Mathf.Abs(u) - hw), oy = Mathf.Max(0f, Mathf.Abs(v) - hh);
                float rectDist = Mathf.Sqrt(ox * ox + oy * oy);
                float plane = Mathf.Abs(n);
                float center = 0.002f * (Mathf.Abs(u) / hw + Mathf.Abs(v) / hh) * 0.5f;
                float score = plane + 2f * rectDist + center;
                bool inRect = ox <= margin && oy <= margin;

                if (score < nearestScore) { nearestScore = score; nearest = new HitInfo { Btn = b, Plane = n, InRect = inRect, RectDist = rectDist }; }
                if (inRect && plane <= depth && score < bestPress) { bestPress = score; press = b; }
                if (ox <= hoverMargin && oy <= hoverMargin && plane <= hoverDepth && score < bestHover) { bestHover = score; hover = b; }
            }
            Nearest = nearest;
            if (press != null) hover = press;
            SetHovered(hover);

            Btn pressed = null;
            if (press != null && press != _lastInside && Time.unscaledTime >= _cooldownUntil)
            {
                _cooldownUntil = Time.unscaledTime + cooldown;
                Plugin.Log.LogInfo("[GTag Mod Checker] Meny: trykk " + press.Id);
                if (haptics) GtRefs.Vibrate(false, hapticAmp * 0.6f, 0.03f);
                pressed = press;
                try { press.OnPress?.Invoke(); }
                catch (Exception ex) { Plugin.Log.LogError("[GTag Mod Checker] Knapp " + press.Id + " feilet: " + ex.Message); }
            }
            _lastInside = press; // må ut av sonen før neste trykk
            return pressed;
        }

        public void ClearHover() => SetHovered(null);

        /// <summary>Trykk fra laseren (avtrekker). Samme cooldown og logg som fingertrykk.</summary>
        public bool PressExternal(Btn b, string source, bool haptics, float hapticAmp, float cooldown)
        {
            if (b == null || b.Go == null || !b.Go.activeInHierarchy || Time.unscaledTime < _cooldownUntil) return false;
            _cooldownUntil = Time.unscaledTime + cooldown;
            Plugin.Log.LogInfo("[GTag Mod Checker] Meny: trykk " + b.Id + " (" + source + ")");
            if (haptics) GtRefs.Vibrate(false, hapticAmp * 0.6f, 0.03f);
            try { b.OnPress?.Invoke(); }
            catch (Exception ex) { Plugin.Log.LogError("[GTag Mod Checker] Knapp " + b.Id + " feilet: " + ex.Message); }
            return true;
        }

        /// <summary>
        /// Laser mot knapper: skjæringspunktet mellom strålen og hver knapps plan; treff hvis innenfor rektangelet + margin.
        /// Nærmeste treff langs strålen vinner. Ren matte, ingen raycast/collidere.
        /// </summary>
        public Btn RayPick(Vector3 origin, Vector3 dir, float maxDist, float margin, out Vector3 hitPoint)
        {
            hitPoint = Vector3.zero;
            Btn best = null;
            float bestT = maxDist;
            foreach (var b in Buttons)
            {
                if (b.Go == null || !b.Go.activeInHierarchy || b.OnPress == null) continue;
                Transform t = b.Go.transform;
                Vector3 n = t.forward;
                float denom = Vector3.Dot(dir, n);
                if (Mathf.Abs(denom) < 1e-4f) continue;
                float dist = Vector3.Dot(t.position - origin, n) / denom;
                if (dist <= 0f || dist >= bestT) continue;
                Vector3 p = origin + dir * dist;
                Vector3 d = p - t.position;
                Vector3 ls = t.lossyScale;
                float hw = b.W * Mathf.Abs(ls.x) * 0.5f + margin, hh = b.H * Mathf.Abs(ls.y) * 0.5f + margin;
                if (Mathf.Abs(Vector3.Dot(d, t.right)) > hw || Mathf.Abs(Vector3.Dot(d, t.up)) > hh) continue;
                bestT = dist; best = b; hitPoint = p;
            }
            return best;
        }

        public void SetHovered(Btn b)
        {
            if (b == Hovered) return;
            var old = Hovered;
            Hovered = b;
            if (old != null) SetStyle(old, old.Style);
            if (b != null) SetStyle(b, b.Style);
        }

        // ------------------------------------------------------------------ materialer og ressurser

        public Material Mat(Color c)
        {
            string key = "o" + ColorKey(c);
            if (_mats.TryGetValue(key, out var m)) return m;
            if (_shader == null) return null;
            m = new Material(_shader) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            _mats[key] = m;
            return m;
        }

        /// <summary>Unlit-materiale som viser en tekstur (f.eks. kameraets RenderTexture).</summary>
        public Material TexMat(Texture tex)
        {
            if (_shader == null) return null;
            var m = new Material(_shader) { color = Color.white };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            m.mainTexture = tex;
            _texMats.Add(m);
            return m;
        }

        public Material AlphaMat(Color c)
        {
            string key = "a" + ColorKey(c);
            if (_mats.TryGetValue(key, out var m)) return m;
            var sh = _alphaShader ?? _shader;
            if (sh == null) return null;
            m = new Material(sh) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (sh.name == "Universal Render Pipeline/Unlit") MakeUrpTransparent(m);
            m.renderQueue = 3000;
            if (!_alphaLogged) { _alphaLogged = true; Plugin.Log.LogInfo("[GTag Mod Checker] Gjennomsiktig shader: " + sh.name + (sh.name.StartsWith("Universal") ? " (Surface=Transparent)" : "")); }
            _mats[key] = m;
            return m;
        }

        private static string ColorKey(Color c) =>
            Mathf.RoundToInt(c.r * 255f) + "," + Mathf.RoundToInt(c.g * 255f) + "," + Mathf.RoundToInt(c.b * 255f) + "," + Mathf.RoundToInt(c.a * 255f);

        private bool _alphaLogged;

        /// <summary>Gjør et URP/Unlit-materiale gjennomsiktig (alfa-blanding, ingen dybdeskriving).</summary>
        private static void MakeUrpTransparent(Material m)
        {
            try
            {
                if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
                if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
                if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", 5f);  // SrcAlpha
                if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", 10f); // OneMinusSrcAlpha
                if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
            catch (Exception ex) { Plugin.Log.LogWarning("[GTag Mod Checker] Kunne ikke gjøre URP-materiale gjennomsiktig: " + ex.Message); }
        }

        public void Destroy()
        {
            foreach (var m in _mats.Values) if (m != null) UnityEngine.Object.Destroy(m);
            _mats.Clear();
            foreach (var m in _texMats) if (m != null) UnityEngine.Object.Destroy(m);
            _texMats.Clear();
            foreach (var m in _meshes.Values) if (m != null) UnityEngine.Object.Destroy(m);
            _meshes.Clear();
            foreach (var m in _ownedMeshes) if (m != null) UnityEngine.Object.Destroy(m);
            _ownedMeshes.Clear();
        }

        /// <summary>Valgfri overstyring fra config (Menu.TransparentShader). Tom/"auto" = automatisk.</summary>
        public static string PreferredAlphaShader = "auto";

        /// <summary>Gjennomsiktig shader: URP Unlit (satt til Transparent) når URP er aktiv, ellers Sprites/Default, så Unlit/Transparent.</summary>
        private static Shader PickAlphaShader()
        {
            if (!string.IsNullOrEmpty(PreferredAlphaShader) && !PreferredAlphaShader.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                var forced = Shader.Find(PreferredAlphaShader);
                if (forced != null) return forced;
                Plugin.Log.LogWarning("[GTag Mod Checker] TransparentShader '" + PreferredAlphaShader + "' finnes ikke, bruker automatisk valg.");
            }
            bool urp = false;
            try { urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null; } catch { }
            string[] order = urp
                ? new[] { "Universal Render Pipeline/Unlit", "Sprites/Default", "Unlit/Transparent" }
                : new[] { "Sprites/Default", "Unlit/Transparent", "Universal Render Pipeline/Unlit" };
            foreach (var n in order) { var sh = Shader.Find(n); if (sh != null) return sh; }
            return null;
        }

        private static Shader PickShader()
        {
            bool urp = false;
            try { urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null; } catch { }
            string[] order = urp
                ? new[] { "Universal Render Pipeline/Unlit", "Unlit/Color", "Sprites/Default", "GorillaTag/UberShader" }
                : new[] { "Unlit/Color", "Universal Render Pipeline/Unlit", "Sprites/Default", "GorillaTag/UberShader" };
            foreach (var n in order)
            {
                var s = Shader.Find(n);
                if (s != null) { Plugin.Log.LogInfo("[GTag Mod Checker] Meny-shader: " + n); return s; }
            }
            return null;
        }

        /// <summary>Innebygd Unity-font (brukes av navnelappene over hodene).</summary>
        internal static Font LoadFont()
        {
            Font f = null;
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (f == null) try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
            if (f == null) try { f = Font.CreateDynamicFontFromOSFont("Arial", 64); } catch { }
            if (f == null) Plugin.Log.LogWarning("[GTag Mod Checker] Fant ingen innebygd font for navnelappene.");
            return f;
        }

        // ------------------------------------------------------------------ disc-byggeklosser

        private readonly Dictionary<string, Mesh> _meshes = new Dictionary<string, Mesh>();

        public MeshRenderer MeshObj(Transform parent, string name, Vector3 localPos, Mesh mesh, Material mat)
        {
            var t = Child(parent, name, localPos);
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = t.gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.enabled = mat != null;
            return mr;
        }

        /// <summary>Boks-knapp for disc-stilen: mørk avrundet boks, tynn sølvkant, liten sølv caption over og hvit verdi under.</summary>
        public Btn Box(Transform parent, string id, Vector3 localPos, float w, float h, string caption, string value, Action onPress, float valueSize = 0.014f)
        {
            var t = Child(parent, "Box_" + id, localPos);
            var b = new Btn { Id = id, Go = t.gameObject, W = w, H = h, OnPress = onPress };
            float r = Mathf.Min(w, h) * 0.16f;
            float edge = Mathf.Max(0.0012f, h * 0.035f);
            b.Border = MeshObj(t, "Border", new Vector3(0, 0, 0.001f), RoundedRect(w + edge * 2f, h + edge * 2f, r + edge), Mat(Accent));
            b.Fill = MeshObj(t, "Fill", Vector3.zero, RoundedRect(w, h, r), Mat(BoxFill));
            float capSize = valueSize * 0.62f;
            bool hasCap = !string.IsNullOrEmpty(caption);
            b.Caption = Text(t, new Vector3(0, h * 0.22f, -0.002f), capSize, TextAnchor.MiddleCenter, TextAlignment.Center, Silver, caption, w * 0.9f);
            b.Label = Text(t, new Vector3(0, hasCap ? -h * 0.16f : 0f, -0.002f), valueSize, TextAnchor.MiddleCenter, TextAlignment.Center, White, value, w * 0.9f);
            SetStyle(b, Style.Box);
            Buttons.Add(b);
            return b;
        }

        /// <summary>Gjennomsiktig glødende ring: bred svak glød, gjennomsiktig fyll og lyse tynne kanter innerst og ytterst.</summary>
        public void Ring(Transform parent, float outerR, float thickness, Color accent)
        {
            float innerR = outerR - thickness;
            bool silver = accent == Silver;
            MeshObj(parent, "RingGlow", new Vector3(0, 0, 0.012f), Annulus(Mathf.Max(0.001f, innerR - 0.025f), outerR + 0.035f, 96), AlphaMat(new Color(accent.r, accent.g, accent.b, silver ? 0.08f : 0.10f)));
            MeshObj(parent, "RingFill", new Vector3(0, 0, 0.010f), Annulus(innerR, outerR, 96), AlphaMat(new Color(accent.r, accent.g, accent.b, silver ? 0.30f : 0.38f)));
            Color edge = silver ? White : Color.Lerp(accent, Color.white, 0.35f);
            MeshObj(parent, "RingEdgeOuter", new Vector3(0, 0, 0.008f), Annulus(outerR - 0.004f, outerR, 96), AlphaMat(new Color(edge.r, edge.g, edge.b, 0.95f)));
            MeshObj(parent, "RingEdgeInner", new Vector3(0, 0, 0.008f), Annulus(innerR, innerR + 0.004f, 96), AlphaMat(new Color(edge.r, edge.g, edge.b, 0.90f)));
        }

        /// <summary>Flat ring (annulus) i XY-planet, synlig fra begge sider.</summary>
        public Mesh Annulus(float innerR, float outerR, int segments)
        {
            string key = $"ann{innerR:0.0000}_{outerR:0.0000}_{segments}";
            if (_meshes.TryGetValue(key, out var cached)) return cached;
            var v = new Vector3[(segments + 1) * 2];
            var col = new Color[v.Length];
            for (int i = 0; i <= segments; i++)
            {
                float a = 2f * Mathf.PI * i / segments;
                float cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                v[i * 2] = new Vector3(cs * innerR, sn * innerR, 0);
                v[i * 2 + 1] = new Vector3(cs * outerR, sn * outerR, 0);
                col[i * 2] = Color.white; col[i * 2 + 1] = Color.white;
            }
            var tris = new List<int>();
            for (int i = 0; i < segments; i++)
            {
                int a0 = i * 2, a1 = a0 + 1, b0 = a0 + 2, b1 = a0 + 3;
                tris.AddRange(new[] { a0, a1, b0, b0, a1, b1 });   // én side
                tris.AddRange(new[] { a0, b0, a1, b0, b1, a1 });   // andre side
            }
            var m = new Mesh { vertices = v, colors = col, triangles = tris.ToArray() };
            m.RecalculateNormals();
            _meshes[key] = m;
            return m;
        }

        /// <summary>Avrundet rektangel (w x h, hjørneradius r), sentrert, synlig fra begge sider.</summary>
        public Mesh RoundedRect(float w, float h, float r)
        {
            string key = $"rr{w:0.00000}_{h:0.00000}_{r:0.00000}";
            if (_meshes.TryGetValue(key, out var cached)) return cached;
            r = Mathf.Max(0f, Mathf.Min(r, Mathf.Min(w, h) * 0.5f));
            const int seg = 5;
            var pts = new List<Vector3>();
            float hx = w / 2 - r, hy = h / 2 - r;
            Vector3[] centers = { new Vector3(hx, hy, 0), new Vector3(-hx, hy, 0), new Vector3(-hx, -hy, 0), new Vector3(hx, -hy, 0) };
            for (int c = 0; c < 4; c++)
                for (int i = 0; i <= seg; i++)
                {
                    float a = Mathf.PI * 0.5f * (c + (float)i / seg);
                    pts.Add(centers[c] + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0));
                }
            var v = new List<Vector3> { Vector3.zero };
            v.AddRange(pts);
            var tris = new List<int>();
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                int a = 1 + i, b = 1 + (i + 1) % n;
                tris.AddRange(new[] { 0, b, a, 0, a, b });
            }
            var m = new Mesh { vertices = v.ToArray(), triangles = tris.ToArray() };
            m.RecalculateNormals();
            _meshes[key] = m;
            return m;
        }

        private Mesh _sphere;

        /// <summary>Kule (diameter 1) til fingerprikken. Lages som mesh, så det blir ingen collider.</summary>
        public MeshRenderer Sphere(Transform parent, string name, float diameter, Material mat)
        {
            if (_sphere == null) _sphere = BuildSphere(14, 10);
            var t = Child(parent, name, Vector3.zero);
            t.localScale = new Vector3(diameter, diameter, diameter);
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = _sphere;
            var mr = t.gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.enabled = mat != null;
            return mr;
        }

        private static Mesh BuildSphere(int lon, int lat)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int y = 0; y <= lat; y++)
            {
                float a = Mathf.PI * y / lat;
                for (int x = 0; x <= lon; x++)
                {
                    float b = 2f * Mathf.PI * x / lon;
                    verts.Add(new Vector3(Mathf.Sin(a) * Mathf.Cos(b), Mathf.Cos(a), Mathf.Sin(a) * Mathf.Sin(b)) * 0.5f);
                }
            }
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int i0 = y * (lon + 1) + x, i1 = i0 + lon + 1;
                    tris.Add(i0); tris.Add(i0 + 1); tris.Add(i1);
                    tris.Add(i0 + 1); tris.Add(i1 + 1); tris.Add(i1);
                }
            var m = new Mesh { vertices = verts.ToArray(), triangles = tris.ToArray() };
            m.RecalculateNormals();
            return m;
        }

        private static Mesh BuildQuad()
        {
            var m = new Mesh();
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 }; // synlig forfra (sett langs +Z)
            m.RecalculateNormals();
            return m;
        }

        public static Color Hex(string hex)
        {
            int v = Convert.ToInt32(hex, 16);
            return new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);
        }

        public static string VerdictHex(FanGame.ModChecker.Verdict v) =>
            v == FanGame.ModChecker.Verdict.CHEATING ? RedHex : v == FanGame.ModChecker.Verdict.SUS ? YellowHex : GreenHex;

        public static string Trunc(string s, int n) => s == null ? "" : (s.Length <= n ? s : s.Substring(0, n - 1) + "…");
    }
}
