using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CatCircuit
{
    /// <summary>
    /// Calm background music made in code – no audio files, so the web build stays small.
    /// ~80 BPM in C major / A minor: soft pad chords, a plucked bass, a sparse bell melody and a light shaker.
    ///
    /// The song is made of 8-bar segments (~24 s). Every segment picks two chord progressions
    /// (C–Am–F–G, F–G–Em–Am, Am–F–C–G) and a new random melody from the pentatonic scale with rests,
    /// so it never repeats exactly; every few segments comes a quieter "breathing" one (pad + a few bells).
    /// Segments are rendered over several frames (coroutine) into AudioClips (works on WebGL – no
    /// OnAudioFilterRead / streaming), at most 3 exist at once, and two AudioSources hand over with a short fade.
    ///
    /// Volume = Настройки ▸ Музика, below the sound effects; it drops to about half while a dialogue or the code
    /// terminal is open. Added by <see cref="GameAudio"/> when the Sound Library has no music clip of its own.
    /// </summary>
    public class MusicPlayer : MonoBehaviour
    {
        private const int Rate = 22050;
        private const float Bpm = 80f;
        private const int Bars = 8;
        private const float Tail = 2f;            // ringing notes after the last bar
        private const float FadeTime = 1f;
        private const float BaseVolume = 0.6f;    // music sits under the SFX (they peak ~4x louder)
        private const int SamplesPerYield = 40000;

        private static readonly float Beat = 60f / Bpm;
        private static readonly float Bar = Beat * 4f;
        private static readonly float SegmentLength = Bar * Bars;

        // Chords: pad voicing (MIDI notes) + bass root.
        private struct Chord { public int[] pad; public int bass; }
        private static readonly Dictionary<string, Chord> chords = new Dictionary<string, Chord>
        {
            { "C",  new Chord { pad = new[] { 55, 60, 64 }, bass = 36 } },
            { "Am", new Chord { pad = new[] { 57, 60, 64 }, bass = 33 } },
            { "F",  new Chord { pad = new[] { 57, 60, 65 }, bass = 41 } },
            { "G",  new Chord { pad = new[] { 55, 59, 62 }, bass = 43 } },
            { "Em", new Chord { pad = new[] { 55, 59, 64 }, bass = 40 } },
        };
        private static readonly string[][] progressions =
        {
            new[] { "C", "Am", "F", "G" },
            new[] { "F", "G", "Em", "Am" },
            new[] { "Am", "F", "C", "G" },
        };
        private static readonly int[] melodyScale = { 72, 74, 76, 79, 81, 84, 86, 88 };   // C D E G A C D E

        private static MusicPlayer instance;

        private AudioSource[] sources;
        private int current = -1;
        private double currentStart;
        private AudioClip nextClip;
        private bool generating;
        private int segmentCount;
        private float duck = 1f;
        private System.Random rng;
        private readonly List<AudioClip> clips = new List<AudioClip>();

        public static bool IsPlaying => instance != null && instance.current >= 0;

        private void Awake()
        {
            instance = this;
            rng = new System.Random(System.Environment.TickCount);
            sources = new AudioSource[2];
            for (int i = 0; i < 2; i++)
            {
                sources[i] = gameObject.AddComponent<AudioSource>();
                sources[i].playOnAwake = false;
                sources[i].loop = false;
                sources[i].spatialBlend = 0f;
                sources[i].ignoreListenerPause = true;
                sources[i].volume = 0f;
            }
            StartCoroutine(Generate());
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            foreach (AudioClip c in clips) if (c != null) Destroy(c);
        }

        private void Update()
        {
            if (sources == null) return;

            // Quieter while someone talks or the code terminal is open.
            bool busy = GameUI.DialogueVisible || CodeTerminal.AnyOpen;
            duck = Mathf.MoveTowards(duck, busy ? 0.5f : 1f, Time.unscaledDeltaTime * 1.5f);
            float target = GameSettings.MusicVolume * BaseVolume * duck;

            double now = AudioSettings.dspTime;

            // Start the first segment, or the next one when the current reaches its last bar line.
            if (nextClip != null && (current < 0 || now >= currentStart + SegmentLength))
            {
                int next = current < 0 ? 0 : 1 - current;
                AudioClip old = sources[next].clip;   // two segments ago – finished by now
                sources[next].Stop();
                sources[next].clip = nextClip;
                if (old != null) { clips.Remove(old); Destroy(old); }
                sources[next].volume = target;
                sources[next].Play();
                current = next;
                currentStart = now;
                nextClip = null;
                if (!generating) StartCoroutine(Generate());
            }

            if (current < 0) return;
            sources[current].volume = target;

            // The previous segment rings out and fades during the first second of the new one.
            AudioSource prev = sources[1 - current];
            if (prev.isPlaying)
            {
                float k = 1f - Mathf.Clamp01((float)(now - currentStart) / FadeTime);
                prev.volume = target * k;
                if (k <= 0f) prev.Stop();
            }
        }

        // ------------------------------------------------------------------ composing

        private IEnumerator Generate()
        {
            generating = true;
            segmentCount++;
            bool breathing = segmentCount > 2 && segmentCount % 4 == 0;   // every 4th: quieter, sparser

            string[] a = progressions[rng.Next(progressions.Length)];
            string[] b = progressions[rng.Next(progressions.Length)];
            var bars = new string[Bars];
            for (int i = 0; i < Bars; i++) bars[i] = i < 4 ? a[i] : b[i - 4];

            int length = Mathf.CeilToInt((SegmentLength + Tail) * Rate);
            var data = new float[length];
            int budget = 0;

            // Pad chords – long, soft, slightly detuned for warmth.
            for (int bar = 0; bar < Bars; bar++)
            {
                Chord c = chords[bars[bar]];
                foreach (int note in c.pad)
                {
                    budget += Pad(data, bar * Bar, Bar + 0.3f, Midi(note), breathing ? 0.045f : 0.04f);
                    if (budget > SamplesPerYield) { budget = 0; yield return null; }
                }
            }

            // Plucked bass on beats 1 and 3 (root, then fifth).
            if (!breathing)
            {
                for (int bar = 0; bar < Bars; bar++)
                {
                    Chord c = chords[bars[bar]];
                    budget += Pluck(data, bar * Bar, Midi(c.bass), 0.16f);
                    budget += Pluck(data, bar * Bar + 2f * Beat, Midi(c.bass + 7), 0.11f);
                    if (rng.NextDouble() < 0.25) budget += Pluck(data, bar * Bar + 3.5f * Beat, Midi(c.bass + 12), 0.07f);
                    if (budget > SamplesPerYield) { budget = 0; yield return null; }
                }
            }

            // Sparse bell melody on eighth notes, moving in small steps, with rests.
            float chance = breathing ? 0.1f : 0.26f;
            int idx = rng.Next(2, 5);
            int inARow = 0;
            for (int bar = 0; bar < Bars; bar++)
            {
                Chord c = chords[bars[bar]];
                for (int e = 0; e < 8; e++)
                {
                    bool strong = e == 0 || e == 4;
                    if (inARow >= 2 || rng.NextDouble() > (strong ? chance * 1.6f : chance)) { inARow = 0; continue; }
                    idx = Mathf.Clamp(idx + rng.Next(-2, 3), 0, melodyScale.Length - 1);
                    int note = melodyScale[idx];
                    if (strong) note = NearestChordTone(note, c.pad);   // land on the harmony on strong beats
                    budget += BellNote(data, bar * Bar + e * Beat * 0.5f, Midi(note), 0.09f + 0.03f * (float)rng.NextDouble());
                    inARow++;
                }
                if (budget > SamplesPerYield) { budget = 0; yield return null; }
            }

            // Light shaker on the off-beats.
            if (!breathing)
            {
                for (int bar = 0; bar < Bars; bar++)
                {
                    for (int e = 0; e < 8; e++)
                        budget += Shaker(data, bar * Bar + e * Beat * 0.5f, e % 2 == 1 ? 0.025f : 0.012f);
                    if (budget > SamplesPerYield) { budget = 0; yield return null; }
                }
            }

            // Soft limiter + fade at the very end.
            for (int i = 0; i < data.Length; i++) data[i] = (float)System.Math.Tanh(data[i] * 1.2f) * 0.8f;
            int fade = Mathf.Min(data.Length, Rate / 2);
            for (int i = 0; i < fade; i++) data[data.Length - 1 - i] *= i / (float)fade;
            yield return null;

            AudioClip clip = AudioClip.Create("music_" + segmentCount, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            clips.Add(clip);
            nextClip = clip;
            generating = false;
        }

        private static float Midi(int note) => 440f * Mathf.Pow(2f, (note - 69) / 12f);

        private static int NearestChordTone(int note, int[] pad)
        {
            int best = note, bestDist = 99;
            foreach (int p in pad)
                for (int oct = -12; oct <= 36; oct += 12)
                {
                    int d = Mathf.Abs(p + oct - note);
                    if (d < bestDist) { bestDist = d; best = p + oct; }
                }
            return best;
        }

        // ------------------------------------------------------------------ instruments (add into data)

        /// <summary>Cheap sine oscillator (rotation recurrence) – much faster than Mathf.Sin per sample.</summary>
        private struct Osc
        {
            private double y1, y2, k;

            public Osc(float freq)
            {
                double w = 2.0 * System.Math.PI * freq / Rate;
                k = 2.0 * System.Math.Cos(w);
                y1 = 0.0;
                y2 = -System.Math.Sin(w);
            }

            public float Next()
            {
                double y = k * y1 - y2;
                y2 = y1;
                y1 = y;
                return (float)y2;
            }
        }

        private static int Pad(float[] d, float start, float length, float freq, float amp)
        {
            int s0 = (int)(start * Rate), n = (int)((length + 1f) * Rate);
            var a = new Osc(freq);
            var b = new Osc(freq * 1.0015f);
            var o = new Osc(freq * 2f);
            for (int i = 0; i < n && s0 + i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Clamp01(t / 0.6f) * (t > length ? Mathf.Clamp01(1f - (t - length)) : 1f);
                d[s0 + i] += amp * env * (a.Next() + b.Next() + 0.25f * o.Next()) * 0.5f;
            }
            return n;
        }

        private static int Pluck(float[] d, float start, float freq, float amp)
        {
            int s0 = (int)(start * Rate), n = (int)(1.4f * Rate);
            var f1 = new Osc(freq);
            var f2 = new Osc(freq * 2f);
            var f3 = new Osc(freq * 3f);
            float e1 = 1f, e2 = 1f, e3 = 1f;
            float k1 = Mathf.Exp(-3f / Rate), k2 = Mathf.Exp(-6f / Rate), k3 = Mathf.Exp(-9f / Rate);
            for (int i = 0; i < n && s0 + i < d.Length; i++)
            {
                float attack = Mathf.Clamp01(i / (0.005f * Rate));
                d[s0 + i] += amp * attack * e1 * (f1.Next() + 0.25f * e2 * f2.Next() + 0.1f * e3 * f3.Next());
                e1 *= k1; e2 *= k2; e3 *= k3;
            }
            return n;
        }

        private static int BellNote(float[] d, float start, float freq, float amp)
        {
            int s0 = (int)(start * Rate), n = (int)(1.6f * Rate);
            var f1 = new Osc(freq);
            var f2 = new Osc(freq * 2.01f);
            var f3 = new Osc(freq * 3.98f);
            float e1 = 1f, e2 = 1f, e3 = 1f;
            float k1 = Mathf.Exp(-2.6f / Rate), k2 = Mathf.Exp(-5f / Rate), k3 = Mathf.Exp(-9f / Rate);
            for (int i = 0; i < n && s0 + i < d.Length; i++)
            {
                float attack = Mathf.Clamp01(i / (0.004f * Rate));
                d[s0 + i] += amp * attack * e1 * (f1.Next() + 0.4f * e2 * f2.Next() + 0.15f * e3 * f3.Next());
                e1 *= k1; e2 *= k2; e3 *= k3;
            }
            return n;
        }

        private int Shaker(float[] d, float start, float amp)
        {
            int s0 = (int)(start * Rate), n = (int)(0.07f * Rate);
            float prev = 0f;
            for (int i = 0; i < n && s0 + i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)rng.NextDouble() * 2f - 1f;
                float hp = noise - prev;   // crude high-pass = "tss"
                prev = noise;
                d[s0 + i] += amp * Mathf.Exp(-60f * t) * hp;
            }
            return n;
        }
    }
}
