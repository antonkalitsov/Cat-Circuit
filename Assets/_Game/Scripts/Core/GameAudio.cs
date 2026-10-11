using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatCircuit
{
    /// <summary>Every sound effect in the game.</summary>
    public enum Sfx
    {
        Pickup, Place, Drop, Rotate,
        StageComplete, PuzzleComplete, Hint, Zap,
        Blip, Door, UiClick, BuildOk, BuildError, Switch,
    }

    /// <summary>
    /// Plays sounds:  GameAudio.Play(Sfx.Pickup);
    ///
    /// Works with NO audio files: every sound is synthesized when the game starts.
    /// To use real recordings instead, create  CatCircuit ▸ Create ▸ Sound Library  and drop clips in –
    /// any clip you set replaces the built-in one, and a Music clip plays in the background.
    /// Volumes come from <see cref="GameSettings"/> (pause menu ▸ Настройки).
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        private static GameAudio instance;

        private AudioSource sfxSource;
        private AudioSource musicSource;
        private readonly Dictionary<Sfx, AudioClip> clips = new Dictionary<Sfx, AudioClip>();
        private readonly Dictionary<Sfx, float> lastPlayed = new Dictionary<Sfx, float>();

        public static GameAudio Instance
        {
            get
            {
                if (instance == null && Application.isPlaying)
                {
                    var go = new GameObject("GameAudio");
                    DontDestroyOnLoad(go);
                    instance = go.AddComponent<GameAudio>();
                }
                return instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            GameSettings.Apply();
            _ = Instance;
        }

        /// <summary>Play a sound effect. <paramref name="pitchJitter"/> adds a little variation (0.05 = ±5 %).</summary>
        public static void Play(Sfx sound, float volume = 1f, float pitchJitter = 0f)
        {
            GameAudio a = Instance;
            if (a != null) a.PlayInternal(sound, volume, pitchJitter);
        }

        /// <summary>Call after changing volumes in GameSettings.</summary>
        public static void RefreshVolumes()
        {
            if (instance != null) instance.ApplyVolumes();
        }

        public static bool HasMusic => instance != null && instance.musicSource != null && instance.musicSource.clip != null;

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;

            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.spatialBlend = 0f;

            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = true;
            musicSource.spatialBlend = 0f;

            SoundLibrary library = Resources.Load<SoundLibrary>(SoundLibrary.ResourceName);
            foreach (Sfx s in Enum.GetValues(typeof(Sfx)))
            {
                AudioClip custom = library != null ? library.Get(s) : null;
                clips[s] = custom != null ? custom : SoundSynth.Create(s);
            }

            if (library != null && library.music != null)
            {
                musicSource.clip = library.music;
                musicSource.Play();
            }
            ApplyVolumes();
        }

        private void ApplyVolumes()
        {
            AudioListener.volume = GameSettings.MasterVolume;
            if (sfxSource != null) sfxSource.volume = GameSettings.SfxVolume;
            if (musicSource != null) musicSource.volume = GameSettings.MusicVolume * 0.6f;
        }

        private void PlayInternal(Sfx sound, float volume, float pitchJitter)
        {
            // The same sound many times in one moment just gets loud – skip repeats within 50 ms.
            float now = Time.unscaledTime;
            if (lastPlayed.TryGetValue(sound, out float t) && now - t < 0.05f) return;
            lastPlayed[sound] = now;

            if (!clips.TryGetValue(sound, out AudioClip clip) || clip == null) return;
            sfxSource.pitch = 1f + (pitchJitter > 0f ? UnityEngine.Random.Range(-pitchJitter, pitchJitter) : 0f);
            sfxSource.PlayOneShot(clip, volume);
        }
    }

    /// <summary>Tiny synthesizer – makes all game sounds from sine waves and noise, so no audio files are needed.</summary>
    public static class SoundSynth
    {
        private const int Rate = 44100;
        private static System.Random rng = new System.Random(1234);

        public static AudioClip Create(Sfx s)
        {
            float[] data;
            switch (s)
            {
                case Sfx.Pickup: data = Sweep(0.09f, 480f, 900f, 0.004f, 30f, 0.5f); break;
                case Sfx.Drop: data = Mix(Sweep(0.16f, 190f, 70f, 0.003f, 18f, 0.8f), Noise(0.06f, 0.25f, 60f)); break;
                case Sfx.Place: data = Mix(Noise(0.012f, 0.6f, 300f), Tone(0.05f, 1400f, 0.002f, 60f, 0.4f), Offset(Tone(0.12f, 330f, 0.004f, 25f, 0.5f), 0.02f)); break;
                case Sfx.Rotate: data = Mix(Noise(0.015f, 0.5f, 250f), Tone(0.02f, 2600f, 0.001f, 150f, 0.25f)); break;
                case Sfx.Switch: data = Mix(Noise(0.01f, 0.7f, 400f), Tone(0.03f, 1800f, 0.001f, 120f, 0.35f), Offset(Noise(0.01f, 0.5f, 400f), 0.04f)); break;
                case Sfx.StageComplete: data = Mix(Crackle(0.25f, 0.35f), Arpeggio(new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.085f, 0.7f)); break;
                case Sfx.PuzzleComplete:
                    data = Mix(Crackle(0.3f, 0.35f),
                               Arpeggio(new[] { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f }, 0.09f, 0.8f),
                               Offset(Chord(new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 1.4f), 0.5f));
                    break;
                case Sfx.Hint: data = Mix(Tone(0.14f, 660f, 0.01f, 12f, 0.4f), Offset(Tone(0.18f, 523.25f, 0.01f, 10f, 0.4f), 0.13f)); break;
                case Sfx.Zap: data = Mix(Crackle(0.45f, 0.8f), Buzz(0.35f, 60f, 0.25f)); break;
                case Sfx.Blip: data = Tone(0.05f, 760f, 0.003f, 45f, 0.35f); break;
                case Sfx.Door: data = Mix(Swoosh(0.35f, 0.45f), Offset(Sweep(0.12f, 120f, 60f, 0.003f, 20f, 0.6f), 0.25f)); break;
                case Sfx.UiClick: data = Tone(0.035f, 1500f, 0.001f, 90f, 0.35f); break;
                case Sfx.BuildOk: data = Mix(Tone(0.12f, 660f, 0.005f, 14f, 0.4f), Offset(Tone(0.25f, 990f, 0.005f, 9f, 0.4f), 0.1f)); break;
                case Sfx.BuildError: data = Mix(Square(0.14f, 150f, 0.25f), Offset(Square(0.2f, 120f, 0.25f), 0.17f)); break;
                default: data = Tone(0.05f, 440f, 0.005f, 30f, 0.3f); break;
            }
            Normalize(data, Loudness(s));
            AudioClip clip = AudioClip.Create("sfx_" + s, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>How loud each sound is compared to the others (0..1).</summary>
        private static float Loudness(Sfx s)
        {
            switch (s)
            {
                case Sfx.StageComplete: return 0.8f;
                case Sfx.PuzzleComplete: return 0.9f;
                case Sfx.Zap: return 0.55f;
                case Sfx.Blip: case Sfx.UiClick: case Sfx.Rotate: return 0.35f;
                case Sfx.Hint: return 0.45f;
                case Sfx.BuildError: return 0.35f;
                default: return 0.6f;
            }
        }

        // ------------------------------------------------------------------ building blocks

        private static float[] Buffer(float seconds) => new float[Mathf.Max(1, (int)(seconds * Rate))];

        private static float Env(float t, float attack, float decayRate) =>
            (t < attack ? t / attack : 1f) * Mathf.Exp(-decayRate * Mathf.Max(0f, t - attack));

        private static float[] Tone(float seconds, float freq, float attack, float decay, float amp)
        {
            float[] d = Buffer(seconds);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                d[i] = amp * Env(t, attack, decay) * Mathf.Sin(2f * Mathf.PI * freq * t);
            }
            return FadeOut(d);
        }

        private static float[] Sweep(float seconds, float from, float to, float attack, float decay, float amp)
        {
            float[] d = Buffer(seconds);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(from, to, t / seconds);
                phase += 2f * Mathf.PI * f / Rate;
                d[i] = amp * Env(t, attack, decay) * Mathf.Sin(phase);
            }
            return FadeOut(d);
        }

        /// <summary>A bell-like note (a few inharmonic partials).</summary>
        private static float[] Bell(float seconds, float freq, float amp)
        {
            float[] d = Buffer(seconds);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float w = 2f * Mathf.PI * freq * t;
                float s = Mathf.Sin(w) + 0.45f * Mathf.Sin(2.01f * w) * Mathf.Exp(-t * 4f) + 0.2f * Mathf.Sin(3.98f * w) * Mathf.Exp(-t * 8f);
                d[i] = amp * Env(t, 0.004f, 5f) * s;
            }
            return FadeOut(d);
        }

        private static float[] Arpeggio(float[] notes, float step, float noteLength)
        {
            var parts = new List<float[]>();
            for (int i = 0; i < notes.Length; i++) parts.Add(Offset(Bell(noteLength, notes[i], 0.4f), i * step));
            return Mix(parts.ToArray());
        }

        private static float[] Chord(float[] notes, float seconds)
        {
            float[] d = Buffer(seconds);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float s = 0f;
                foreach (float f in notes) s += Mathf.Sin(2f * Mathf.PI * f * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * f * t);
                d[i] = 0.18f * Env(t, 0.02f, 2.2f) * s;
            }
            return FadeOut(d);
        }

        private static float[] Noise(float seconds, float amp, float decay)
        {
            float[] d = Buffer(seconds);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                d[i] = amp * Env(t, 0.0005f, decay) * ((float)rng.NextDouble() * 2f - 1f);
            }
            return FadeOut(d);
        }

        /// <summary>Electric crackle: noise switched on and off in random short bursts.</summary>
        private static float[] Crackle(float seconds, float amp)
        {
            float[] d = Buffer(seconds);
            bool on = true;
            int left = 0;
            for (int i = 0; i < d.Length; i++)
            {
                if (left-- <= 0) { on = rng.NextDouble() > 0.45; left = rng.Next(40, 600); }
                float t = i / (float)Rate;
                float fade = 1f - t / seconds;
                d[i] = on ? amp * fade * ((float)rng.NextDouble() * 2f - 1f) : 0f;
            }
            return FadeOut(d);
        }

        private static float[] Buzz(float seconds, float freq, float amp)
        {
            float[] d = Buffer(seconds);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float saw = 2f * (t * freq - Mathf.Floor(t * freq + 0.5f));
                d[i] = amp * saw * (1f - t / seconds);
            }
            return FadeOut(d);
        }

        private static float[] Square(float seconds, float freq, float amp)
        {
            float[] d = Buffer(seconds);
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                d[i] = amp * Env(t, 0.005f, 6f) * (Mathf.Sin(2f * Mathf.PI * freq * t) >= 0f ? 1f : -1f);
            }
            return FadeOut(d);
        }

        /// <summary>Low-passed noise that rises and falls ("whoosh").</summary>
        private static float[] Swoosh(float seconds, float amp)
        {
            float[] d = Buffer(seconds);
            float lp = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)Rate;
                float shape = Mathf.Sin(Mathf.PI * t / seconds);
                lp += 0.06f * (((float)rng.NextDouble() * 2f - 1f) - lp);
                d[i] = amp * shape * lp * 3f;
            }
            return FadeOut(d);
        }

        private static float[] Offset(float[] src, float seconds)
        {
            int pad = (int)(seconds * Rate);
            var d = new float[src.Length + pad];
            Array.Copy(src, 0, d, pad, src.Length);
            return d;
        }

        private static float[] Mix(params float[][] parts)
        {
            int len = 0;
            foreach (float[] p in parts) len = Mathf.Max(len, p.Length);
            var d = new float[len];
            foreach (float[] p in parts)
                for (int i = 0; i < p.Length; i++) d[i] += p[i];
            return d;
        }

        /// <summary>Short fade at the end so nothing clicks.</summary>
        private static float[] FadeOut(float[] d)
        {
            int n = Mathf.Min(d.Length, 200);
            for (int i = 0; i < n; i++) d[d.Length - 1 - i] *= i / (float)n;
            return d;
        }

        private static void Normalize(float[] d, float peak)
        {
            float max = 0f;
            foreach (float v in d) max = Mathf.Max(max, Mathf.Abs(v));
            if (max < 1e-5f) return;
            float k = peak / max;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
        }
    }
}
