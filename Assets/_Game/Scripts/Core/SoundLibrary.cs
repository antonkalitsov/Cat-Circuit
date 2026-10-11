using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatCircuit
{
    /// <summary>Optional real sound files. Must be named "CatCircuitSounds" and live in a Resources folder.</summary>
    [CreateAssetMenu(fileName = SoundLibrary.ResourceName, menuName = "CatCircuit/Sound Library")]
    public class SoundLibrary : ScriptableObject
    {
        public const string ResourceName = "CatCircuitSounds";

        [Serializable]
        public class Entry
        {
            public Sfx sound;
            public AudioClip clip;
        }

        [Tooltip("Only the sounds you add here are replaced – the rest stay synthesized.")]
        public List<Entry> sounds = new List<Entry>();
        [Tooltip("Background music (loops).")]
        public AudioClip music;

        public AudioClip Get(Sfx s)
        {
            foreach (Entry e in sounds) if (e != null && e.sound == s) return e.clip;
            return null;
        }
    }
}
