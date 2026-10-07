// The panels below the hero: the square grid that carries the argument, the two charts
// that back it up, the AI pitch that writes it down, and the assumptions that let anyone
// take the whole thing apart.

public record Audience(string Value, string Label, string Brief);

public partial class MyHackApp
{
    // 600 squares: fine enough that the orange block reads as a quantity rather than a
    // bar chart, coarse enough that one square is still a square on a phone.
    private const int GridColumns = 40;
    private const int GridRows = 15;
    private const int GridCells = GridColumns * GridRows;

    // One diagonal wave of squares, then a second wave that paints the saved ones.
    // The step is set so the longest diagonal still lands inside a second.
    private const int WaveStepMs = 11;
    private const int AccentDelayMs = 700;

    private static readonly Audience[] Audiences =
    [
        new("investor", "An investor",
            "They fund machines, not ideas. Lead with the market and the unit economics, and name what the money buys."),
        new("factory", "A modular house factory",
            "They already build indoors. Lead with the hours and the line rate, and treat the higher unit price as the objection to answer."),
        new("builder", "A site manager",
            "They live with the weather and the crew. Lead with days off the critical path and work that no longer happens at height."),
        new("climate", "A municipality or climate officer",
            "They procure and report. Lead with embodied carbon, waste and the durability of the layer."),
    ];

    private void RenderGridPanel(UIView view, Outcome outcome)
    {
        var metric = _metric.Value;
        var year = outcome.PerYear(metric);
        var savedCells = (int)Math.Round(year.SavedFraction * GridCells);
        var perCell = year.Traditional / GridCells;

        view.Column(["gap-5"], content: panel =>
        {
            panel.Row(["flex-wrap items-end justify-between gap-5"], content: head =>
            {
                head.Column(["gap-2"], content: left =>
                {
                    left.Text(["font-heading uppercase leading-none text-3xl sm:text-4xl"], text: "One year of roofs");
                    left.Text([Text.Body, "text-muted-foreground max-w-xl"],
                        text: $"Every square is {Units.SayPrecise(metric, perCell)} of {Units.Long(metric).ToLowerInvariant()} "
                            + $"across {Units.Group(outcome.A.HousesPerYear)} roofs. The orange squares are the ones Flext takes away.");
                });

                RenderMetricSwitch(head);
            });

            panel.Column([Card.Elevated, "p-5 sm:p-8 gap-6"], content: card =>
            {
                card.Row(["flex-wrap items-end justify-between gap-4"], content: figures =>
                {
                    figures.Column(["gap-1"], content: saved =>
                    {
                        saved.Text([Text.Caption, "uppercase tracking-[0.15em] font-semibold text-brand-secondary"],
                            text: "Flext removes");

                        saved.Row(["items-baseline gap-2.5 flex-wrap"], content: row =>
                        {
                            row.Text(["font-heading text-5xl sm:text-6xl leading-none text-brand-secondary"],
                                text: Units.Say(metric, year.Saved));
                            row.Text([Text.Body, "text-muted-foreground pb-1"],
                                text: $"of {Units.Say(metric, year.Traditional)}");
                        });
                    });

                    figures.Button([Button.OutlineMd, "gap-2"],
                        text: "Replay",
                        onClick: async () => Replay(),
                        content: button =>
                        {
                            button.Icon([Icon.Sm], name: "rotate-ccw");
                            button.Text(["text-sm font-semibold"], text: "Replay");
                        });
                });

                RenderSquares(card, savedCells);

                card.Row(["flex-wrap items-center gap-x-6 gap-y-3 pt-1"], content: legend =>
                {
                    RenderLegendKey(legend, "bg-brand-solid",
                        $"Removed by Flext — {Units.Say(metric, year.Saved)}");

                    RenderLegendKey(legend, "bg-foreground/15",
                        $"Still needed — {Units.Say(metric, year.Flext)}");
                });
            });
        });
    }

    private void RenderMetricSwitch(UIView view)
    {
        view.Row(["items-center gap-1 p-1.5 rounded-full bg-foreground/[0.06] border border-primary"], content: row =>
        {
            foreach (var metric in AllMetrics)
            {
                var active = _metric.Value == metric;

                row.Button([
                        "rounded-full px-4 sm:px-5 py-2 text-sm font-semibold border-0 shadow-none",
                        active
                            ? "bg-brand-solid text-primary-on-brand"
                            : "bg-transparent text-muted-foreground hover:text-foreground",
                        Transition.Fast,
                    ],
                    text: Units.Short(metric),
                    key: $"switch-{metric}",
                    onClick: async () => Show(metric));
            }
        });
    }

