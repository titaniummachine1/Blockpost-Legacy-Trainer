namespace BlockpostTrainer.Sdk
{
    /// <summary>
    /// Known weapon / inventory item ids, codenames and display names.
    /// Populated from an in-game dump of GUIInv.OIHNJCKDOIG (NAHLLMJMOED[]).
    /// The runtime source of truth is still GUIInv.AllWeapons; this is a convenience lookup.
    /// </summary>
    public static class Weapons
    {
        public static readonly System.Collections.Generic.Dictionary<int, string> CodenameById = new()
        {
            [23] = "beretta92",
            [28] = "kriss_vector",
            [68] = "shovel",
            [69] = "block",
            [71] = "sl8",
            [107] = "ammo",
            [108] = "grenade",
            [109] = "medkit",
            [110] = "shield",
        };

        public static readonly System.Collections.Generic.Dictionary<int, string> NameById = new()
        {
            [23] = "Beretta 92",
            [28] = "KRISS Vector",
            [68] = "Shovel",
            [69] = "Block",
            [71] = "SL8",
            [107] = "BP Ammo",
            [108] = "BP Grenade",
            [109] = "BP Medkit",
            [110] = "BP Armor",
        };

        public static readonly System.Collections.Generic.Dictionary<string, int> IdByCodename = new()
        {
            ["beretta92"] = 23,
            ["kriss_vector"] = 28,
            ["shovel"] = 68,
            ["block"] = 69,
            ["sl8"] = 71,
            ["ammo"] = 107,
            ["grenade"] = 108,
            ["medkit"] = 109,
            ["shield"] = 110,
        };
    }
}
