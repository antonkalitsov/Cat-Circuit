# CatCircuit – programmer guide

Everything electrical in the game runs through **one real circuit simulator**.
Breadboards (parts placed by the cat) and the code terminal (circuit typed by the player)
both feed it, and puzzles just check its results ("is L1 lit?", "does any LED burn?").

---

## 0. First time after the rewrite – run the upgrade (once)

Back up the project first. Then in Unity, top menu **CatCircuit ▸ Upgrade**:

| Step | What it does |
|---|---|
| **1. Organize script files** | Moves scripts into the folders below (GUIDs are kept, nothing breaks). Wait for the recompile. |
| **2. Upgrade all scenes** | Converts Houses 1–4 + Outside: writes each board's circuit, adds `CircuitPuzzle` stages, turns the old LEDs into `LEDLight`, links the NPCs, removes old scripts/listeners. |
| **3. Clean up prefabs & data** | Removes old scripts from prefabs, gives prefabs clear names/folders (models untouched), sets part types on the ComponentData assets. |
| **4. Delete legacy scripts** | Deletes the old scripts that nothing uses any more. |
| **5. Apply fixes** | Makes the House 2 LED pickable again, turns parts the circuit already contains into decoration, and turns on the sparkle effect for every puzzle. |

Then **CatCircuit ▸ Create ▸ House 5 – Mayor code puzzle scene**, and **CatCircuit ▸ Validate open scene** in every scene.
Each step writes `Assets/_Game/CatCircuit_UpgradeReport.txt`.

---

## 1. Folders

```
Scripts/
  Core/         GameProgress, UIManager (+ GameUI helpers), UIText (all short UI strings), GameInput
  Player/       Player (movement), PlayerCarry (the mouth), PlayerInteractor (E/R/Q), spawner
  Interaction/  CatInteractable (base for everything you press E on), PickupableItem, PickupTrigger, SceneTransition
  Circuit/      ComponentData, CircuitScript (language), CircuitSolver (physics), CircuitHost, LEDLight
  Breadboard/   Breadboard, BreadboardSlot
  Puzzles/      CircuitPuzzle, CircuitConditions (stages, conditions, hints)
  NPC/          CatNPC
  CodeCircuit/  CodeTerminal, CodeCircuitBuilder, CircuitSwitch
  Editor/       upgrade tool, create menus, validator, inspectors, recipes (example puzzles)
  Legacy/       old scripts kept only until the upgrade is done
prefabs/
  Parts/        Part_Battery, Part_LED, Part_Resistor_100/330/1k/10k   ← things the cat carries
  Models/       LED on/off models, decorative LED
  Board/        Board_TwoSlots, Slot, Deco_Wire
  Characters/   Player_Cat, NPC_Cat
  Environment/  House_Interior
```

---

## 2. How interaction works (why only one thing reacts to E)

`CatInteractable` is the base class of pickups, slots, NPCs, doors, terminals and switches.
When the player enters an object's **trigger**, it registers with the player's `PlayerInteractor`.
Each frame the interactor picks **one** focus (highest `Interact Priority`, then nearest) whose
`GetPrompt()` is not null, shows that prompt, and sends E / R to it. Q drops the held item.

New interactable = inherit `CatInteractable`, implement `GetPrompt` + `Interact`. A trigger is
created automatically if the object has none (`EnsureInteractionTrigger`).

Held items are the **real objects** (moved, never destroyed/respawned) so their components survive.

---

## 3. Make a new board puzzle (5 minutes)

1. **GameObject ▸ CatCircuit ▸ Circuit Board Puzzle** (or add `Breadboard` to your board model).
2. Add slots: child objects with a trigger `BoxCollider` + `BreadboardSlot`. Give each a **Slot Id** (`S1`, `S2`…).
   * `Require Orientation` + `Correct Rotation Y` → part only works rotated correctly (+180° = backwards).
   * `Accept Only` → limit which parts fit. `Starts With` → part already in the slot.
