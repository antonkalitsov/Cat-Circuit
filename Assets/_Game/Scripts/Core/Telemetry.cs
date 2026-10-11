using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace CatCircuit
{
    /// <summary>
    /// Where telemetry is sent. Both empty = nothing is sent (events are only printed to the Console).
    /// For a Google Form: FormUrl = "https://docs.google.com/forms/d/e/&lt;id&gt;/formResponse",
    /// FieldEntry = the paragraph question's "entry.123456" name.
    /// </summary>
    public static class TelemetryConfig
    {
        public const string FormUrl = "";
        public const string FieldEntry = "";
    }

    /// <summary>
    /// Anonymous play statistics for the thesis: <c>Telemetry.Log("hint_shown", text, puzzle)</c>.
    /// Every event has: seconds since the game started, type, scene, puzzle id, detail. Each sent batch starts
    /// with a header line: random player id (PlayerPrefs 'cc_player_id'), session id, game version, platform.
    /// No names, nothing the player typed.
    ///
    /// In the editor or while <see cref="TelemetryConfig.FormUrl"/> is empty, events are only printed as
    /// "[Telemetry] ...". Otherwise they are kept in memory and POSTed as JSON lines in one form field.
    /// Off when the player turns off Настройки ▸ Анонимна статистика (<see cref="GameSettings.Telemetry"/>).
    /// Creates itself when the game starts – nothing to place in scenes.
    /// </summary>
    public class Telemetry : MonoBehaviour
    {
        private struct Entry
        {
            public float t;
            public string type, scene, detail;
            public int puzzle;
        }

        private const string KeyPlayerId = "cc_player_id";
        private const int MaxQueued = 500;
        private const int MaxPerRequest = 100;
        private const float FlushInterval = 20f;

        private static Telemetry instance;
        private static readonly List<Entry> queue = new List<Entry>();
        private static string playerId, sessionId, sceneName = "";
        private static float sessionStart, sceneStart;
        private static int currentPuzzle;

        private float flushTimer;

        /// <summary>Puzzle id of the puzzle in the current scene (set by CircuitPuzzle), used when Log gets 0.</summary>
        public static int CurrentPuzzle { get => currentPuzzle; set => currentPuzzle = value; }

        /// <summary>Seconds since the current scene was entered.</summary>
        public static int SecondsInScene => Mathf.RoundToInt(Time.realtimeSinceStartup - sceneStart);

        private static bool SendingEnabled =>
            !Application.isEditor && !string.IsNullOrEmpty(TelemetryConfig.FormUrl) && !string.IsNullOrEmpty(TelemetryConfig.FieldEntry);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            queue.Clear();
            sessionId = null;
            sceneName = "";
            currentPuzzle = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            var go = new GameObject("Telemetry");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<Telemetry>();

            sessionStart = sceneStart = Time.realtimeSinceStartup;
            sessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
            sceneName = SceneManager.GetActiveScene().name;
            SceneManager.sceneLoaded += OnSceneLoaded;

            Log("session_start");
            Log("scene_enter");
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            sceneName = scene.name;
            sceneStart = Time.realtimeSinceStartup;
            Log("scene_enter");
        }

        private void OnDestroy()
        {
            if (instance != this) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            instance = null;
        }

        // ------------------------------------------------------------------ API

        /// <summary>Records one event. puzzle 0 = the puzzle of the current scene (if any).</summary>
        public static void Log(string type, string detail = null, int puzzle = 0)
        {
            try
            {
                if (!GameSettings.Telemetry || string.IsNullOrEmpty(type)) return;
                if (puzzle == 0) puzzle = currentPuzzle;

                var e = new Entry
                {
                    t = Time.realtimeSinceStartup - sessionStart,
                    type = type,
                    scene = sceneName,
                    detail = detail ?? "",
                    puzzle = puzzle,
                };

                if (!SendingEnabled)
                {
                    Debug.Log($"[Telemetry] {e.t:0.0}s {type} scene={e.scene} puzzle={puzzle} {e.detail}");
                    return;
                }

                if (queue.Count >= MaxQueued) queue.RemoveAt(0);   // drop the oldest
                queue.Add(e);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Telemetry] " + ex.Message);
            }
        }

        /// <summary>Sends everything that is waiting (max 100 events per request). Never throws.</summary>
        public static void Flush()
        {
            if (queue.Count == 0) return;
            if (!SendingEnabled || !GameSettings.Telemetry) { queue.Clear(); return; }

            try
            {
                while (queue.Count > 0)
                {
                    int n = Mathf.Min(MaxPerRequest, queue.Count);
                    string body = BuildBatch(n);
                    queue.RemoveRange(0, n);

                    var form = new WWWForm();
                    form.AddField(TelemetryConfig.FieldEntry, body);
                    UnityWebRequest req = UnityWebRequest.Post(TelemetryConfig.FormUrl, form);
                    // The answer is ignored (a browser hides cross-origin results anyway).
                    req.SendWebRequest().completed += _ => req.Dispose();
                }
            }
            catch (Exception ex)
            {
                queue.Clear();
                Debug.LogWarning("[Telemetry] send failed: " + ex.Message);
            }
        }

        /// <summary>Shortens text for an event detail (hints, error messages).</summary>
        public static string Trim(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return s.Length <= max ? s : s.Substring(0, max);
        }

        // ------------------------------------------------------------------ runner

        private void Update()
        {
            if (queue.Count == 0) { flushTimer = 0f; return; }
            flushTimer += Time.unscaledDeltaTime;
            if (flushTimer < FlushInterval) return;
            flushTimer = 0f;
            Flush();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) Flush();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) Flush();
        }

        private void OnApplicationQuit() => Flush();

        // ------------------------------------------------------------------ batch building

        private static string BuildBatch(int count)
        {
            var sb = new StringBuilder(128 + count * 96);
            sb.Append("{\"pid\":\"").Append(PlayerId)
              .Append("\",\"sid\":\"").Append(sessionId ?? "")
              .Append("\",\"ver\":\"").Append(Escape(Application.version))
              .Append("\",\"platform\":\"").Append(Platform)
              .Append("\",\"n\":").Append(count).Append("}\n");

            for (int i = 0; i < count; i++)
            {
                Entry e = queue[i];
                sb.Append("{\"t\":").Append(e.t.ToString("0.0", CultureInfo.InvariantCulture))
                  .Append(",\"type\":\"").Append(Escape(e.type))
                  .Append("\",\"scene\":\"").Append(Escape(e.scene))
                  .Append("\",\"puzzle\":").Append(e.puzzle)
                  .Append(",\"detail\":\"").Append(Escape(e.detail))
                  .Append("\"}\n");
            }
            return sb.ToString();
        }

        private static string PlayerId
        {
            get
            {
                if (!string.IsNullOrEmpty(playerId)) return playerId;
                playerId = PlayerPrefs.GetString(KeyPlayerId, "");
                if (playerId.Length != 8)
                {
                    const string chars = "abcdefghjkmnpqrstuvwxyz23456789";
                    var rng = new System.Random(Guid.NewGuid().GetHashCode());
                    var id = new char[8];
                    for (int i = 0; i < id.Length; i++) id[i] = chars[rng.Next(chars.Length)];
                    playerId = new string(id);
                    PlayerPrefs.SetString(KeyPlayerId, playerId);
                    PlayerPrefs.Save();
                }
                return playerId;
            }
        }

        private static string Platform
        {
            get
            {
                if (Application.isEditor) return "EDITOR";
                switch (Application.platform)
                {
                    case RuntimePlatform.WebGLPlayer: return "WEBGL";
                    case RuntimePlatform.WindowsPlayer: return "WINDOWS";
                    default: return Application.platform.ToString().ToUpperInvariant();
                }
            }
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
