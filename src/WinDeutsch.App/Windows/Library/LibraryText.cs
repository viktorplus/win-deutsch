using System.Globalization;
using WinDeutsch.Core.Models;

namespace WinDeutsch.App.Windows.Library;

// Russian UI texts for enums and dates shown by the library.
internal static class LibraryText
{
    public static string Familiarity(Familiarity f) => f switch
    {
        Core.Models.Familiarity.New => "Новая",
        Core.Models.Familiarity.Unknown => "Не знаю",
        Core.Models.Familiarity.Learning => "Учу",
        _ => "Знаю",
    };

    public static string Outcome(ShowOutcome o) => o switch
    {
        ShowOutcome.Shown => "показ",
        ShowOutcome.Correct => "верно",
        ShowOutcome.Wrong => "неверно",
        ShowOutcome.Known => "знаю",
        ShowOutcome.Unknown => "не знаю",
        ShowOutcome.Later => "позже",
        ShowOutcome.Excluded => "исключена",
        _ => "повторить чаще",
    };

    public static string Mode(DisplayMode m) => m == DisplayMode.Widget ? "виджет" : "карточка";

    public static string Local(DateTimeOffset t) =>
        t.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);
}
