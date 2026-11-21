using System.Collections.Generic;
using System.Linq;
using System.Text;
using FanGame.ModChecker;
using UnityEngine;

namespace GTagModChecker
{
    /// <summary>
    /// Lokale navnelapper over hodet til spillerne (bare du ser dem). TextMesh som alltid vender mot kameraet.
    /// Linjer (ovenfra): plattform, fps/hz, navn, status. Styres av NAMETAGS-fanen og lagres i BepInEx-config.
    /// </summary>
    internal sealed class Nametags : MonoBehaviour
    {
        private sealed class Tag { public GameObject Go; public TextMesh Text; public RigInfo Rig; }

        private readonly Dictionary<int, Tag> _tags = new Dictionary<int, Tag>();
        private List<RigInfo> _rigs = new List<RigInfo>();
        private Dictionary<string, PlayerReport> _reports = new Dictionary<string, PlayerReport>();
        private Font _font;
        private float _nextText;

        private PluginSettings S => Plugin.Settings;

        public static Nametags Create(Font font)
        {
            var go = new GameObject("GTagModCheckerNametags") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            var n = go.AddComponent<Nametags>();
            n._font = font;
            return n;
        }

        public void SetData(List<RigInfo> rigs, List<PlayerReport> reports)
        {
            _rigs = rigs ?? new List<RigInfo>();
            _reports = new Dictionary<string, PlayerReport>();
            if (reports != null) foreach (var r in reports) if (r?.PlayerId != null) _reports[r.PlayerId] = r;
            _nextText = 0f; // oppdater teksten neste ramme
        }

        /// <summary>For testing: teksten over en spiller, eller null.</summary>
        internal string TextFor(int actor) => _tags.TryGetValue(actor, out var t) && t.Go != null && t.Go.activeSelf ? t.Text.text : null;
        internal int VisibleCount => _tags.Values.Count(t => t.Go != null && t.Go.activeSelf);

        private void LateUpdate()
        {
            try
            {
                bool on = S.Nametags.Value && PhotonFeed.InRoom;
                var wanted = new HashSet<int>();
                if (on)
                {
                    foreach (var r in _rigs)
                    {
                        if (r == null || !r.Alive) continue;
                        if (r.IsLocal && !S.SelfTags.Value) continue;
                        wanted.Add(r.Actor);
                        if (!_tags.TryGetValue(r.Actor, out var tag) || tag.Go == null) { tag = Make(r.Actor); _tags[r.Actor] = tag; }
                        tag.Rig = r;
                    }
                }
                foreach (var kv in _tags.ToList())
                {
                    if (!wanted.Contains(kv.Key))
                    {
                        if (kv.Value.Go != null) Destroy(kv.Value.Go);
                        _tags.Remove(kv.Key);
                    }
                }
                if (_tags.Count == 0) return;

                bool refreshText = Time.unscaledTime >= _nextText;
                if (refreshText) _nextText = Time.unscaledTime + 0.5f;

                Camera cam = Camera.main;
                Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
                foreach (var tag in _tags.Values)
                {
                    var r = tag.Rig;
                    float sc = r.Scale;
                    tag.Go.transform.position = r.HeadPos + Vector3.up * (S.TagHeight.Value * sc);
                    Vector3 look = tag.Go.transform.position - camPos;
                    if (look.sqrMagnitude > 0.0001f) tag.Go.transform.rotation = Quaternion.LookRotation(look, Vector3.up);
                    float size = Mathf.Max(0.1f, S.TagSize.Value) * sc;
                    tag.Go.transform.localScale = new Vector3(size, size, size);
                    if (refreshText)
                    {
                        tag.Text.fontStyle = S.TagFont.Value == NametagFont.Bold ? FontStyle.Bold : FontStyle.Normal;
                        tag.Text.text = Compose(r);
                        tag.Go.SetActive(tag.Text.text.Length > 0);
                    }
                }
            }
            catch (System.Exception ex) { Plugin.Log.LogWarning("[GTag Mod Checker] Navnelapp-feil: " + ex.Message); }
        }

        private string Compose(RigInfo r)
        {
            _reports.TryGetValue(r.PlayerId, out var rep);
            var sb = new StringBuilder();
            if (S.TagPlatforms.Value && !r.IsLocal)
            {
                string p = PlayerText.Platform(rep, r);
                if (p != null) sb.Append("<color=").Append(UiKit.PinkHex).Append('>').Append(p).Append("</color>\n");
            }
            if (S.TagFpsHz.Value)
            {
                int? fps = PlayerText.Fps(rep, r);
                if (fps != null) sb.Append(fps.Value).Append("hz\n");
            }
            string name = r.IsLocal ? (PhotonFeed.LocalName ?? "YOU") : (rep != null ? PlayerText.Name(rep, 16) : null);
            if (name != null) sb.Append(name).Append('\n');
            if (S.TagVerdict.Value && rep != null)
                sb.Append("<color=").Append(UiKit.VerdictHex(rep.Verdict)).Append('>').Append(rep.StatusText).Append("</color>\n");
            return sb.ToString().TrimEnd('\n');
        }

        private Tag Make(int actor)
        {
            var go = new GameObject("Tag_" + actor) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(transform, false);
            var tm = go.AddComponent<TextMesh>();
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) mr = go.AddComponent<MeshRenderer>();
            if (_font != null) { tm.font = _font; mr.sharedMaterial = _font.material; }
            tm.fontSize = 64;
            tm.characterSize = 0.045f * 10f / tm.fontSize; // ca. 4.5 cm linjehøyde
            tm.anchor = TextAnchor.LowerCenter;
            tm.alignment = TextAlignment.Center;
            tm.richText = true;
            tm.color = Color.white;
            return new Tag { Go = go, Text = tm };
        }

        public void Clear()
        {
            foreach (var t in _tags.Values) if (t.Go != null) Destroy(t.Go);
            _tags.Clear();
        }

        private void OnDestroy() => Clear();
    }
}
