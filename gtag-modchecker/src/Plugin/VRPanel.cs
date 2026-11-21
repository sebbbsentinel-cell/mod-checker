using System.Collections.Generic;
using System.Linq;
using System.Text;
using FanGame.ModChecker;
using UnityEngine;

namespace GTagModChecker
{
    /// <summary>
    /// World-space resultatpanel foran Camera.main. Bruker bare innebygd TextMesh og et egenlaget quad-mesh
    /// (ingen collider, så panelet kan ikke påvirke gameplay).
    /// </summary>
    internal sealed class VRPanel : MonoBehaviour
    {
        public float distance = 0.8f;
        public float verticalOffset = -0.05f;
        public float lineHeight = 0.022f;
        public bool followHead;

        private TextMesh _text;
        private Transform _bg;
        private bool _built;

        public bool IsVisible => gameObject.activeSelf;

        public static VRPanel Create()
        {
            var go = new GameObject("GTagModCheckerPanel");
            go.hideFlags = HideFlags.HideAndDontSave;
            Object.DontDestroyOnLoad(go);
            var p = go.AddComponent<VRPanel>();
            p.Build();
            go.SetActive(false);
            return p;
        }

        private void Build()
        {
            if (_built) return;
            _built = true;

            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                var bg = new GameObject("Background");
                bg.hideFlags = HideFlags.HideAndDontSave;
                bg.transform.SetParent(transform, false);
                bg.transform.localPosition = new Vector3(0f, 0f, 0.01f);
                bg.AddComponent<MeshFilter>().sharedMesh = BuildQuad();
                var mat = new Material(shader) { color = new Color(0.05f, 0.05f, 0.08f, 0.92f) };
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", mat.color);
                bg.AddComponent<MeshRenderer>().sharedMaterial = mat;
                _bg = bg.transform;
            }

            var textGo = new GameObject("Text");
            textGo.hideFlags = HideFlags.HideAndDontSave;
            textGo.transform.SetParent(transform, false);
            _text = textGo.AddComponent<TextMesh>();
            var mr = textGo.GetComponent<MeshRenderer>();
            if (mr == null) mr = textGo.AddComponent<MeshRenderer>();
            var font = LoadFont();
            if (font != null) { _text.font = font; mr.sharedMaterial = font.material; }
            else Plugin.Log.LogWarning("[GTag Mod Checker] Fant ingen innebygd font for panelet.");
            _text.fontSize = 64;
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Left;
            _text.richText = true;
            _text.color = Color.white;
            ApplySize();
        }

        private void ApplySize()
        {
            if (_text != null) _text.characterSize = lineHeight * 10f / _text.fontSize; // fontSize 10 * characterSize 1 ≈ 1 m
        }

        private static Mesh BuildQuad()
        {
            var m = new Mesh();
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 }; // synlig forfra (sett langs +Z)
            m.RecalculateNormals();
            return m;
        }

        private static Font LoadFont()
        {
            Font f = null;
            try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (f == null) try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
            if (f == null) try { f = Font.CreateDynamicFontFromOSFont("Arial", 64); } catch { }
            return f;
        }

        public void Show(string richText, bool reposition)
        {
            Build();
            ApplySize();
            _text.text = richText;
            ResizeBackground(richText);
            if (reposition) PlaceInFrontOfCamera();
            gameObject.SetActive(true);
        }

        public void Hide() => gameObject.SetActive(false);

        private void LateUpdate()
        {
            if (followHead) PlaceInFrontOfCamera();
        }

        private void ResizeBackground(string text)
        {
            if (_bg == null) return;
            var lines = System.Text.RegularExpressions.Regex.Replace(text, "<.*?>", "").Split('\n');
            int maxChars = lines.Max(l => l.Length);
            _bg.localScale = new Vector3(maxChars * lineHeight * 0.55f + 0.06f, lines.Length * lineHeight * 1.1f + 0.05f, 1f);
        }

        public void PlaceInFrontOfCamera()
        {
            Camera cam = Camera.main;
            if (cam == null) cam = Object.FindObjectOfType<Camera>();
            if (cam == null) { transform.position = new Vector3(0f, 1.5f, distance); transform.rotation = Quaternion.identity; return; }
            Transform c = cam.transform;
            Vector3 fwd = c.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = c.forward;
            fwd.Normalize();
            transform.position = c.position + fwd * distance + Vector3.up * verticalOffset;
            transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        }

        // ------------------------------------------------------------ tekst

        public static string ColorFor(Verdict v) => v == Verdict.CHEATING ? "#FF4040" : v == Verdict.SUS ? "#FFC830" : "#40FF60";

        /// <summary>Lager panelteksten. Viser alltid noe, også når man ikke er i et rom.</summary>
        public static string BuildText(List<PlayerReport> reports, bool inRoom, TriggerMode mode, string movementStatus, int maxPlayers = 10)
        {
            var sb = new StringBuilder();
            sb.Append("<b>GTAG MOD CHECKER</b>   <size=40>(").Append(mode == TriggerMode.DoublePress ? "X X" : "X").Append(" = lukk)</size>\n");
            reports = reports ?? new List<PlayerReport>();

            if (!inRoom || reports.Count == 0)
            {
                sb.Append("<color=#40FF60>NOT DETECTED</color> - 0 players checked\n");
                sb.Append(inRoom ? "No players found" : "No players found (not in a room)");
                return sb.ToString();
            }

            int flagged = reports.Count(r => r.Verdict != Verdict.CLEAN);
            sb.Append(reports.Count).Append(" players checked, ").Append(flagged).Append(" flagged\n");
            foreach (var r in reports.Take(maxPlayers))
            {
                string name = r.Info?.DisplayName ?? r.PlayerId;
                sb.Append('\n').Append(name).Append("  <color=").Append(ColorFor(r.Verdict)).Append('>').Append(r.StatusText).Append("</color>");
                if (!string.IsNullOrEmpty(r.Info?.Platform)) sb.Append("  <size=40>").Append(PhotonFeed.Sanitize(r.Info.Platform)).Append("</size>");
                sb.Append('\n');
                sb.Append("  ALLOWED MODS: ").Append(r.AllowedModCount);
                if (r.AllowedModCount > 0) sb.Append("  <color=#A0C8FF>").Append(string.Join(", ", r.AllowedMods.Take(3))).Append("</color>");
                sb.Append('\n');
                sb.Append("  CHEATS: ").Append(r.CheatCount);
                if (r.CheatCount > 0) sb.Append("  <color=#FF8080>").Append(string.Join(", ", r.Cheats.Take(4))).Append(r.CheatCount > 4 ? " ..." : "").Append("</color>");
                if (r.UnsureCount > 0) sb.Append("   UNSURE: ").Append(r.UnsureCount);
                sb.Append('\n');
            }
            if (reports.Count > maxPlayers) sb.Append("\n+").Append(reports.Count - maxPlayers).Append(" more\n");
            sb.Append("\n<size=36>Movement: ").Append(movementStatus).Append("</size>");
            return sb.ToString();
        }
    }
}
