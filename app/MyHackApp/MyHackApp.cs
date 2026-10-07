return await App.Run(args);

// One scenario per person: two people tuning the same sliders should not fight over them.
public record SessionIdentity(string? UserId);

public record ClientParameters(string Name);

/// The pitch the model writes from the numbers on screen.
public record Pitch(string Headline, string[] Points, string Close);

[App]
public partial class MyHackApp(IApp<SessionIdentity, ClientParameters> app)
{
    // Industrial Nordic: warm paper, slate ink, one safety-orange accent for everything
    // Flext takes away. Anton for figures that have to land like a hammer.
    private UI UI { get; } = new(app, new IkonTheme
    {
        ["background"] = "#f5f2ec",
        ["foreground"] = "#15181d",
        ["card"] = "#ffffff",
        ["muted-foreground"] = "#5a5852",
        ["border"] = "#e2ded4",
        ["primary"] = "#ef5b25",
        ["font-heading"] = "Anton",
        ["radius"] = "rounded-xl",

        DarkMode = new IkonTheme
        {
            ["background"] = "#101216",
            ["foreground"] = "#f2efe8",
            ["card"] = "#181b20",
            ["muted-foreground"] = "#9b978e",
            ["border"] = "#262a30",
        },
    });

    private static readonly Metric[] AllMetrics = [Metric.Hours, Metric.Money, Metric.Co2];

    // Volumes a Nordic modular factory would actually recognise.
    private static readonly double[] Presets = [250, 1_000, 2_000, 5_000, 10_000];

    private const double MinHouses = 100;
    private const double MaxHouses = 10_000;
    private const double HousesStep = 100;

    private const int ChartYears = 10;

    private readonly UserReactive<Assumptions> _assumptions = new(new Assumptions());
    private readonly UserReactive<Metric> _metric = new(Metric.Hours);

    // Bumped to replay the grid animation: it rides in the grid's key, so a new value
    // remounts the squares and the CSS timeline starts over.
    private readonly UserReactive<int> _run = new(0);

    private readonly UserReactive<bool> _showAssumptions = new(false);
    private readonly UserReactive<string> _audience = new(Audiences[0].Value);
    private readonly UserReactive<Pitch?> _pitch = new(null);
    private readonly UserReactive<bool> _pitchBusy = new(false);
    private readonly UserReactive<string?> _pitchError = new(null);

    private Assumptions A => _assumptions.Value;

    private Outcome Current => Outcome.From(_assumptions.Value);

    public async Task Main()
    {
        UI.Root([Page.Default], content: view =>
        {
            view.Column(["h-screen w-full bg-background text-foreground"], content: page =>
            {
                RenderTopBar(page);

                page.Box(["flex-1 min-h-0"], content: body =>
                {
                    body.ScrollArea(["h-full w-full"], viewportStyle: ["px-5 sm:px-8 py-8 sm:py-12"], content: scroll =>
                    {
                        scroll.Column(["w-full max-w-6xl mx-auto gap-10 sm:gap-14"], content: content =>
                        {
                            var outcome = Current;

                            RenderHero(content, outcome);
                            RenderStatCards(content, outcome);
                            RenderGridPanel(content, outcome);

                            // Directly under the squares: when the chatbot moves a dial they
                            // replay, and the motion is the answer the reader came for.
                            RenderChatPanel(content, outcome);

                            RenderCharts(content, outcome);
                            RenderPitchPanel(content, outcome);
                            RenderAssumptionsPanel(content, outcome);
                            RenderFooter(content);
                        });
                    });
                });
            });
        });
    }

    private void RenderTopBar(UIView view)
    {
        var here = app.ReactiveGlobalState.GetHumanClients().Count();

        view.Row(["shrink-0 items-center justify-between gap-4 px-5 sm:px-8 py-4 border-b border-primary bg-card/60"], content: bar =>
        {
            bar.Row(["items-center gap-3"], content: brand =>
            {
                // The wordmark doubles as the product's own diagram: three battens, one module.
                brand.Row(["items-end gap-[3px]"], content: mark =>
                {
                    mark.Box(["w-[5px] h-5 rounded-[1px] bg-brand-solid"]);
                    mark.Box(["w-[5px] h-7 rounded-[1px] bg-brand-solid"]);
                    mark.Box(["w-[5px] h-4 rounded-[1px] bg-brand-solid"]);
                });

                brand.Text(["font-heading uppercase text-2xl leading-none tracking-wide"], text: "Flext");
                brand.Text([Text.Caption, "hidden sm:block text-muted-foreground border-l border-primary pl-3"],
                    text: "Savings calculator");
            });

            bar.Row(["items-center gap-2"], content: row =>
            {
                row.Box(["w-2 h-2 rounded-full bg-brand-solid"]);
                row.Text([Text.Caption, "text-muted-foreground"], text: $"{here} here now");
            });
        });
    }