    private static void RenderLegendKey(UIView view, string swatch, string label)
    {
        view.Row(["items-center gap-2.5"], content: key =>
        {
            key.Box([$"w-3.5 h-3.5 rounded-[2px] shrink-0 {swatch}"]);
            key.Text([Text.Caption, "text-muted-foreground"], text: label);
        });
    }

    /// The grid itself. Each square carries its own delay rather than leaning on
    /// `motion-per-children`, whose nth-child stagger runs out at 256 positions and would
    /// drop the tail of a larger grid into unison.
    private void RenderSquares(UIView view, int savedCells)
    {
        view.Box([
                "grid gap-[2px] sm:gap-[3px] w-full",
                // Half the columns on a phone, so a square never falls below a few pixels.
                $"grid-cols-[repeat({GridColumns / 2},minmax(0,1fr))]",
                $"sm:grid-cols-[repeat({GridColumns},minmax(0,1fr))]",
                // Rows and a fixed aspect pin the block's height to its width. Without this a
                // remount briefly empties the grid, the page shortens, and the reader's scroll
                // position is thrown to the top mid-animation.
                $"grid-rows-[repeat({GridCells / (GridColumns / 2)},minmax(0,1fr))]",
                $"sm:grid-rows-[repeat({GridRows},minmax(0,1fr))]",
                $"aspect-[{GridColumns / 2}/{GridCells / (GridColumns / 2)}]",
                $"sm:aspect-[{GridColumns}/{GridRows}]",
            ],
            // A fresh key remounts the squares, which is what restarts the CSS timeline.
            key: $"squares-{_run.Value}-{_metric.Value}",
            content: grid =>
            {
                for (var index = 0; index < GridCells; index++)
                {
                    var wave = (index / GridColumns + index % GridColumns) * WaveStepMs;
                    var saved = index < savedCells;

                    grid.Box([
                            // The container's rows give the cell its height; squareness comes
                            // from the grid's aspect matching its column and row counts.
                            "relative rounded-[2px] bg-foreground/15",
                            "motion-[0:opacity-0 scale-[0.4],100:opacity-100 scale-100]",
                            $"motion-duration-260ms motion-delay-[{wave}ms]",
                            // 200 squares is far too many to each take a GPU layer.
                            "motion-fill-both motion-ease-ease-out motion-no-promote",
                        ],
                        content: cell =>
                        {
                            if (!saved)
                            {
                                return;
                            }

                            cell.Box([
                                "absolute inset-0 rounded-[2px] bg-brand-solid",
                                "motion-[0:opacity-0 scale-[0.2],60:opacity-100 scale-[1.15],100:opacity-100 scale-100]",
                                $"motion-duration-420ms motion-delay-[{AccentDelayMs + wave}ms]",
                                "motion-fill-both motion-ease-ease-out motion-no-promote",
                            ]);
                        });
                }
            });
    }

