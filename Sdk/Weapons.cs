namespace BlockpostTrainer.Sdk
{
    /// <summary>
    /// Known weapon ids/codenames collected from 0x08 weapondata packets.
    /// This is not auto-generated; update it when new loadouts are captured.
    /// The runtime source of truth is GUIInv.OIHNJCKDOIG (NAHLLMJMOED[]).
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
            [108] = "grenade",
        };

        public static readonly System.Collections.Generic.Dictionary<string, int> IdByCodename = new()
        {
            ["beretta92"] = 23,
            ["kriss_vector"] = 28,
            ["shovel"] = 68,
            ["block"] = 69,
            ["sl8"] = 71,
            ["grenade"] = 108,
        };
    }
}
