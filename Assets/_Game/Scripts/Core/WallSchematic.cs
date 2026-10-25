using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CatCircuit
{
    /// <summary>
    /// A chalkboard with the circuit diagram of THIS house's puzzle, on one wall of House1..House5,
    /// drawn with the IEC / БДС symbols used in Bulgarian schools (battery = long/short plate, resistor =
    /// rectangle, LED = diode triangle + bar + two arrows, switch = open lever). The frame has the house colour.
    /// Dashed boxes = the places (slots) the player fills. House 5 shows the CURRENT task and its CircuitScript code.
    ///
    /// Placement: a GameObject named "SchematicAnchor" (its blue Z arrow pointing into the room) wins.
    /// Otherwise the nearest big wall around the puzzle board is used, ~1.6 m high, not over a door;
    /// if there is none, it stands on an easel next to the board. Creates itself – no scene setup needed.
    /// </summary>
    public class WallSchematic : MonoBehaviour
    {
        private const float BoardW = 2.4f, BoardH = 1.6f;
        private const float Line = 0.022f;
        private const float ZWire = -0.02f, ZMask = -0.026f, ZSymbol = -0.032f;   // in front of the board face (-0.015)

        private static readonly Color Chalk = new Color(0.93f, 0.95f, 0.92f);
        private static readonly Color BoardColor = new Color(0.12f, 0.17f, 0.15f);
        private static readonly Color NoteColor = new Color(1f, 0.88f, 0.45f);

        private int house;
        private bool ohmBoard;   // the "Ohm's law" board next to Sparky in the village
        private CircuitPuzzle puzzle;
        private Transform content;
        private Color houseColor;
        private Material chalk, mask, accent;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            if (scene.buildIndex == 0)
            {
                var ohm = new GameObject("OhmBoard").AddComponent<WallSchematic>();
                ohm.house = 3;
                ohm.ohmBoard = true;
                return;
            }
            int h = HouseInfo.FromScene(scene.name);
            if (h == 0) return;
            var go = new GameObject("WallSchematic");
            go.AddComponent<WallSchematic>().house = h;   // lives in the house scene only
        }

        private void Start()
        {
            houseColor = HouseInfo.Color(house);
            chalk = WorldBuild.Unlit(Chalk);
            mask = WorldBuild.Unlit(BoardColor);
            accent = WorldBuild.Unlit(houseColor);

            if (ohmBoard)
            {
                if (!PlaceNextToSparky()) { Destroy(gameObject); return; }
                BuildBoard();
                Redraw();
                return;
            }

            foreach (CircuitPuzzle p in FindObjectsByType<CircuitPuzzle>(FindObjectsSortMode.None))
                if (p.ProgressId == house) { puzzle = p; break; }

            Place();
            BuildBoard();
            Redraw();

            if (puzzle != null && house == 5) puzzle.StageCompleted += OnStageCompleted;
        }

        private void OnDestroy()
        {
            if (puzzle != null) puzzle.StageCompleted -= OnStageCompleted;
        }

        private void OnStageCompleted(int stage) => Redraw();

        // ------------------------------------------------------------------ placement

        /// <summary>Board sizes tried, biggest first (1 = 2.4 x 1.6 m). The biggest that fits the wall wins.</summary>
        private static readonly float[] Scales = { 3f, 2.6f, 2.2f, 1.9f, 1.6f, 1.35f, 1.15f, 1f };

        private void Place()
        {
            GameObject anchor = GameObject.Find("SchematicAnchor");
            if (anchor != null)
            {
                Vector3 f = anchor.transform.forward;
                f.y = 0f;
                transform.position = anchor.transform.position;
                transform.rotation = Quaternion.LookRotation(f.sqrMagnitude > 0.01f ? -f.normalized : Vector3.forward);
                float s = anchor.transform.lossyScale.x;
                transform.localScale = Vector3.one * (s > 0.2f && Mathf.Abs(s - 1f) > 0.01f ? s : 2f);   // anchor scale = board size
                return;
            }

            CircuitHost host = puzzle != null && puzzle.Circuit != null ? puzzle.Circuit : FindAnyObjectByType<CircuitHost>();
            Vector3 center = host != null ? host.transform.position : Vector3.zero;
            float floor = WorldBuild.GroundY(center + Vector3.up * 0.5f, center.y, 1f);
            Vector3 eye = new Vector3(center.x, floor + 1.6f, center.z);
            float ceiling = Physics.Raycast(eye, Vector3.up, out RaycastHit up, 20f, ~0, QueryTriggerInteraction.Ignore) ? up.point.y : floor + 6f;

            float bestScale = 0f, bestDist = float.MaxValue, bestY = 0f;
            RaycastHit best = default;
            for (int i = 0; i < 8; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
                if (!Physics.Raycast(eye, dir, out RaycastHit hit, 16f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (Mathf.Abs(hit.normal.y) > 0.3f || hit.distance < 0.8f) continue;
                if (hit.collider.GetComponentInParent<CatInteractable>() != null || NearDoor(hit.point)) continue;

                foreach (float s in Scales)
                {
                    if (s < bestScale || (Mathf.Approximately(s, bestScale) && hit.distance >= bestDist)) break;
                    float h = BoardH * s;
                    float cy = Mathf.Max(floor + 1.6f, floor + 0.25f + h / 2f);       // bottom at least 25 cm above the floor
                    if (cy + h / 2f > ceiling - 0.05f) cy = ceiling - 0.05f - h / 2f;    // ...and under the ceiling
                    if (cy - h / 2f < floor + 0.1f) continue;                            // too tall for this room
                    if (!WallFits(hit, BoardW * s, cy - hit.point.y, h)) continue;
                    bestScale = s; bestDist = hit.distance; bestY = cy; best = hit;
                    break;
                }
            }

            if (bestScale > 0f)
            {
                Vector3 n = best.normal;
                n.y = 0f;
                n.Normalize();
                transform.position = new Vector3(best.point.x, bestY, best.point.z) + n * 0.03f;
                transform.rotation = Quaternion.LookRotation(-n);
                transform.localScale = Vector3.one * bestScale;
                return;
            }

            // No good wall: a big easel next to the board, facing the room.
            Vector3 toRoom = Vector3.forward;
            PlayerSpawner spawner = FindAnyObjectByType<PlayerSpawner>();
            if (spawner != null) { toRoom = spawner.DefaultSpawnPosition - center; toRoom.y = 0f; }
            if (toRoom.sqrMagnitude < 0.01f) toRoom = Vector3.forward;
            toRoom.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, toRoom);
            PlaceOnEasel(center + side * 3f, toRoom, Mathf.Min(1.8f, (ceiling - floor - 0.4f) / BoardH), floor);
        }

        /// <summary>Village: an easel beside Sparky (the info NPC unlocked by puzzle 3), facing the same way he does.</summary>
        private bool PlaceNextToSparky()
        {
            CatNPC sparky = null;
            foreach (CatNPC n in FindObjectsByType<CatNPC>(FindObjectsSortMode.None))
                if (n.Puzzle == null && (n.NpcName == "Спарки" || n.UnlockedAfterPuzzle == 3)) { sparky = n; break; }
            if (sparky == null) return false;

            Vector3 front = sparky.transform.forward;
            front.y = 0f;
            if (front.sqrMagnitude < 0.01f) front = Vector3.forward;
            front.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, front);
            Vector3 eye = sparky.transform.position + Vector3.up;
            float scale = 1.6f, offset = 1.3f + BoardW * scale / 2f;
            Vector3 pos = sparky.transform.position + side * offset;
            if (Physics.Linecast(eye, pos + Vector3.up, ~0, QueryTriggerInteraction.Ignore)) pos = sparky.transform.position - side * offset;
            PlaceOnEasel(pos + front * 0.3f, front, scale, sparky.transform.position.y);
            return true;
        }

        /// <summary>Free-standing board at <paramref name="pos"/> (ground found below), front facing <paramref name="toViewer"/>.</summary>
        private void PlaceOnEasel(Vector3 pos, Vector3 toViewer, float scale, float floorGuess)
        {
            scale = Mathf.Max(1f, scale);
            float ground = WorldBuild.GroundY(new Vector3(pos.x, floorGuess + 1f, pos.z), floorGuess, 1.5f);
            transform.localScale = Vector3.one * scale;
            transform.position = new Vector3(pos.x, ground + 0.5f + BoardH * scale / 2f, pos.z);
            transform.rotation = Quaternion.LookRotation(-toViewer);
            BuildEasel(0.5f / scale + BoardH / 2f);
        }

        private static bool NearDoor(Vector3 p)
        {
            foreach (SceneTransition door in FindObjectsByType<SceneTransition>(FindObjectsSortMode.None))
            {
                Vector3 d = door.transform.position - p;
                d.y = 0f;
                if (d.sqrMagnitude < 1.8f * 1.8f) return true;
            }
            return false;
        }

        /// <summary>Is there flat wall behind the whole board (corners + edge middles)?</summary>
        private static bool WallFits(RaycastHit hit, float width, float centerDy, float height)
        {
            Vector3 n = hit.normal;
            n.y = 0f;
            n.Normalize();
            Vector3 along = Vector3.Cross(Vector3.up, n);
            foreach (float sx in new[] { -0.5f, 0f, 0.5f })
                foreach (float sy in new[] { -0.5f, 0.5f })
                {
                    Vector3 from = hit.point + n * 0.4f + along * (sx * width) + Vector3.up * (centerDy + sy * height);
                    if (!Physics.Raycast(from, -n, out RaycastHit h2, 0.8f, ~0, QueryTriggerInteraction.Ignore)) return false;
                    if (Mathf.Abs(h2.distance - 0.4f) > 0.15f) return false;
                }
            return true;
        }

        /// <summary>Two legs from the board down to the ground (<paramref name="drop"/> = board centre to ground, board units).</summary>
        private void BuildEasel(float drop)
        {
            Material wood = WorldBuild.Unlit(new Color(0.45f, 0.3f, 0.18f));
            float length = drop + BoardH / 2f;
            foreach (float x in new[] { -0.9f, 0.9f })
                WorldBuild.Primitive(PrimitiveType.Cube, transform, new Vector3(x, BoardH / 2f - length / 2f, 0.06f), new Vector3(0.06f, length, 0.05f), wood, "Leg");
        }

        // ------------------------------------------------------------------ board

        private void BuildBoard()
        {
            WorldBuild.Primitive(PrimitiveType.Cube, transform, Vector3.zero, new Vector3(BoardW, BoardH, 0.03f), mask, "Chalkboard");
            float f = 0.07f;
            Material frame = accent;
            WorldBuild.Primitive(PrimitiveType.Cube, transform, new Vector3(0f, BoardH / 2f, -0.01f), new Vector3(BoardW + f, f, 0.05f), frame, "FrameTop");
            WorldBuild.Primitive(PrimitiveType.Cube, transform, new Vector3(0f, -BoardH / 2f, -0.01f), new Vector3(BoardW + f, f, 0.05f), frame, "FrameBottom");
            WorldBuild.Primitive(PrimitiveType.Cube, transform, new Vector3(-BoardW / 2f, 0f, -0.01f), new Vector3(f, BoardH + f, 0.05f), frame, "FrameLeft");
            WorldBuild.Primitive(PrimitiveType.Cube, transform, new Vector3(BoardW / 2f, 0f, -0.01f), new Vector3(f, BoardH + f, 0.05f), frame, "FrameRight");

            // House number badge in the corner – same colour and number as the sign outside.
            WorldBuild.Disc(transform, new Vector3(-BoardW / 2f + 0.14f, BoardH / 2f - 0.14f, -0.03f), 0.13f, 0.02f, accent, "Badge");
            TextMeshPro n = WorldBuild.Text(transform, ohmBoard ? "Ω" : house.ToString(), 2.2f, new Color(0.08f, 0.08f, 0.1f),
                                            new Vector3(-BoardW / 2f + 0.14f, BoardH / 2f - 0.15f, -0.045f), TextAlignmentOptions.Center, 0.3f);
            n.fontStyle = FontStyles.Bold;
        }

        private void Redraw()
        {
            if (content != null) Destroy(content.gameObject);
            content = new GameObject("Content").transform;
            content.SetParent(transform, false);

            if (ohmBoard) { DrawOhm(); return; }
            Title($"Къща {house} – {HouseInfo.Title(house)}");
            switch (house)
            {
                case 1: DrawHouse1(); break;
                case 2: DrawHouse2(); break;
                case 3: DrawHouse3(); break;
                case 4: DrawHouse4(); break;
                case 5: DrawHouse5(puzzle != null ? Mathf.Min(puzzle.CompletedStages, 2) : 0, puzzle != null && puzzle.IsComplete); break;
            }
        }

        // ------------------------------------------------------------------ the five diagrams
        // Board coordinates: metres, (0,0) = centre, x right, y up. Usable area about x ±1.05, y −0.62…0.5.

        private void DrawHouse1()
        {
            // Battery (slot) – resistor (slot) – LED in one closed loop.
            Loop(-0.8f, 0.8f, -0.38f, 0.32f);
            Battery(new Vector2(-0.8f, -0.03f), true, "батерия", true);
            Resistor(new Vector2(0f, 0.32f), false, "резистор", true);
            Led(new Vector2(0.8f, -0.03f), Vector2.down, "LED", false);
            CurrentArrow(new Vector2(-0.45f, 0.32f), Vector2.right);
            Note("Пунктирът показва празните места на платката.\nСложи батерията и резистора – кръгът трябва да е затворен!");
        }

        private void DrawHouse2()
        {
            Loop(-0.8f, 0.8f, -0.38f, 0.32f);
            Battery(new Vector2(-0.8f, -0.03f), true, "9 V", false);
            Resistor(new Vector2(0f, 0.32f), false, "R1  330 Ω", false);
            Led(new Vector2(0.8f, -0.03f), Vector2.down, "LED", true);
            Label("+ (анод,\nдълъг крак)", new Vector2(0.48f, 0.14f), 0.75f, NoteColor);
            CurrentArrow(new Vector2(-0.45f, 0.32f), Vector2.right);
            CurrentArrow(new Vector2(0.8f, 0.22f), Vector2.down);
            Note("Токът влиза в LED-а през + (дългия крак).\nОбърнат LED не свети – завърти го с R.");
        }

        private void DrawHouse3()
        {
            Loop(-0.85f, 0.85f, -0.42f, 0.32f);
            Battery(new Vector2(-0.85f, -0.05f), true, "12 V", false);
            Resistor(new Vector2(-0.15f, 0.32f), false, "R = ?", true);
            Led(new Vector2(0.85f, 0.16f), Vector2.down, "", false);
            Led(new Vector2(0.85f, -0.05f), Vector2.down, "", false);
            Led(new Vector2(0.85f, -0.26f), Vector2.down, "", false);
            Label("3 LED", new Vector2(0.6f, -0.05f), 0.75f, Chalk);
            CurrentArrow(new Vector2(0.45f, 0.32f), Vector2.right);
            Note("I = U / R      цел: 3 – 25 mA през LED-ите\nТвърде голям ток – изгарят. Твърде малък – не светят.");
        }

        private void DrawHouse4()
        {
            const float left = -0.75f, right = 0.85f, top = 0.36f, mid = 0.1f, low = -0.13f, bottom = -0.38f;
            // Rails and the battery on the bottom wire (+ on the left: the current goes up the left rail).
            Wire(new Vector2(left, top), new Vector2(left, bottom));
            Wire(new Vector2(right, top), new Vector2(right, bottom));
            Wire(new Vector2(left, bottom), new Vector2(right, bottom));
            Battery(new Vector2(0.05f, bottom), false, "6 V", false);

            // Top branch: R1 + three LEDs one after another (series).
            Wire(new Vector2(left, top), new Vector2(right, top));
            Resistor(new Vector2(-0.48f, top), false, "200 Ω", false);
            Led(new Vector2(-0.08f, top), Vector2.right, "", false, true);
            Led(new Vector2(0.22f, top), Vector2.right, "", false, true);
            Led(new Vector2(0.52f, top), Vector2.right, "", false, true);
            Label("последователно", new Vector2(0.22f, top - 0.12f), 0.75f, NoteColor);

            // Two parallel branches, each with its own resistor and LED.
            foreach (float y in new[] { mid, low })
            {
                Wire(new Vector2(left, y), new Vector2(right, y));
                Resistor(new Vector2(-0.35f, y), false, "200 Ω", false);
                Led(new Vector2(0.3f, y), Vector2.right, "", false, true);
                Dot(new Vector2(left, y));
                Dot(new Vector2(right, y));
            }
            Label("успоредно", new Vector2(0.62f, (mid + low) / 2f), 0.75f, NoteColor);
            Note("Горе LED-ите са един след друг – делят си напрежението.\nДолу всеки LED има свой път – свети ярко.");
        }

        private void DrawHouse5(int stage, bool done)
        {
            Label(done ? "Всички задачи са решени!" : $"Задача {stage + 1} от 3", new Vector2(0f, 0.47f), 0.85f, NoteColor);
            string code1, code2;
            if (stage == 0)
            {
                Loop(-0.75f, 0.75f, -0.3f, 0.3f);
                Battery(new Vector2(-0.75f, 0f), true, "B  9 V", false);
                Resistor(new Vector2(0f, 0.3f), false, "R1  330 Ω", false);
                Led(new Vector2(0.75f, 0f), Vector2.down, "L1", false);
                CurrentArrow(new Vector2(-0.4f, 0.3f), Vector2.right);
                code1 = "battery B 9V   resistor R1 330   led L1 red";
                code2 = "B+ -> R1 -> L1 -> B-";
            }
            else
            {
                bool withSwitch = stage >= 2;
                const float left = -0.42f, right = 0.85f, top = 0.28f, low = -0.04f, bottom = -0.3f;
                Wire(new Vector2(left, top), new Vector2(left, low));
                Wire(new Vector2(right, top), new Vector2(right, bottom));
                Wire(new Vector2(-0.95f, bottom), new Vector2(right, bottom));
                Wire(new Vector2(-0.95f, bottom), new Vector2(-0.95f, top));
                Wire(new Vector2(-0.95f, top), new Vector2(left, top));
                Battery(new Vector2(-0.95f, -0.01f), true, "B  3 V", false);
                if (withSwitch) Switch(new Vector2(-0.7f, top), "S1");
                foreach (var (y, r, l) in new[] { (top, "R1", "L1"), (low, "R2", "L2") })
                {
                    Wire(new Vector2(left, y), new Vector2(right, y));
                    Resistor(new Vector2(-0.05f, y), false, r + "  68 Ω", false);
                    Led(new Vector2(0.45f, y), Vector2.right, l, false);
                }
                Dot(new Vector2(left, top));
                Dot(new Vector2(right, low));
                if (withSwitch) Label("A", new Vector2(left - 0.06f, top + 0.08f), 0.7f, Chalk);
                code1 = withSwitch ? "B+ -> S1 -> A" : "battery B 3V   resistor R1 68   resistor R2 68";
                code2 = withSwitch ? "A -> R1 -> L1 -> B-     A -> R2 -> L2 -> B-" : "B+ -> R1 -> L1 -> B-     B+ -> R2 -> L2 -> B-";
            }
            Label(code1, new Vector2(0f, -0.5f), 0.75f, NoteColor);
            Label(code2, new Vector2(0f, -0.62f), 0.75f, NoteColor);
        }

        /// <summary>Sparky's board: U = I · R, the memory triangle, units and the House 3 example.</summary>
        private void DrawOhm()
        {
            Title("Законът на Ом");
            TextMeshPro big = WorldBuild.Text(content, "U = I · R", 1.6f, NoteColor, new Vector3(-0.6f, 0.44f, ZSymbol), TextAlignmentOptions.Center, 1f);
            big.fontStyle = FontStyles.Bold;

            // Memory triangle: cover what you look for. U on top, I and R below.
            Vector2 a = new Vector2(-1.0f, -0.38f), b = new Vector2(-0.2f, -0.38f), c = new Vector2(-0.6f, 0.3f);
            Bar(a, b, ZSymbol, chalk); Bar(b, c, ZSymbol, chalk); Bar(c, a, ZSymbol, chalk);
            float midY = -0.06f;
            float halfAtMid = 0.4f * (0.3f - midY) / 0.68f;
            Bar(new Vector2(-0.6f - halfAtMid, midY), new Vector2(-0.6f + halfAtMid, midY), ZSymbol, chalk);
            Bar(new Vector2(-0.6f, midY), new Vector2(-0.6f, -0.38f), ZSymbol, chalk);
            Label("U", new Vector2(-0.6f, 0.07f), 1.3f, Chalk, 0.3f);
            Label("I", new Vector2(-0.77f, -0.24f), 1.3f, Chalk, 0.3f);
            Label("R", new Vector2(-0.43f, -0.24f), 1.3f, Chalk, 0.3f);
            Label("Закрий с пръст това, което търсиш:\nI = U / R     R = U / I", new Vector2(-0.6f, -0.52f), 0.62f, NoteColor, 1.1f);

            const string text =
                "<b>U</b> – напрежение, във волтове (V)\n" +
                "     колко силно батерията бута тока\n" +
                "<b>I</b> – ток, в ампери (A)\n" +
                "     1 mA = 0,001 A\n" +
                "<b>R</b> – съпротивление, в омове (Ω)\n" +
                "     колко резисторът пречи на тока\n" +
                "\n" +
                "<b>Пример – Къща 3:</b>\n" +
                "12 V – 3 LED × 1,8 V = 6,6 V за R\n" +
                "I = 6,6 V / 1000 Ω = 6,6 mA\n" +
                "6,6 mA е между 3 и 25 mA – светят!";
            TextMeshPro t = WorldBuild.Text(content, text, 0.6f, Chalk, new Vector3(0.58f, 0.36f, ZSymbol), TextAlignmentOptions.TopLeft, 1.15f);
            t.richText = true;
        }

        // ------------------------------------------------------------------ symbols

        private void Title(string text)
        {
            TextMeshPro t = WorldBuild.Text(content, text, 1.15f, Chalk, new Vector3(0.08f, BoardH / 2f - 0.15f, ZSymbol), TextAlignmentOptions.Center, BoardW - 0.4f);
            t.fontStyle = FontStyles.Bold;
        }

        private void Note(string text) => Label(text, new Vector2(0f, -0.6f), 0.78f, NoteColor, BoardW - 0.2f);

        private void Label(string text, Vector2 at, float size, Color color, float width = 1.2f)
        {
            WorldBuild.Text(content, text, size, color, new Vector3(at.x, at.y, ZSymbol), TextAlignmentOptions.Center, width);
        }

        private void Bar(Vector2 a, Vector2 b, float z, Material m, float width = Line) => WorldBuild.Bar(content, a, b, width, m, z, 0.004f);

        private void Wire(Vector2 a, Vector2 b) => Bar(a, b, ZWire, chalk);

        private void Loop(float x0, float x1, float y0, float y1)
        {
            Wire(new Vector2(x0, y0), new Vector2(x1, y0));
            Wire(new Vector2(x1, y0), new Vector2(x1, y1));
            Wire(new Vector2(x1, y1), new Vector2(x0, y1));
            Wire(new Vector2(x0, y1), new Vector2(x0, y0));
        }

        private void Dot(Vector2 at) => Bar(at - new Vector2(0.025f, 0f), at + new Vector2(0.025f, 0f), ZSymbol, chalk, 0.05f);

        /// <summary>Hides the wire under a symbol.</summary>
        private void Mask(Vector2 c, Vector2 u, Vector2 v, float halfLen, float halfWidth)
        {
            WorldBuild.Bar(content, c - u * halfLen, c + u * halfLen, halfWidth * 2f, mask, ZMask, 0.004f);
        }

        /// <summary>Dashed box in the house colour = a slot the player fills.</summary>
        private void SlotBox(Vector2 c, Vector2 u, Vector2 v, float halfLen, float halfWidth)
        {
            Vector2[] corners = { c - u * halfLen - v * halfWidth, c + u * halfLen - v * halfWidth, c + u * halfLen + v * halfWidth, c - u * halfLen + v * halfWidth };
            for (int s = 0; s < 4; s++)
            {
                Vector2 a = corners[s], b = corners[(s + 1) % 4];
                int dashes = Mathf.Max(2, Mathf.RoundToInt((b - a).magnitude / 0.07f));
                for (int i = 0; i < dashes; i += 2)
                    Bar(Vector2.Lerp(a, b, i / (float)dashes), Vector2.Lerp(a, b, (i + 1) / (float)dashes), ZSymbol, accent, 0.016f);
            }
        }

        /// <summary>IEC battery: long thin plate (+) and short thick plate (−). vertical = on an up/down wire, + at the top.</summary>
        private void Battery(Vector2 c, bool vertical, string label, bool slot)
        {
            Vector2 u = vertical ? Vector2.up : Vector2.left;   // towards "+"
            Vector2 v = vertical ? Vector2.right : Vector2.up;
            Mask(c, u, v, 0.06f, 0.05f);
            Bar(c + u * 0.03f - v * 0.12f, c + u * 0.03f + v * 0.12f, ZSymbol, chalk, 0.022f);    // long (+)
            Bar(c - u * 0.03f - v * 0.06f, c - u * 0.03f + v * 0.06f, ZSymbol, chalk, 0.045f);    // short (−)
            Label("+", c + u * 0.09f + v * (vertical ? 0.14f : 0.1f), 0.8f, Chalk, 0.2f);
            Label("−", c - u * 0.09f + v * (vertical ? 0.14f : 0.1f), 0.8f, Chalk, 0.2f);
            if (!string.IsNullOrEmpty(label)) Label(label, vertical ? c + v * 0.3f : c - v * 0.11f, 0.8f, Chalk, 0.34f);
            if (slot) SlotBox(c, u, v, 0.14f, 0.2f);
        }

        /// <summary>IEC resistor: a rectangle on the wire.</summary>
        private void Resistor(Vector2 c, bool vertical, string label, bool slot)
        {
            Vector2 u = vertical ? Vector2.up : Vector2.right;
            Vector2 v = vertical ? Vector2.left : Vector2.up;
            Mask(c, u, v, 0.12f, 0.05f);
            Vector2 a = c - u * 0.12f - v * 0.045f, b = c + u * 0.12f - v * 0.045f, d = c + u * 0.12f + v * 0.045f, e = c - u * 0.12f + v * 0.045f;
            Bar(a, b, ZSymbol, chalk); Bar(b, d, ZSymbol, chalk); Bar(d, e, ZSymbol, chalk); Bar(e, a, ZSymbol, chalk);
            if (!string.IsNullOrEmpty(label)) Label(label, c + v * 0.12f, 0.8f, Chalk, 0.8f);
            if (slot) SlotBox(c, u, v, 0.18f, 0.1f);
        }

        /// <summary>IEC LED: triangle pointing in the current direction, bar at the tip, two light arrows.</summary>
        private void Led(Vector2 c, Vector2 dir, string label, bool slot, bool small = false)
        {
            float s = small ? 0.8f : 1f;
            Vector2 u = dir.normalized, v = new Vector2(-u.y, u.x);
            Mask(c, u, v, 0.07f * s, 0.07f * s);
            Vector2 baseA = c - u * 0.055f * s + v * 0.06f * s, baseB = c - u * 0.055f * s - v * 0.06f * s, tip = c + u * 0.055f * s;
            Bar(baseA, baseB, ZSymbol, chalk);
            Bar(baseA, tip, ZSymbol, chalk);
            Bar(baseB, tip, ZSymbol, chalk);
            Bar(tip + v * 0.06f * s, tip - v * 0.06f * s, ZSymbol, chalk);   // cathode bar
            // Light arrows (pointing away from the diode).
            Vector2 side = v.y < 0f || (Mathf.Approximately(v.y, 0f) && v.x < 0f) ? -v : v;   // arrows on the "up / right" side
            for (int i = 0; i < 2; i++)
            {
                Vector2 from = c + u * (0.0f + i * 0.045f) * s + side * 0.07f * s;
                Vector2 dirA = (side + u * 0.6f).normalized;
                Vector2 to = from + dirA * 0.09f * s;
                Bar(from, to, ZSymbol, accent, 0.014f);
                Vector2 back = -dirA, perp = new Vector2(-dirA.y, dirA.x);
                Bar(to, to + (back + perp) * 0.025f * s, ZSymbol, accent, 0.014f);
                Bar(to, to + (back - perp) * 0.025f * s, ZSymbol, accent, 0.014f);
            }
            if (!string.IsNullOrEmpty(label)) Label(label, c - side * 0.15f, 0.8f, Chalk, 0.5f);
            if (slot || small) SlotBox(c, u, v, 0.11f * s, 0.11f * s);
        }

        /// <summary>IEC switch: two contacts and an open lever.</summary>
        private void Switch(Vector2 c, string label)
        {
            Mask(c, Vector2.right, Vector2.up, 0.1f, 0.05f);
            Vector2 a = c - Vector2.right * 0.09f, b = c + Vector2.right * 0.09f;
            Bar(a - Vector2.right * 0.012f, a + Vector2.right * 0.012f, ZSymbol, chalk, 0.035f);
            Bar(b - Vector2.right * 0.012f, b + Vector2.right * 0.012f, ZSymbol, chalk, 0.035f);
            Bar(a, a + new Vector2(0.15f, 0.08f), ZSymbol, chalk);
            Label(label, c + Vector2.up * 0.16f, 0.8f, Chalk, 0.4f);
        }

        /// <summary>Small arrowhead on a wire showing the direction of the current, with "I".</summary>
        private void CurrentArrow(Vector2 at, Vector2 dir)
        {
            Vector2 u = dir.normalized, v = new Vector2(-u.y, u.x);
            Bar(at, at - u * 0.05f + v * 0.04f, ZSymbol, accent, 0.018f);
            Bar(at, at - u * 0.05f - v * 0.04f, ZSymbol, accent, 0.018f);
            Label("I", at + v * 0.09f + (Mathf.Abs(u.x) > 0.5f ? Vector2.zero : new Vector2(0.06f, 0f)), 0.8f, houseColor, 0.2f);
        }
    }
}
