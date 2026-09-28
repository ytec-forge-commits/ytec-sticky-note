namespace YtecStickyNote.Models;

public sealed record FontChoice(string FamilyName, string DisplayName, bool IsFavorite)
{
    public string DisplayText => IsFavorite ? $"★ {DisplayName}" : DisplayName;
}