3. Write the **circuit** on the Breadboard (see §5). Slots appear as `slot S1`.
4. LEDs on the table: add `LEDLight`, set **Element Id** (`L1`). Emission mode or swap on/off models.
   LEDs placed *in* slots light up automatically.
5. Add **`CircuitPuzzle`** → stages → conditions/hints/dialogue. Set `Progress Id` (puzzle number).
6. On the NPC (`CatNPC`): set **Puzzle**. Done – the quest flow is automatic:
   greeting → objective → stage solved → "Talk to X" → stage dialogue → next stage …
7. **CatCircuit ▸ Validate open scene.**

The inspector of a Breadboard shows circuit errors while editing and live currents in Play mode.
The CircuitPuzzle inspector shows ✔/✘ for every condition in Play mode (+ a cheat button).

`Editor/CatCircuitRecipes.cs` contains the circuits and stages of every house – copy from there.

---

## 4. Add a new part type

1. Make a prefab of the model, add `PickupableItem`, put it in `prefabs/Parts`.
2. **Create ▸ CatCircuit ▸ Component Data**: name, World Prefab (the prefab, not the .obj!),
   **Electrical Kind** (Battery / Resistor / LED / Wire / Switch) and its value (Ω, V, LED colour),
   slot rotation/offset.
3. Put a copy in the scene. That's it – slots, the simulator and the code builder know it.

---

## 5. The circuit language (CircuitScript)

Used in Breadboard circuit texts **and** typed by players in the code terminal.
Keywords work in English and Bulgarian (`battery/батерия`, `resistor/резистор`, `led/лед`, `switch/ключ`, `wire/жица`).

```
# comment
battery B 9V          # volts (default 9V)
resistor R1 220       # 220, 1k, 4.7k, 2k2, 10kΩ
led L1 red            # red orange yellow green blue white (sets forward voltage 1.8–3.0V)
switch S1 on          # on / off – in the code puzzle the cat can flip it with E
wire W1
slot SA wire          # BOARDS ONLY: "wire" = an empty slot acts like a wire

B+ -> R1 -> L1 -> B-  # a chain; arrows = direction of the current
B+ -> R1 -> A         # A is a junction (any new name)
A -> L1 -> B-         # two chains sharing A  =  parallel
A -> L2 -> B-
```

Rules: a chain starts and ends at a **point** (junction name or terminal like `B+`, `L1-`);
each part may be in the **middle** of only one chain (use `R1+`/`R1-` for extra wires).
Battery: current in at `-`, out at `+`. LED: in at `+` (anode / long leg).

---

## 6. Puzzle conditions

`LedsLit, LedsOff, AnyLedLit, LitLedCountAtLeast, LedCurrentBetween (mA), LedsBrightnessAtLeast (0–1),
LedsEqualBrightness, NoLedOverloaded, AnyLedOverloaded, AnyLedReversed, ShortCircuit, NoCurrent,
SlotsFilled, SlotsContain, BatteryVoltageAtMost, PartCountAtLeast, SwitchTurnsAllLedsOff, CircuitHasErrors`

* **Targets**: comma list of LED / slot ids (`L1,S3`); empty = all LEDs (or all slots).
* **Invert** = NOT. Hints are shown (as a short message) when *all* their conditions are true.

---

## 7. Physics numbers

All in `CircuitConstants` (`Circuit/CircuitCore.cs`): 20 mA = full brightness, > 30 mA = LED burns
(flickers), battery internal resistance 0.5 Ω, short circuit > 1 A. LED colours set the forward voltage.
Examples: 9V + 330Ω + red LED = 20.8 mA; House 3 (12V, 3 LEDs): 100Ω 45 mA (burns), 1kΩ 6.3 mA, 10kΩ 0.7 mA.

---

## 8. Code puzzle (House 5)

* `CodeTerminal` – press E → code editor (no UI setup needed; Ctrl+Enter build, Esc close),
  shows syntax help, errors with line numbers and a multimeter read-out.
