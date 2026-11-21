using System;
using System.Collections.Generic;
using UnityEngine;

namespace GTagModChecker
{
    /// <summary>
    /// Pikseltekst (erstatter TextMesh i menyen). Bygger en mesh av små firkanter fra SebPixel-fonten.
    /// Støtter &lt;color=#RRGGBB&gt;...&lt;/color&gt; (én undermesh per farge); &lt;b&gt;, &lt;i&gt; og &lt;size&gt; ignoreres.
    /// Cell = høyden på en 8x8-tegnrute i meter. MaxWidth &gt; 0 krymper teksten så den får plass.
    /// </summary>
    internal sealed class PText : MonoBehaviour
    {
        internal UiKit Kit;
        private string _text = "";
        private Color _color = Color.white;
        private TextAnchor _anchor = TextAnchor.MiddleCenter;
        private TextAlignment _alignment = TextAlignment.Center;
        private float _cell = 0.01f, _maxWidth, _lineSpacing = 1.3f;
        private MeshFilter _mf;
        private MeshRenderer _mr;
        private Mesh _mesh;
        private readonly List<Color> _colors = new List<Color>();

        public string text { get => _text; set { value = value ?? ""; if (value == _text) return; _text = value; Rebuild(); } }
        public Color color { get => _color; set { if (value == _color) return; _color = value; ApplyMaterials(); } }
        public TextAnchor anchor { get => _anchor; set { if (value == _anchor) return; _anchor = value; Rebuild(); } }
        public TextAlignment alignment { get => _alignment; set { if (value == _alignment) return; _alignment = value; Rebuild(); } }
        public float Cell { get => _cell; set { if (Mathf.Approximately(value, _cell)) return; _cell = value; Rebuild(); } }
        public float MaxWidth { get => _maxWidth; set { if (Mathf.Approximately(value, _maxWidth)) return; _maxWidth = value; Rebuild(); } }
        public float LineSpacing { get => _lineSpacing; set { if (Mathf.Approximately(value, _lineSpacing)) return; _lineSpacing = value; Rebuild(); } }

        internal void Init(UiKit kit, float cell, TextAnchor anchor, TextAlignment align, Color c, string t)
        {
            Kit = kit;
            _mf = gameObject.AddComponent<MeshFilter>();
            _mr = gameObject.AddComponent<MeshRenderer>();
            _mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _mr.receiveShadows = false;
            _mesh = new Mesh { name = "PText" };
            _mesh.MarkDynamic();
            _mf.sharedMesh = _mesh;
            _cell = cell; _anchor = anchor; _alignment = align; _color = c; _text = t ?? "";
            Rebuild();
        }

        private struct Ch { public char C; public int Col; }

        private void Rebuild()
        {
            if (_mesh == null) return;
            // ---- tolk rik tekst til linjer med (tegn, fargeindeks) ----
            _colors.Clear();
            _colors.Add(_color); // indeks 0 = grunnfarge
            var lines = new List<List<Ch>> { new List<Ch>() };
            var stack = new Stack<int>();
            int col = 0;
            string s = _text;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '<')
                {
                    int end = s.IndexOf('>', i + 1);
                    if (end > i)
                    {
                        string tag = s.Substring(i + 1, end - i - 1).Trim();
                        string lower = tag.ToLowerInvariant();
                        if (lower.StartsWith("color="))
                        {
                            string val = tag.Substring(6).Trim('"', '\'', ' ');
                            if (ColorUtility.TryParseHtmlString(val, out Color cc))
                            {
                                stack.Push(col);
                                col = IndexOf(cc);
                                i = end; continue;
                            }
                        }
                        else if (lower == "/color") { col = stack.Count > 0 ? stack.Pop() : 0; i = end; continue; }
                        else if (lower == "b" || lower == "/b" || lower == "i" || lower == "/i" || lower == "/size" || lower.StartsWith("size=")) { i = end; continue; }
                    }
                }
                if (c == '\r') continue;
                if (c == '\n') { lines.Add(new List<Ch>()); continue; }
                lines[lines.Count - 1].Add(new Ch { C = c, Col = col });
            }

            // ---- mål og skaler ----
            int maxChars = 0;
            foreach (var l in lines) maxChars = Math.Max(maxChars, l.Count);
            float px = _cell / PixelFont.Cell;
            if (_maxWidth > 0f && maxChars > 0 && maxChars * PixelFont.Cell * px > _maxWidth) px = _maxWidth / (maxChars * PixelFont.Cell);
            float adv = PixelFont.Cell * px;              // tegnbredde (monospace)
            float lineAdv = PixelFont.Cell * _lineSpacing * px;
            float capH = 7f * px;                          // rad 0-6 = store bokstaver, rad 7 = descender
            float blockW = maxChars * adv - px;            // siste kolonne er alltid tom
            float blockH = (lines.Count - 1) * lineAdv + capH;

            float left, top;
            switch (_anchor)
            {
                case TextAnchor.UpperLeft: case TextAnchor.MiddleLeft: case TextAnchor.LowerLeft: left = 0f; break;
                case TextAnchor.UpperRight: case TextAnchor.MiddleRight: case TextAnchor.LowerRight: left = -blockW; break;
                default: left = -blockW / 2f; break;
            }
            switch (_anchor)
            {
                case TextAnchor.UpperLeft: case TextAnchor.UpperCenter: case TextAnchor.UpperRight: top = 0f; break;
                case TextAnchor.LowerLeft: case TextAnchor.LowerCenter: case TextAnchor.LowerRight: top = blockH; break;
                default: top = blockH / 2f; break;
            }

            // ---- bygg mesh ----
            var verts = new List<Vector3>(256);
            var tris = new List<int>[_colors.Count];
            for (int k = 0; k < tris.Length; k++) tris[k] = new List<int>();
            for (int li = 0; li < lines.Count; li++)
            {
                var line = lines[li];
                float lw = line.Count * adv - px;
                float x = left;
                if (_alignment == TextAlignment.Center) x = left + (blockW - lw) / 2f;
                else if (_alignment == TextAlignment.Right) x = left + (blockW - lw);
                float y = top - li * lineAdv;
                foreach (var ch in line)
                {
                    if (ch.C != ' ') PixelFont.AddGlyph(verts, tris[ch.Col], PixelFont.Glyph(ch.C), x, y, px);
                    x += adv;
                }
            }
            _mesh.Clear();
            _mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            _mesh.SetVertices(verts);
            _mesh.subMeshCount = _colors.Count;
            for (int k = 0; k < _colors.Count; k++) _mesh.SetTriangles(tris[k], k, false);
            _mesh.RecalculateBounds();
            Width = Mathf.Max(0f, blockW);
            ApplyMaterials();
        }

        /// <summary>Bredden på teksten (meter) etter siste bygging.</summary>
        public float Width { get; private set; }

        private int IndexOf(Color c)
        {
            for (int i = 1; i < _colors.Count; i++) if (_colors[i] == c) return i;
            _colors.Add(c);
            return _colors.Count - 1;
        }

        private void ApplyMaterials()
        {
            if (_mr == null || Kit == null) return;
            if (_colors.Count == 0) _colors.Add(_color);
            _colors[0] = _color;
            var mats = new Material[_colors.Count];
            bool ok = true;
            for (int i = 0; i < mats.Length; i++) { mats[i] = Kit.Mat(_colors[i]); if (mats[i] == null) ok = false; }
            _mr.sharedMaterials = mats;
            _mr.enabled = ok;
        }

        private void OnDestroy() { if (_mesh != null) Destroy(_mesh); }
    }
}
