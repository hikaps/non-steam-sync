namespace SteamShortcutsImporter;

internal class ExportContext
{
    public bool SkipAllRemaining { get; set; } = false;

    /// <summary>
    /// When true, the export never shows dialogs: games needing manual exe selection or name-merge are skipped.
    /// </summary>
    public bool Unattended { get; set; } = false;
}
