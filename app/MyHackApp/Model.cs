// The Flext case, as arithmetic. Every input is editable in the app: a number an investor
// cannot challenge is a number they will not believe.

public enum Metric
{
    Hours,
    Money,
    Co2,
}

/// Everything the calculator runs on. Defaults are Flext's own figures where it has them
/// (20-30 carpenter hours per roof down to 3-4) and Swedish construction norms elsewhere.
public record Assumptions
{
    public double HousesPerYear { get; init; } = 2_000;

    // Flext's claim: the on-site batten layer, before and after.
    public double TraditionalHours { get; init; } = 25;
    public double FlextHours { get; init; } = 3.5;

    // A Swedish carpenter, fully loaded: wage plus social fees plus overhead, SEK per hour.
    public double HourlyRate { get; init; } = 650;

    // The batten layer bought loose - timber, fasteners, the lot - SEK per roof.
    public double LooseMaterialCost { get; init; } = 6_500;

    // The factory-made module. Deliberately the dearer unit: that is the whole objection.
    public double FlextModulePrice { get; init; } = 11_000;

    // Battens cut on a roof make offcuts. A factory jig cuts to length.
    public double SiteWastePercent { get; init; } = 12;
    public double FactoryWastePercent { get; init; } = 2;

    // Sawn softwood in the batten layer, and its cradle-to-gate footprint.
    public double BattenMassKg { get; init; } = 220;
    public double TimberCo2PerKg { get; init; } = 0.31;

    // Crew travel: one van round trip per working day that never has to happen.
    public double HoursPerSiteTrip { get; init; } = 8;
    public double TripKm { get; init; } = 40;
    public double VanCo2PerKm { get; init; } = 0.25;

    // How fast the volume grows once the machine is running.
    public double AnnualGrowthPercent { get; init; } = 20;
}

/// One metric, both ways of building the roof.
public readonly record struct Comparison(double Traditional, double Flext)
{
    public double Saved => Traditional - Flext;

    /// What share of the traditional figure Flext removes, clamped so a nonsense
    /// assumption set cannot produce a grid with negative or runaway squares.
    public double SavedFraction => Traditional <= 0 ? 0 : Math.Clamp(Saved / Traditional, 0, 1);

    /// True when these inputs make Flext the worse way to build. The module price slider
    /// alone can reach it, so every figure drawn from this comparison has to say so rather
    /// than let SavedFraction's clamp round the bad news away.
    public bool Adverse => Saved < 0;

    /// How much worse, as a share of the traditional figure. Only meaningful when Adverse.
    public double AdverseFraction => Traditional <= 0 ? 0 : Math.Min(-Saved / Traditional, 9.99);

    /// The saving written as a size, with the direction carried by the words beside it
    /// rather than by a minus sign.
    public double Magnitude => Math.Abs(Saved);

    public Comparison Times(double n) => new(Traditional * n, Flext * n);
}

public record Outcome(Comparison Hours, Comparison Money, Comparison Co2, Assumptions A)
{
    public Comparison PerRoof(Metric metric) => metric switch
    {
        Metric.Hours => Hours,
        Metric.Money => Money,
        _ => Co2,
    };

    public Comparison PerYear(Metric metric) => PerRoof(metric).Times(A.HousesPerYear);

    /// Roofs built in a given year, counting from year 1, as volume compounds.
    public double HousesInYear(int year) =>
        A.HousesPerYear * Math.Pow(1 + A.AnnualGrowthPercent / 100, year - 1);

    /// Roofs built from year 1 through the given year.
    public double HousesThrough(int throughYear)
    {
        var total = 0.0;

        for (var year = 1; year <= throughYear; year++)
        {
            total += HousesInYear(year);
        }

        return total;
    }

    /// Savings banked from year 1 through the given year.
    public double CumulativeSaved(Metric metric, int throughYear) =>
        PerRoof(metric).Saved * HousesThrough(throughYear);

    /// Both ways of building, accumulated: the gap between them is the saving.
    public Comparison Cumulative(Metric metric, int throughYear) =>
        PerRoof(metric).Times(HousesThrough(throughYear));

    public static Outcome From(Assumptions a)
    {
        var hours = new Comparison(a.TraditionalHours, a.FlextHours);

        // Waste is not free: offcuts are timber you bought and then threw away.
        var looseWithWaste = a.LooseMaterialCost * (1 + a.SiteWastePercent / 100);

        var money = new Comparison(
            a.TraditionalHours * a.HourlyRate + looseWithWaste,
            a.FlextHours * a.HourlyRate + a.FlextModulePrice);

        var timberSite = a.BattenMassKg * (1 + a.SiteWastePercent / 100) * a.TimberCo2PerKg;
        var timberFactory = a.BattenMassKg * (1 + a.FactoryWastePercent / 100) * a.TimberCo2PerKg;

        // Hours removed from the site are crew trips that never happen.
        var perHourTravel = a.HoursPerSiteTrip <= 0
            ? 0
            : a.TripKm * a.VanCo2PerKm / a.HoursPerSiteTrip;

        var co2 = new Comparison(
            timberSite + a.TraditionalHours * perHourTravel,
            timberFactory + a.FlextHours * perHourTravel);

        return new Outcome(hours, money, co2, a);
    }
}

