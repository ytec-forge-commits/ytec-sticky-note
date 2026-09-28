using System.Globalization;
using System.Windows.Markup;
using System.Windows.Media;
using YtecStickyNote.Models;

namespace YtecStickyNote.Services;

public static class FontCatalog
{
    private static readonly string[] FavoriteFamilies =
    [
        "Yu Gothic UI",
        "Meiryo",
        "BIZ UDPGothic",
        "BIZ UDPMincho",
        "Yu Gothic",
        "Yu Mincho",
        "MS Gothic",
        "MS Mincho",
        "Segoe UI",
        "Arial"
    ];

    private static readonly XmlLanguage[] PreferredLanguages =
    [
        XmlLanguage.GetLanguage("ja-JP"),
        XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.IetfLanguageTag),
        XmlLanguage.GetLanguage("en-US")
    ];

    public static IReadOnlyList<FontChoice> GetInstalledFonts() => BuildChoices(Fonts.SystemFontFamilies);

    public static IReadOnlyList<FontChoice> BuildChoices(IEnumerable<FontFamily> families)
    {
        ArgumentNullException.ThrowIfNull(families);

        var comparer = StringComparer.Create(CultureInfo.GetCultureInfo("ja-JP"), ignoreCase: true);
        return families
            .Where(family => !string.IsNullOrWhiteSpace(family.Source))
            .GroupBy(family => family.Source, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(family =>
            {
                var favoriteRank = GetFavoriteRank(family.Source);
                return new
                {
                    Choice = new FontChoice(family.Source, GetDisplayName(family), favoriteRank >= 0),
                    FavoriteRank = favoriteRank < 0 ? int.MaxValue : favoriteRank
                };
            })
            .OrderBy(item => item.FavoriteRank)
            .ThenBy(item => item.Choice.DisplayName, comparer)
            .Select(item => item.Choice)
            .ToArray();
    }

    private static int GetFavoriteRank(string familyName)
    {
        for (var index = 0; index < FavoriteFamilies.Length; index++)
        {
            if (string.Equals(FavoriteFamilies[index], familyName, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static string GetDisplayName(FontFamily family)
    {
        foreach (var language in PreferredLanguages)
        {
            if (family.FamilyNames.TryGetValue(language, out var localizedName) && !string.IsNullOrWhiteSpace(localizedName))
            {
                return localizedName;
            }
        }

        return family.Source;
    }
}