    private void RenderHero(UIView view, Outcome outcome)
    {
        view.Column(["gap-7"], content: hero =>
        {
            hero.Column(["gap-4"], content: head =>
            {
                head.Text([Text.Caption, "uppercase tracking-[0.2em] text-brand-secondary font-semibold"],
                    text: "Patent pending · Roof battens, factory-made");

                head.Text(["font-heading uppercase leading-[0.88] text-[13vw] sm:text-7xl lg:text-8xl max-w-4xl",
                        "motion-[0:opacity-0_translate-y-[14px],100:opacity-100_translate-y-0]",
                        "motion-duration-700ms motion-fill-both motion-ease-ease-out"],
                    text: "Roofs should be built in a factory.");

                head.Text([Text.Lg, "text-muted-foreground max-w-2xl leading-relaxed"],
                    text: "Nordic houses are modular now — except the battens, still nailed down by hand on a roof in the rain. "
                        + "Set your volume and watch what moving that one layer indoors takes off the table.");
            });

            RenderVolumeControl(hero, outcome);
        });
    }

    private void RenderVolumeControl(UIView view, Outcome outcome)
    {
        var houses = A.HousesPerYear;

        var metric = _metric.Value;
        var year = outcome.PerYear(metric);

        // Card tokens are plain class strings, so they go on a Column: the children are then
        // direct flex items and the gap actually reaches them, which it does not through
        // Card's own content slot.
        view.Column([Card.Elevated, "p-6 sm:p-8 gap-7"], content: card =>
        {
            card.Row(["flex-wrap items-center justify-between gap-x-10 gap-y-6"], content: top =>
            {
                top.Column(["gap-2"], content: left =>
                {
                    left.Text([Text.Caption, "uppercase tracking-[0.15em] text-muted-foreground font-semibold"],
                        text: "Roofs per year");

                    left.Row(["flex-wrap items-center gap-x-5 gap-y-3"], content: figure =>
                    {
                        figure.Row(["items-baseline gap-2"], content: number =>
                        {
                            number.Text(["font-heading text-6xl sm:text-7xl leading-none"], text: Units.Group(houses));
                            number.Text([Text.Body, "text-muted-foreground"], text: "roofs");
                        });

                        figure.Row(["items-center gap-2"], content: steppers =>
                        {
                            steppers.Button([Button.OutlineMd, "w-11 h-11 p-0 justify-center"],
                                text: "Fewer roofs per year",
                                onClick: async () => SetHouses(houses - HousesStep * 5),
                                content: icon => icon.Icon([Icon.Sm], name: "minus"));

                            steppers.Button([Button.OutlineMd, "w-11 h-11 p-0 justify-center"],
                                text: "More roofs per year",
                                onClick: async () => SetHouses(houses + HousesStep * 5),
                                content: icon => icon.Icon([Icon.Sm], name: "plus"));
                        });
                    });
                });

                // What the input buys, stated beside the input rather than only below it.
                top.Column(["gap-2 sm:items-end sm:text-right"], content: right =>
                {
                    // Green is for good news only: on inputs where Flext loses, this figure
                    // is a cost and has to look like one.
                    var tone = year.Adverse ? "text-error-700 dark:text-error-400" : FreedText;

                    right.Text([Text.Caption, "uppercase tracking-[0.15em] font-semibold", tone],
                        text: $"{Units.Short(metric)} {(year.Adverse ? "added" : "saved")} a year");

                    right.Text(["font-heading text-5xl sm:text-6xl leading-none", tone],
                        text: Units.Say(metric, year.Magnitude));
                });
            });

            card.Column(["gap-2.5"], content: track =>
            {
                track.Slider([Slider.Default],
                    value: [houses],
                    min: MinHouses,
                    max: MaxHouses,
                    step: HousesStep,
                    ariaLabel: "Roofs per year",
                    onValueChange: async values => SetHouses(values[0]));

                track.Row(["items-center justify-between"], content: ends =>
                {
                    ends.Text([Text.Caption, "font-mono text-muted-foreground"], text: Units.Group(MinHouses));
                    ends.Text([Text.Caption, "font-mono text-muted-foreground"], text: Units.Group(MaxHouses));
                });
            });

            card.Row(["flex-wrap items-center gap-2"], content: chips =>
            {
                chips.Text([Text.Caption, "text-muted-foreground mr-1"], text: "Jump to");

                foreach (var preset in Presets)
                {
                    var active = Math.Abs(preset - houses) < 0.5;

                    chips.Button([
                            active ? Button.PrimaryMd : Button.OutlineMd,
                            "rounded-full px-4 py-1.5 text-sm",
                        ],
                        text: Units.Group(preset),
                        key: $"preset-{preset}",
                        onClick: async () => SetHouses(preset));
                }
            });
        });
    }