    private void RenderCharts(UIView view, Outcome outcome)
    {
        var metric = _metric.Value;
        var perRoof = outcome.PerRoof(metric);
        var unit = Units.Axis(metric);

        view.Box(["grid gap-4 grid-cols-1 lg:grid-cols-2"], content: grid =>
        {
            grid.Column([Card.Default, "p-6 gap-4"], content: card =>
            {
                card.Column(["gap-1"], content: head =>
                {
                    head.Text([Text.BodyStrong], text: "One roof, both ways");
                    head.Text([Text.Caption, "text-muted-foreground"],
                        text: $"{Units.Long(metric)} for a single roof — {Units.Say(metric, perRoof.Saved)} lighter with Flext.");
                });

                card.BarChart(["h-64 w-full"],
                    data:
                    [
                        new Dictionary<string, object>
                        {
                            ["side"] = "On site today",
                            ["value"] = Math.Round(perRoof.Traditional, 1),
                        },
                        new Dictionary<string, object>
                        {
                            ["side"] = "With Flext",
                            ["value"] = Math.Round(perRoof.Flext, 1),
                        },
                    ],
                    keys: ["value"],
                    indexBy: "side",
                    valueUnit: unit,
                    padding: 0.45,
                    borderRadius: 4,
                    enableGridX: false,
                    key: $"bars-{metric}");
            });

            grid.Column([Card.Default, "p-6 gap-4"], content: card =>
            {
                card.Column(["gap-1"], content: head =>
                {
                    head.Text([Text.BodyStrong], text: $"Banked over {ChartYears} years");
                    head.Text([Text.Caption, "text-muted-foreground"],
                        text: $"Cumulative savings as volume compounds at {outcome.A.AnnualGrowthPercent:0.#}% a year — "
                            + $"{Units.Say(metric, outcome.CumulativeSaved(metric, ChartYears))} by year {ChartYears}.");
                });

                // Plotted in whatever unit keeps the axis legible: raw kronor would
                // label the ten-year total "532,000,000 SEK".
                var (divisor, axisUnit) = Units.AxisScale(metric, outcome.CumulativeSaved(metric, ChartYears));

                card.LineChart(["h-64 w-full"],
                    data:
                    [
                        new LineChartSeries
                        {
                            Id = $"Cumulative {Units.Short(metric).ToLowerInvariant()} saved",
                            Data = Enumerable.Range(1, ChartYears).Select(y => new LineChartPoint
                            {
                                X = $"Y{y}",
                                Y = Math.Round(outcome.CumulativeSaved(metric, y) / divisor, 2),
                            }),
                        },
                    ],
                    valueUnit: axisUnit,
                    curve: LineCurve.MonotoneX,
                    enableArea: true,
                    areaOpacity: 0.16,
                    enableGridX: false,
                    lineWidth: 3,
                    pointSize: 7,
                    key: $"line-{metric}");
            });
        });
    }

    private void RenderPitchPanel(UIView view, Outcome outcome)
    {
        var audience = Audiences.FirstOrDefault(a => a.Value == _audience.Value) ?? Audiences[0];

        view.Column(["gap-5"], content: panel =>
        {
            panel.Column(["gap-2"], content: head =>
            {
                head.Text(["font-heading uppercase leading-none text-3xl sm:text-4xl"], text: "Make the case");
                head.Text([Text.Body, "text-muted-foreground max-w-2xl"],
                    text: "The numbers above, written as an argument for the person you are about to show them to.");
            });

            panel.Column([Card.Elevated, "p-6 sm:p-8 gap-6"], content: card =>
            {
                card.Row(["flex-wrap items-end gap-4"], content: controls =>
                {
                    controls.Box(["w-full sm:w-80"], content: field =>
                        field.Select([Select.Default],
                            label: "Who is reading",
                            value: _audience.Value,
                            options: [.. Audiences.Select(a => new SelectOption(a.Value, a.Label))],
                            onValueChange: async value =>
                            {
                                _audience.Value = value;
                                _pitch.Value = null;
                                _pitchError.Value = null;
                            }));

                    controls.Button([Button.PrimaryMd, "gap-2"],
                        text: _pitch.Value is null ? "Write the pitch" : "Write it again",
                        disabled: _pitchBusy.Value,
                        onClick: async () => await WritePitchAsync(outcome, audience),
                        content: button =>
                        {
                            if (_pitchBusy.Value)
                            {
                                button.Spinner(size: SpinnerSize.Sm);
                            }
                            else
                            {
                                button.Icon([Icon.Sm], name: "sparkles");
                            }

                            button.Text(["text-sm font-semibold"],
                                text: _pitchBusy.Value
                                    ? "Writing…"
                                    : _pitch.Value is null ? "Write the pitch" : "Write it again");
                        });
                });

                if (_pitchError.Value is { } error)
                {
                    card.Alert(error, tone: SemanticTone.Error, onDismiss: async () => _pitchError.Value = null,
                        content: alert => alert.Button([Button.OutlineMd, "mt-3 self-start"],
                            text: "Try again",
                            onClick: async () => await WritePitchAsync(outcome, audience)));
                }

                if (_pitchBusy.Value && _pitch.Value is null)
                {
                    card.Column(["gap-3"], content: loading =>
                    {
                        loading.Skeleton(["h-8 w-3/4"]);
                        loading.Skeleton(["h-4 w-full"]);
                        loading.Skeleton(["h-4 w-5/6"]);
                        loading.Skeleton(["h-4 w-2/3"]);
                    });
                }
                else if (_pitch.Value is { } pitch)
                {
                    RenderPitch(card, pitch);
                }
                else if (_pitchError.Value is null)
                {
                    card.Column(["items-start gap-2 py-4 border-t border-primary"], content: empty =>
                    {
                        empty.Text([Text.Body, "text-muted-foreground max-w-xl"], text: audience.Brief);
                    });
                }
            });
        });
    }