/// How each metric is named and written. Kept in one place so the grid legend, the stat
/// cards and the AI prompt never disagree about what a number means.
public static class Units
{
    public static string Short(Metric metric) => metric switch
    {
        Metric.Hours => "Hours",
        Metric.Money => "Money",
        _ => "CO₂",
    };

    public static string Long(Metric metric) => metric switch
    {
        Metric.Hours => "Carpenter hours on site",
        Metric.Money => "Cost of the batten layer",
        _ => "Carbon footprint",
    };

    public static string Verb(Metric metric) => metric switch
    {
        Metric.Hours => "hours never worked on a roof",
        Metric.Money => "kronor that stay in the build",
        _ => "CO₂e never emitted",
    };

    /// Thousands grouped with a non-breaking space, the Swedish way, and never
    /// broken across a line.
    public static string Group(double value) =>
        Math.Round(value).ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ");

    private static string Decimals(double value, int places) =>
        value.ToString("0." + new string('#', places), CultureInfo.InvariantCulture);

    /// A figure written the way a person would say it out loud.
    public static string Say(Metric metric, double value)
    {
        var v = Math.Abs(value);

        return metric switch
        {
            Metric.Hours when v >= 1_000 => $"{Group(value)} h",
            Metric.Hours => $"{Decimals(value, 1)} h",
            Metric.Money when v >= 1_000_000 => $"{Decimals(value / 1_000_000, 1)} MSEK",
            Metric.Money => $"{Group(value)} SEK",
            _ when v >= 1_000 => $"{Decimals(value / 1_000, 1)} t",
            _ => $"{Group(value)} kg",
        };
    }

    /// One grid square's worth, where the quantity is small and rounding to whole
    /// units would print "0".
    public static string SayPrecise(Metric metric, double value) => metric switch
    {
        Metric.Hours => $"{Decimals(value, 1)} h",
        Metric.Money when Math.Abs(value) >= 1_000_000 => $"{Decimals(value / 1_000_000, 2)} MSEK",
        Metric.Money => $"{Group(value)} SEK",
        _ when Math.Abs(value) >= 1_000 => $"{Decimals(value / 1_000, 2)} t",
        _ => $"{Decimals(value, 1)} kg",
    };

    /// The improvement said as a gain, never as a reduction: a minus sign in front of good
    /// news reads as a loss, and these numbers going down is the entire point of the product.
    public static string Gain(Metric metric, double fraction) => metric switch
    {
        Metric.Hours => $"{Percent(fraction)} freed",
        Metric.Money => $"{Percent(fraction)} cheaper",
        _ => $"{Percent(fraction)} cleaner",
    };

    /// The same figure when the inputs make Flext the worse way to build. Reachable by
    /// hand — the module price slider alone can do it — so the page has to say it rather
    /// than show a green badge over a loss.
    public static string Cost(Metric metric, double fraction) => metric switch
    {
        Metric.Hours => $"{Percent(fraction)} longer",
        Metric.Money => $"{Percent(fraction)} more expensive",
        _ => $"{Percent(fraction)} dirtier",
    };

    public static string CostsVerb(Metric metric) => metric switch
    {
        Metric.Hours => "Flext costs",
        Metric.Money => "Flext adds",
        _ => "Flext emits",
    };

    public static string AddedLabel(Metric metric) => metric switch
    {
        Metric.Hours => "Added by Flext",
        Metric.Money => "Added by Flext",
        _ => "Emitted by Flext",
    };

    public static string CostVerb(Metric metric) => metric switch
    {
        Metric.Hours => "extra hours on the roof",
        Metric.Money => "kronor the layer adds",
        _ => "CO₂e added",
    };

    /// What the saved side is called. Hours are freed, kronor saved, carbon avoided —
    /// one word each, because the right verb is doing the persuading.
    public static string FreedLabel(Metric metric) => metric switch
    {
        Metric.Hours => "Freed by Flext",
        Metric.Money => "Saved by Flext",
        _ => "Avoided by Flext",
    };

    public static string FreesVerb(Metric metric) => metric switch
    {
        Metric.Hours => "Flext frees",
        Metric.Money => "Flext saves",
        _ => "Flext avoids",
    };

    public static string Percent(double fraction) =>
        $"{Math.Round(fraction * 100)}%";
}