    private void RenderStatCards(UIView view, Outcome outcome)
    {
        view.Box(["grid gap-4 grid-cols-1 md:grid-cols-3"], content: grid =>
        {
            foreach (var metric in AllMetrics)
            {
                var year = outcome.PerYear(metric);
                var active = _metric.Value == metric;

                grid.Box([
                        active ? Card.Elevated : Card.Subtle,
                        "flex flex-col p-6 gap-4 cursor-pointer border-2 text-left",
                        active ? "border-brand" : "border-transparent hover:border-brand/40",
                        State.Pressable,
                        Transition.Fast,
                    ],
                    key: $"stat-{metric}",
                    ariaLabel: $"Show {Units.Short(metric)}",
                    onClick: async () => Show(metric),
                    content: card =>
                    {
                        card.Row(["items-center justify-between gap-3"], content: head =>
                        {
                            head.Row(["items-center gap-2.5"], content: label =>
                            {
                                label.Box(["w-8 h-8 rounded-lg bg-brand-solid/10 flex items-center justify-center shrink-0"],
                                    content: tile => tile.Icon([Icon.Sm, "text-brand-secondary"], name: IconFor(metric)));

                                label.Text([Text.Caption, "uppercase tracking-[0.12em] font-semibold text-muted-foreground"],
                                    text: Units.Short(metric));
                            });

                            // A success badge, not the brand colour: orange means work still
                            // spent everywhere else on this page, so a gain needs its own signal.
                            // An error badge when the inputs make Flext the dearer way — a green
                            // badge over a loss is the one thing this page cannot afford.
                            head.Row([year.Adverse ? Badge.ErrorSm : Badge.SuccessSm, "shrink-0 whitespace-nowrap"],
                                content: badge => badge.Text(["text-xs font-semibold"],
                                    text: year.Adverse
                                        ? Units.Cost(metric, year.AdverseFraction)
                                        : Units.Gain(metric, year.SavedFraction)));
                        });

                        card.Column(["gap-1"], content: figure =>
                        {
                            figure.Text(["font-heading text-4xl leading-none"], text: Units.Say(metric, year.Magnitude));
                            figure.Text([Text.Caption, "text-muted-foreground"],
                                text: year.Adverse
                                    ? $"added per year · {Units.CostVerb(metric)}"
                                    : $"saved per year · {Units.Verb(metric)}");
                        });

                        // The same proportion the grid below draws, in one line, for a glance:
                        // the orange run is the work still done, the empty track what is freed.
                        card.Box(["h-1.5 w-full rounded-full bg-foreground/10 overflow-hidden"], content: track =>
                            track.Box([
                                $"h-full rounded-full bg-brand-solid w-[{(1 - year.SavedFraction) * 100:0.#}%]",
                                Transition.Normal,
                            ]));
                    });
            }
        });
    }

    private static string IconFor(Metric metric) => metric switch
    {
        Metric.Hours => "clock",
        Metric.Money => "banknote",
        _ => "leaf",
    };

    private void RenderFooter(UIView view)
    {
        view.Column(["gap-2 pt-2 pb-4 border-t border-primary"], content: footer =>
        {
            footer.Text([Text.Caption, "text-muted-foreground pt-4"],
                text: "Flext — patent-pending factory-made roof battens. Figures are a model built from Flext's "
                    + "stated 20–30 → 3–4 carpenter hours per roof and Swedish construction norms; every input above is editable.");
        });
    }

    private void SetHouses(double value)
    {
        // Held inside the range but NOT snapped to HousesStep: the slider already emits
        // step-aligned values, and snapping here rounded the 250 preset to 200 — a chip
        // that set a different number from the one printed on it.
        var clamped = Math.Clamp(Math.Round(value), MinHouses, MaxHouses);

        if (Math.Abs(clamped - A.HousesPerYear) < 0.5)
        {
            return;
        }

        _assumptions.Value = A with { HousesPerYear = clamped };
        Replay();
    }

    private void Show(Metric metric)
    {
        if (_metric.Value == metric)
        {
            Replay();
            return;
        }

        _metric.Value = metric;
        Replay();
    }

    /// A new run id remounts the grid, which is what restarts the CSS timeline.
    private void Replay() => _run.Value++;
}