    private static void RenderPitch(UIView view, Pitch pitch)
    {
        view.Column([
                "gap-5 pt-6 border-t border-primary",
                "motion-[0:opacity-0 translate-y-[10px],100:opacity-100 translate-y-0]",
                "motion-duration-400ms motion-fill-both motion-ease-ease-out",
            ],
            content: body =>
            {
                body.Text(["font-heading uppercase text-3xl sm:text-4xl leading-[0.95]"], text: pitch.Headline);

                body.Column(["gap-3"], content: points =>
                {
                    foreach (var (point, index) in pitch.Points.Select((p, i) => (p, i)))
                    {
                        points.Row(["items-start gap-3"], key: $"point-{index}", content: row =>
                        {
                            row.Box(["w-2 h-2 rounded-[1px] bg-brand-solid mt-2 shrink-0"]);
                            row.Text([Text.Body, "leading-relaxed flex-1"], text: point);
                        });
                    }
                });

                body.Text([Text.BodyStrong, "text-brand-secondary leading-relaxed"], text: pitch.Close);

                body.ActionButton([Button.GhostMd, "self-start gap-2 text-muted-foreground"],
                    action: ActionKind.CopyToClipboard,
                    options: new CopyToClipboardActionOptions
                    {
                        Text = $"{pitch.Headline}\n\n"
                            + string.Join("\n", pitch.Points.Select(p => $"• {p}"))
                            + $"\n\n{pitch.Close}",
                    },
                    content: button =>
                    {
                        button.Icon([Icon.Sm], name: "copy");
                        button.Text(["text-sm"], text: "Copy pitch");
                    });
            });
    }

    private async Task WritePitchAsync(Outcome outcome, Audience audience)
    {
        if (_pitchBusy.Value)
        {
            return;
        }

        using var _ = _pitchBusy.AsToken();
        _pitchError.Value = null;

        var a = outcome.A;
        var hours = outcome.PerYear(Metric.Hours);
        var money = outcome.PerYear(Metric.Money);
        var co2 = outcome.PerYear(Metric.Co2);

        // Every figure the model is allowed to use, already computed and already formatted.
        // It writes the argument; it never does the arithmetic.
        var facts = $"""
            Volume: {Units.Group(a.HousesPerYear)} roofs per year, growing {a.AnnualGrowthPercent:0.#}% a year.

            Per roof, the on-site batten layer:
            - Carpenter hours: {a.TraditionalHours:0.#} h today, {a.FlextHours:0.#} h with Flext.
            - Total cost: {Units.Say(Metric.Money, outcome.Money.Traditional)} today, {Units.Say(Metric.Money, outcome.Money.Flext)} with Flext.
            - The Flext module itself costs MORE than loose battens: {Units.Say(Metric.Money, a.FlextModulePrice)} against {Units.Say(Metric.Money, a.LooseMaterialCost)}. Collapsed labour is what pays for it.
            - Carbon: {Units.Say(Metric.Co2, outcome.Co2.Traditional)} today, {Units.Say(Metric.Co2, outcome.Co2.Flext)} with Flext.
            - Offcut waste: {a.SiteWastePercent:0.#}% cutting on a roof, {a.FactoryWastePercent:0.#}% on a factory jig.

            Across a year at this volume:
            - {Units.Say(Metric.Hours, hours.Saved)} of carpenter time never worked at height ({Units.Percent(hours.SavedFraction)} of the layer).
            - {Units.Say(Metric.Money, money.Saved)} saved ({Units.Percent(money.SavedFraction)}).
            - {Units.Say(Metric.Co2, co2.Saved)} of CO2e avoided ({Units.Percent(co2.SavedFraction)}).

            Over 10 years with that growth: {Units.Say(Metric.Money, outcome.CumulativeSaved(Metric.Money, ChartYears))} and {Units.Say(Metric.Hours, outcome.CumulativeSaved(Metric.Hours, ChartYears))}.
            """;

        try
        {
            var pitch = await Emerge.Run<Pitch>(LLMModel.Claude46Sonnet, pass =>
            {
                pass.SystemPrompt = """
                    You write for Flext, a patent-pending invention that lets roof battens be mass-produced
                    in a factory and delivered as one ready-made module. Today battens are the last part of
                    a Nordic modular roof still built by hand on site, in the weather, at height.

                    Write a short, concrete pitch from the figures you are given.
                    - Headline: under 10 words, a claim, no colon, no buzzwords. It will be set in a heavy
                      condensed display face, so make it land.
                    - Points: exactly 3, one sentence each, each built on a figure from the brief and each
                      making a different argument. Quote figures exactly as they are written in the brief.
                    - Close: one sentence naming what this reader should do or decide next.

                    Use only the figures in the brief. Invent no number, no customer, no date and no
                    comparison to a named competitor. Plain British English, no exclamation marks, no
                    "revolutionary", "game-changing", "unlock" or "leverage".
                    """;

                pass.Command = $"""
                    Reader: {audience.Label}. {audience.Brief}

                    The figures:
                    {facts}
                    """;
            });

            if (pitch.Points is null || pitch.Points.Length == 0)
            {
                throw new InvalidOperationException("The model returned a pitch with no points");
            }

            _pitch.Value = pitch;
        }
        catch (Exception e)
        {
            // The numbers are all still on screen, so a failed pitch only needs saying plainly.
            Log.Instance.Warning($"Pitch failed for client {ReactiveScope.ClientIdOrNull}: {e.GetType().Name}: {e.Message}");
            _pitchError.Value = "Couldn't write the pitch just now. The numbers above are unaffected.";
        }
    }

