namespace MechSim;

/// <summary>The campaign mission being played (or about to be), between the menu and the battle.</summary>
public static class CampaignRun
{
    /// <summary>The mission of the battle being played; null in a battle set up in the menus.</summary>
    public static MissionInfo Mission;
    /// <summary>Whether the main menu opens on the campaign page (coming back from a mission).</summary>
    public static bool BackToCampaign;
    /// <summary>
    /// A mission made in the editor (its <see cref="Mission"/> info is made from it too): played from this
    /// instead of a scene. Null for the campaign's own missions and for a battle set up in the menus.
    /// </summary>
    public static MissionDoc Custom;
    /// <summary>The custom mission is being tried out from the editor: the end of it goes back there.</summary>
    public static bool FromEditor;
    /// <summary>Whether the main menu opens on the page of one's own missions (coming back from one).</summary>
    public static bool BackToMissions;
}
