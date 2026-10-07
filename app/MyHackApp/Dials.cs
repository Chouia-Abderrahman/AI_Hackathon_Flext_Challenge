// Every input the model runs on, as one table. The assumptions panel renders it and the
// chatbot is allowed to move it, so a dial cannot exist in the UI that the chatbot cannot
// see, or be reachable by the chatbot without a slider a person can take back.

/// One editable input: what it is, what it is allowed to be, and how to read and write it.
/// The single source of truth for a dial's range — the same reason Units is the single
/// source of truth for how a figure is written.
public sealed record Dial(
    string Key,
    string Label,
    double Min,
    double Max,
    double Step,
    string Unit,
    string Why,
    Func<Assumptions, double> Get,
    Func<Assumptions, double, Assumptions> Set)
{
    /// Snapped to the dial's own step and held inside its own range. The slider and the
    /// chatbot both come through here, so neither can set a value the other could not.
    public double Clamp(double value) => Math.Clamp(Math.Round(value / Step) * Step, Min, Max);

    public string Say(double value) => $"{value.ToString("0.##", CultureInfo.InvariantCulture)} {Unit}";
}

public partial class MyHackApp
{
    /// The twelve assumptions, in the order they are read on the page.
    private static readonly Dial[] Dials =
    [
        new("traditional_hours", "On-site hours per roof, today", 8, 40, 0.5, "h",
            "Flext's own figure is 20–30 carpenter hours for the batten layer.",
            a => a.TraditionalHours, (a, v) => a with { TraditionalHours = v }),

        new("flext_hours", "On-site hours per roof, with Flext", 1, 12, 0.5, "h",
            "Lifting a finished module into place instead of cutting battens: 3–4 hours.",
            a => a.FlextHours, (a, v) => a with { FlextHours = v }),

        new("hourly_rate", "Carpenter cost", 300, 1_200, 10, "SEK/h",
            "Fully loaded: wage plus social fees plus overhead, not the take-home rate.",
            a => a.HourlyRate, (a, v) => a with { HourlyRate = v }),

        new("loose_material_cost", "Loose battens and fasteners", 1_000, 20_000, 250, "SEK",
            "What the layer costs in materials today, before offcut waste is added.",
            a => a.LooseMaterialCost, (a, v) => a with { LooseMaterialCost = v }),

        new("flext_module_price", "Flext module price", 1_000, 30_000, 250, "SEK",
            "Deliberately dearer than loose timber. This is the objection the hours have to answer.",
            a => a.FlextModulePrice, (a, v) => a with { FlextModulePrice = v }),

        new("site_waste_percent", "Offcut waste, cutting on a roof", 0, 30, 0.5, "%",
            "Timber bought and then thrown away. Drives both cost and carbon.",
            a => a.SiteWastePercent, (a, v) => a with { SiteWastePercent = v }),

        new("factory_waste_percent", "Offcut waste, cut on a factory jig", 0, 15, 0.5, "%",
            "A jig cuts to length, so the same roof needs less timber bought.",
            a => a.FactoryWastePercent, (a, v) => a with { FactoryWastePercent = v }),

        new("batten_mass_kg", "Battens per roof", 50, 600, 10, "kg",
            "Sawn softwood in the batten layer of one house.",
            a => a.BattenMassKg, (a, v) => a with { BattenMassKg = v }),

        new("timber_co2_per_kg", "Carbon in sawn softwood", 0.05, 1.0, 0.01, "kg CO₂e/kg",
            "Cradle to gate: forestry, sawmill and transport to the merchant.",
            a => a.TimberCo2PerKg, (a, v) => a with { TimberCo2PerKg = v }),

        new("trip_km", "Crew travel per site day", 0, 200, 5, "km",
            "A van round trip for every working day the batten job no longer needs.",
            a => a.TripKm, (a, v) => a with { TripKm = v }),

        new("van_co2_per_km", "Van emissions", 0.05, 0.6, 0.01, "kg CO₂e/km",
            "A loaded light commercial van in mixed Nordic driving.",
            a => a.VanCo2PerKm, (a, v) => a with { VanCo2PerKm = v }),

        new("annual_growth_percent", "Volume growth", 0, 60, 1, "%/year",
            $"How fast the line fills once the machine runs. Drives the {HorizonYears}-year totals only.",
            a => a.AnnualGrowthPercent, (a, v) => a with { AnnualGrowthPercent = v }),
    ];

    /// Volume has no dial in the assumptions panel — the hero slider is its control, and a
    /// second slider for the same number would be two places to set one thing. The chatbot
    /// still needs to reach it, so it is named here and nowhere else.
    /// A step of 10 rather than the slider's 100, so the chatbot can land on the volumes the
    /// page itself offers as presets — 250 among them — instead of snapping past them.
    private static readonly Dial VolumeDial = new(
        "houses_per_year", "Roofs per year", MinHouses, MaxHouses, 10, "roofs",
        "The volume the whole page is calculated at. The hero slider sets it.",
        a => a.HousesPerYear, (a, v) => a with { HousesPerYear = v });

    /// Everything the chatbot may move. HoursPerSiteTrip is deliberately absent: it has no
    /// control on the page, so the chatbot must not be able to change it either.
    private static readonly Dial[] Tunable = [VolumeDial, .. Dials];

    private static Dial? FindDial(string key) =>
        Tunable.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));
}