    private void RenderAssumptionsPanel(UIView view, Outcome outcome)
    {
        var open = _showAssumptions.Value;
        var a = outcome.A;

        view.Column([Card.Subtle, "p-6 sm:p-8 gap-6"], content: card =>
        {
            card.Row(["flex-wrap items-center justify-between gap-4"], content: head =>
            {
                head.Column(["gap-1"], content: left =>
                {
                    left.Text([Text.BodyStrong], text: "Every number above, and where it comes from");
                    left.Text([Text.Caption, "text-muted-foreground max-w-xl"],
                        text: "Change any of these and the whole page moves. Nothing here is hidden.");
                });

                head.Row(["items-center gap-2"], content: actions =>
                {
                    if (open)
                    {
                        actions.Button([Button.GhostMd, "gap-2"],
                            text: "Reset to defaults",
                            onClick: async () =>
                            {
                                _assumptions.Value = new Assumptions { HousesPerYear = a.HousesPerYear };
                                Replay();
                            });
                    }

                    actions.Button([Button.OutlineMd, "gap-2"],
                        text: open ? "Hide the assumptions" : "Show the assumptions",
                        onClick: async () => _showAssumptions.Value = !open,
                        content: button =>
                        {
                            button.Icon([Icon.Sm], name: open ? "chevron-up" : "sliders-horizontal");
                            button.Text(["text-sm font-semibold"], text: open ? "Hide" : "Show");
                        });
                });
            });

            if (!open)
            {
                return;
            }

            card.Box([
                    "grid gap-x-10 gap-y-7 grid-cols-1 md:grid-cols-2 pt-6 border-t border-primary",
                    "motion-[0:opacity-0,100:opacity-100] motion-duration-300ms motion-fill-both",
                ],
                content: grid =>
                {
                    RenderDial(grid, "On-site hours per roof, today", a.TraditionalHours, 8, 40, 0.5, "h",
                        "Flext's own figure is 20–30 carpenter hours for the batten layer.",
                        value => a with { TraditionalHours = value });

                    RenderDial(grid, "On-site hours per roof, with Flext", a.FlextHours, 1, 12, 0.5, "h",
                        "Lifting a finished module into place instead of cutting battens: 3–4 hours.",
                        value => a with { FlextHours = value });

                    RenderDial(grid, "Carpenter cost", a.HourlyRate, 300, 1_200, 10, "SEK/h",
                        "Fully loaded: wage plus social fees plus overhead, not the take-home rate.",
                        value => a with { HourlyRate = value });

                    RenderDial(grid, "Loose battens and fasteners", a.LooseMaterialCost, 1_000, 20_000, 250, "SEK",
                        "What the layer costs in materials today, before offcut waste is added.",
                        value => a with { LooseMaterialCost = value });

                    RenderDial(grid, "Flext module price", a.FlextModulePrice, 1_000, 30_000, 250, "SEK",
                        "Deliberately dearer than loose timber. This is the objection the hours have to answer.",
                        value => a with { FlextModulePrice = value });

                    RenderDial(grid, "Offcut waste, cutting on a roof", a.SiteWastePercent, 0, 30, 0.5, "%",
                        "Timber bought and then thrown away. Drives both cost and carbon.",
                        value => a with { SiteWastePercent = value });

                    RenderDial(grid, "Offcut waste, cut on a factory jig", a.FactoryWastePercent, 0, 15, 0.5, "%",
                        "A jig cuts to length, so the same roof needs less timber bought.",
                        value => a with { FactoryWastePercent = value });

                    RenderDial(grid, "Battens per roof", a.BattenMassKg, 50, 600, 10, "kg",
                        "Sawn softwood in the batten layer of one house.",
                        value => a with { BattenMassKg = value });

                    RenderDial(grid, "Carbon in sawn softwood", a.TimberCo2PerKg, 0.05, 1.0, 0.01, "kg CO₂e/kg",
                        "Cradle to gate: forestry, sawmill and transport to the merchant.",
                        value => a with { TimberCo2PerKg = value });

                    RenderDial(grid, "Crew travel per site day", a.TripKm, 0, 200, 5, "km",
                        "A van round trip for every working day the batten job no longer needs.",
                        value => a with { TripKm = value });

                    RenderDial(grid, "Van emissions", a.VanCo2PerKm, 0.05, 0.6, 0.01, "kg CO₂e/km",
                        "A loaded light commercial van in mixed Nordic driving.",
                        value => a with { VanCo2PerKm = value });

                    RenderDial(grid, "Volume growth", a.AnnualGrowthPercent, 0, 60, 1, "%/year",
                        $"How fast the line fills once the machine runs. Drives the {ChartYears}-year chart only.",
                        value => a with { AnnualGrowthPercent = value });
                });
        });
    }

