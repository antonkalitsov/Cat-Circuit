namespace CatCircuit
{
    /// <summary>
    /// Every short on-screen text in one place, so wording / language can be changed easily.
    /// (NPC dialogue lives on the NPCs and puzzle stages, not here.)
    /// </summary>
    public static class UIText
    {
        public const string KeyInteract = "[E]";
        public const string KeyRotate = "[R]";
        public const string KeyDrop = "[Q]";

        public static string PickUp(string item) => $"{KeyInteract} Вземи: {item}";
        public static string Place(string item) => $"{KeyInteract} Постави: {item}";
        public static string TakeFromSlot(string item) => $"{KeyInteract} Вземи {item}   {KeyRotate} Завърти";
        public const string SlotTaken = "Мястото е заето – намери празно.";
        public static string Talk(string npc) => $"{KeyInteract} Говори с {npc}";
        public const string DialogueNext = "[E] >>";
        public static string Enter(string place) => string.IsNullOrEmpty(place) || place == "Door"
            ? $"{KeyInteract} Влез"
            : $"{KeyInteract} Влез: {place}";
        public const string DropFirst = "Първо остави това, което носиш  " + KeyDrop;
        public static string Holding(string item) => $"Носиш: {item}   {KeyDrop} Пусни   {KeyRotate} Завърти";
        public static string UseTerminal(string name) => $"{KeyInteract} Пиши код на: {name}";
        public static string ToggleSwitch(string id, bool closed) => $"{KeyInteract} {(closed ? "Изключи" : "Включи")} ключ {id}";
        public static string TalkTo(string npc) => $"Говори с {npc}!";
        public const string SceneMissing = "Тази врата още не води никъде.";
    }
}