* `CodeCircuitBuilder` – builds the circuit on the floor (one row per chain, wires drawn automatically,
  glowing where current flows) using the models from its **Part Library**, then simulates it.
* `CircuitPuzzle` next to the builder = the Mayor's 3 tasks (first LED → two LEDs on 3V → master switch).

Add one anywhere with **GameObject ▸ CatCircuit ▸ Code Circuit Station**.

---

## 9. Effects

Every `CircuitPuzzle` spawns **electric sparkles** (sparks + crackling arcs + a flash) at each lit LED when a
stage is solved (`ElectricSparkle.Spawn(position)` – built from code, no setup). Put your own prefab in
**Complete Effect** to use it instead (e.g. `prefabs/VFX/CircuitCompleteVFX`), or untick **Sparkle On Complete**.

The code builder draws a breadboard under the parts (`Show Board`, optional `Board Material`), 3D wires that
glow yellow where current flows (red on a short circuit) and puts every model flat on the board.

---

## 10. UI, sounds, pause menu

* **Corners** – held item top-left, objective top-right. Settings: the **Canvas** object in each scene ▸ UIManager ▸ Corner layout (margin, width, max screen fraction; untick to keep your own layout). Old TMP margins are cleared automatically.
* **Text backgrounds** – `UIManager` adds a `TextBackdrop` (dark rounded box that follows the text size) to the
  prompt/dialogue, objective and held-item texts. Dialogue gets a coloured stripe and the speaker's name in colour.
  Colours: UIManager ▸ Readability. Add `TextBackdrop` to any other TMP text the same way.
* **Sounds** – `GameAudio.Play(Sfx.X)`. All sounds are synthesized in code (no audio files needed):
  pickup, place, drop, rotate, stage complete (chime + crackle), puzzle complete (fanfare), hint, zap
  (short circuit / burning LED), dialogue blip, door, UI click, build ok/error, switch.
  To use real recordings: **CatCircuit ▸ Create ▸ Sound Library** and drop clips in (+ optional music).
* **Pause menu** – Esc. Continue / Settings (volumes, graphics quality, fullscreen – saved in PlayerPrefs) /
  Controls / Restart room / Back to village / Quit. Creates itself, nothing to place in scenes.
  `UIFocus.Push()/Pop()` is what the terminal and the menu use to free the mouse and stop the cat.

---

## 11. Building (Windows .exe / Web)

**CatCircuit ▸ Build ▸ Prepare project for building** (once): creates shader keep-materials in
`Resources/ShaderKeep` (so sparkles/glowing LEDs aren't pink in builds), checks the scene list (Outside first,
House1–5), sets Web compression to Gzip + decompression fallback, warns if the web font is missing.
Then **Build Windows (.exe)** → `Builds/Windows/` or **Build Web** → `Builds/Web/` (project folder, next to Assets).

Web notes: put a Cyrillic .ttf at `Assets/_Game/Resources/CatCircuitUIFont.ttf` (IMGUI menus can't use OS fonts
in a browser). Quit is hidden on the web. Test with Build And Run (double-clicking index.html does not work).

---

## 12. Troubleshooting

| Problem | Fix |
|---|---|
| Nothing happens on E | Object needs a trigger collider (auto-added) and the player a `PlayerCarry`. Check the prompt text. |
| LED never lights | Inspector of the board in Play mode shows the currents. Validate the scene. Check `LEDLight` Element Id. |
| "No BreadboardSlot with Slot Id …" | The circuit uses a slot id that no slot has. |
| Puzzle never completes | CircuitPuzzle inspector (Play mode) shows which condition is ✘. |
| Door does nothing | Target scene must be in the build scene list. |
| Pink / missing effects in a build | Run Build ▸ Prepare project for building again. |
| Boxes instead of letters in the web menu | Missing `Resources/CatCircuitUIFont.ttf`. |