    /// One assumption: what it is, what it is set to, why that number, and a slider plus
    /// steppers so it can be moved by touch or by keyboard.
    private void RenderDial(
        UIView view,
        string label,
        double value,
        double min,
        double max,
        double step,
        string suffix,
        string why,
        Func<double, Assumptions> apply)
    {
        void Set(double next)
        {
            var clamped = Math.Clamp(Math.Round(next / step) * step, min, max);

            if (Math.Abs(clamped - value) < step / 1000)
            {
                return;
            }

            _assumptions.Value = apply(clamped);
            Replay();
        }

        view.Column(["gap-2.5"], key: $"dial-{label}", content: dial =>
        {
            dial.Row(["items-baseline justify-between gap-3"], content: head =>
            {
                head.Text([Text.Caption, "font-semibold flex-1"], text: label);
                head.Text([Text.Caption, "font-mono text-brand-secondary font-semibold shrink-0"],
                    text: $"{value.ToString("0.##", CultureInfo.InvariantCulture)} {suffix}");
            });

            dial.Row(["items-center gap-3"], content: row =>
            {
                row.Button([Button.OutlineSm, "w-9 h-9 p-0 justify-center shrink-0"],
                    text: $"Decrease {label}",
                    onClick: async () => Set(value - step),
                    content: icon => icon.Icon([Icon.Sm], name: "minus"));

                row.Box(["flex-1"], content: track =>
                    track.Slider([Slider.Default],
                        value: [value],
                        min: min,
                        max: max,
                        step: step,
                        ariaLabel: label,
                        onValueChange: async values => Set(values[0])));

                row.Button([Button.OutlineSm, "w-9 h-9 p-0 justify-center shrink-0"],
                    text: $"Increase {label}",
                    onClick: async () => Set(value + step),
                    content: icon => icon.Icon([Icon.Sm], name: "plus"));
            });

            dial.Text([Text.Caption, "text-muted-foreground leading-snug"], text: why);
        });
    }
}
