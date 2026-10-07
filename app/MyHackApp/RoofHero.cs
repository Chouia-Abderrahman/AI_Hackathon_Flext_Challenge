using System.Runtime.CompilerServices;

/// One line of the memorandum's value-creation table: SEK for a standard 144 m² roof.
/// Traditional is null only for "FLEXT-machine", which the memo lists as a dash — a cost
/// that does not exist in the traditional method rather than one that happens to be zero.
public readonly record struct CostLine(string Label, double? Traditional, double Flext);

// Every figure the roof hero is allowed to show, taken from Develog's own 2024 memorandum
// ("FLEXT — Removable Prefabricated Decking"), calculated for its standard 144 m² roof.
// Kept in one place so the headline experience and the calculator below it can never quote
// different numbers, and so nobody adds a figure the memorandum doesn't say.
public static class RoofHeroFigures
{
    public const double TraditionalTotal = 31_480;
    public const double FlextTotal = 17_147;

    public static readonly CostLine[] Breakdown =
    [
        new("Material", 5_611, 8_608),
        new("Installation time", 13_750, 2_063),
        new("Project overheads", 11_719, 1_758),
        new("Shipping & packaging", 400, 2_800),
        new("FLEXT machine", null, 1_918),
    ];

    public const string TraditionalHoursLabel = "20–30";
    public const string FlextHoursLabel = "3–4";
    public const string TimeReductionLabel = "Up to 85%";
    public const string MaintenanceDaysLabel = "85% fewer maintenance days";
    public const string ProjectDaysLabel = "Up to 1.5 fewer project days per roof";
    public const string RotProtectionLabel = "10–20 years longer roof life";
}

/// The cinematic, cursor-scrubbed comparison: the same unfinished roof built by hand, then
/// by Flext, then held side by side. Pure client-side animation, so it is one custom React
/// node rather than a tree of server-driven components re-rendering on every pointer move.
public static class RoofHeroExtensions
{
    public static void RoofHero(
        this UIView view,
        string? headline = null,
        string? subheadline = null,
        string[]? style = null,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        view.AddNode(
            "custom.roof-hero",
            new Dictionary<string, object?>
            {
                ["headline"] = headline,
                ["subheadline"] = subheadline,
                ["figures"] = new Dictionary<string, object?>
                {
                    ["traditionalTotal"] = RoofHeroFigures.TraditionalTotal,
                    ["flextTotal"] = RoofHeroFigures.FlextTotal,
                    ["breakdown"] = RoofHeroFigures.Breakdown.Select(line => new Dictionary<string, object?>
                    {
                        ["label"] = line.Label,
                        ["traditional"] = line.Traditional,
                        ["flext"] = line.Flext,
                    }).ToArray(),
                    ["traditionalHoursLabel"] = RoofHeroFigures.TraditionalHoursLabel,
                    ["flextHoursLabel"] = RoofHeroFigures.FlextHoursLabel,
                    ["timeReductionLabel"] = RoofHeroFigures.TimeReductionLabel,
                    ["maintenanceDaysLabel"] = RoofHeroFigures.MaintenanceDaysLabel,
                    ["projectDaysLabel"] = RoofHeroFigures.ProjectDaysLabel,
                    ["rotProtectionLabel"] = RoofHeroFigures.RotProtectionLabel,
                },
            },
            style: style,
            file: file,
            line: line);
    }
}
