using System.Collections.Generic;
using UnityEngine;

namespace GTagModChecker
{
    /// <summary>
    /// LOKAL stemmevolum per spiller: endrer bare avspillingen av stemmen på DIN maskin (AudioSource.volume / mute).
    /// Sender ingenting og påvirker ikke hva andre hører. Settes på nytt hver ramme fordi spillet kan nullstille det.
    /// </summary>
    internal sealed class VoiceControl
    {
        private sealed class Ovr { public float Volume = 1f; public bool Mute; }
        private readonly Dictionary<int, Ovr> _ovr = new Dictionary<int, Ovr>();

        public bool HasOverride(int actor) => _ovr.ContainsKey(actor);

        /// <summary>Volum 0-1 for visning, eller null hvis stemmen ikke ble funnet.</summary>
        public float? Volume(RigInfo rig)
        {
            if (rig == null) return null;
            if (_ovr.TryGetValue(rig.Actor, out var o)) return o.Volume;
            if (rig.Voice == null) return null;
            return rig.Voice.volume;
        }

        public bool Muted(RigInfo rig)
        {
            if (rig == null) return false;
            if (_ovr.TryGetValue(rig.Actor, out var o)) return o.Mute;
            return rig.Voice != null && rig.Voice.mute;
        }

        /// <summary>Endrer volumet med steg (f.eks. -0.1). Returnerer false hvis stemmen ikke finnes.</summary>
        public bool Step(RigInfo rig, float delta)
        {
            if (rig == null || rig.Voice == null) return false;
            var o = Get(rig);
            o.Volume = Mathf.Clamp01(Mathf.Round((o.Volume + delta) * 10f) / 10f);
            Apply(rig);
            return true;
        }

        public bool ToggleMute(RigInfo rig)
        {
            if (rig == null || rig.Voice == null) return false;
            var o = Get(rig);
            o.Mute = !o.Mute;
            Apply(rig);
            return true;
        }

        private Ovr Get(RigInfo rig)
        {
            if (!_ovr.TryGetValue(rig.Actor, out var o))
            {
                o = new Ovr { Volume = rig.Voice != null ? rig.Voice.volume : 1f, Mute = rig.Voice != null && rig.Voice.mute };
                _ovr[rig.Actor] = o;
            }
            return o;
        }

        private void Apply(RigInfo rig)
        {
            if (rig?.Voice == null || !_ovr.TryGetValue(rig.Actor, out var o)) return;
            if (!Mathf.Approximately(rig.Voice.volume, o.Volume)) rig.Voice.volume = o.Volume;
            if (rig.Voice.mute != o.Mute) rig.Voice.mute = o.Mute;
        }

        /// <summary>Kalles hver ramme: holder lokale innstillinger på plass, glemmer spillere som har gått.</summary>
        public void ApplyAll(List<RigInfo> rigs)
        {
            if (_ovr.Count == 0 || rigs == null) return;
            var present = new HashSet<int>();
            foreach (var r in rigs)
            {
                if (r == null || r.IsLocal) continue;
                present.Add(r.Actor);
                try { Apply(r); } catch { }
            }
            if (rigs.Count > 0 && PhotonFeed.InRoom)
            {
                var gone = new List<int>();
                foreach (var k in _ovr.Keys) if (!present.Contains(k)) gone.Add(k);
                foreach (var k in gone) _ovr.Remove(k);
            }
            else if (!PhotonFeed.InRoom) _ovr.Clear();
        }
    }
}
