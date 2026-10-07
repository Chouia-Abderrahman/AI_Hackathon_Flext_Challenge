// The panels below the hero: the square grid that carries the argument, and the
// assumptions that let anyone take the whole thing apart.

public partial class MyHackApp
{
    // 600 squares: fine enough that the orange block reads as a quantity rather than a
    // bar chart, coarse enough that one square is still a square on a phone.
    private const int GridColumns = 40;
    private const int GridRows = 15;
    private const int GridCells = GridColumns * GridRows;

    // Three diagonal waves: the empty block appears, every square fills orange as the work
    // the roof needs today, then the squares Flext frees drain back out, leaving only the
    // work that is still done. The step keeps the longest diagonal inside a second.
    private const int WaveStepMs = 11;
    private const int FillDelayMs = 700;

    // The drain is one track with the hold built into its keyframes, so a freed square and
    // a kept square start together and only part company once the block is full.
    private const int DrainDurationMs = 1500;

    /// A freed quantity is green wherever it is written, matching the squares it counts.
    /// Two steps, because one green cannot clear AA on both the paper and the dark card.
    private const string FreedText = "text-success-700 dark:text-success-400";

    private void RenderGridPanel(UIView view, Outcome outcome)
    {
        var metric = _metric.Value;
        var year = outcome.PerYear(metric);
        // Orange marks the work still done, so the kept squares are the Flext side of the
        // comparison and the rest of the block is the capacity handed back.
        var keptCells = GridCells - (int)Math.Round(year.SavedFraction * GridCells);
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
                            + $"across {Units.Group(outcome.A.HousesPerYear)} roofs. Watch the whole block fill with the work a roof "
                            + "takes today: everything that turns green is what Flext gives back, and the orange left behind is all the work still done.");
                });

                RenderMetricSwitch(head);
            });

            panel.Column([Card.Elevated, "p-5 sm:p-8 gap-6"], content: card =>
            {
                card.Row(["flex-wrap items-end justify-between gap-4"], content: figures =>
                {
                    figures.Column(["gap-1"], content: saved =>
                    {
                        // On inputs where Flext loses there is nothing freed to colour green,
                        // and the headline has to name the cost instead.
                        var tone = year.Adverse ? "text-error-700 dark:text-error-400" : FreedText;

                        saved.Text([Text.Caption, "uppercase tracking-[0.15em] font-semibold", tone],
                            text: year.Adverse ? Units.CostsVerb(metric) : Units.FreesVerb(metric));

                        saved.Row(["items-baseline gap-2.5 flex-wrap"], content: row =>
                        {
                            row.Text(["font-heading text-5xl sm:text-6xl leading-none", tone],
                                text: Units.Say(metric, year.Magnitude));
                            row.Text([Text.Body, "text-muted-foreground pb-1"],
                                text: year.Adverse
                                    ? $"on top of {Units.Say(metric, year.Traditional)}"
                                    : $"of {Units.Say(metric, year.Traditional)}");
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

                RenderSquares(card, keptCells);

                card.Row(["flex-wrap items-center gap-x-6 gap-y-3 pt-1"], content: legend =>
                {
                    RenderLegendKey(legend, "bg-brand-solid",
                        $"Still worked with Flext — {Units.Say(metric, year.Flext)}");

                    RenderLegendKey(legend, year.Adverse ? "bg-error-500" : "bg-success-500",
                        year.Adverse
                            ? $"{Units.AddedLabel(metric)} — {Units.Say(metric, year.Magnitude)}"
                            : $"{Units.FreedLabel(metric)} — {Units.Say(metric, year.Saved)}");
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
    private void RenderSquares(UIView view, int keptCells)
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

                    // The kept squares lead the block so the work that remains reads as one
                    // solid mass instead of orange confetti scattered through the grid.
                    var kept = index < keptCells;

                    grid.Box([
                            // The container's rows give the cell its height; squareness comes
                            // from the grid's aspect matching its column and row counts.
                            "relative rounded-[2px] bg-success-500",
                            "motion-[0:opacity-0_scale-[0.4],100:opacity-100_scale-100]",
                            $"motion-duration-260ms motion-delay-[{wave}ms]",
                            // 600 squares is far too many to each take a GPU layer.
                            "motion-fill-both motion-ease-ease-out motion-no-promote",
                        ],
                        content: cell =>
                        {
                            // Every square fills: the full block is the work a roof takes today.
                            // A kept square holds that fill; a freed one drains back out, and
                            // `motion-fill-both` leaves it resting on its last, empty keyframe.
                            cell.Box([
                                "absolute inset-0 rounded-[2px] bg-brand-solid",
                                kept
                                    ? "motion-[0:opacity-0_scale-[0.2],60:opacity-100_scale-[1.15],100:opacity-100_scale-100]"
                                    : "motion-[0:opacity-0_scale-[0.2],18:opacity-100_scale-[1.15],26:opacity-100_scale-100,62:opacity-100_scale-100,100:opacity-0_scale-[0.55]]",
                                kept
                                    ? "motion-duration-420ms"
                                    : $"motion-duration-{DrainDurationMs}ms",
                                $"motion-delay-[{FillDelayMs + wave}ms]",
                                "motion-fill-both motion-ease-ease-out motion-no-promote",
                            ]);
                        });
                }
            });
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
                    foreach (var dial in Dials)
                    {
                        RenderDial(grid, dial, a);
                    }
                });
        });
    }

    /// One assumption: what it is, what it is set to, why that number, and a slider plus
    /// steppers so it can be moved by touch or by keyboard.
    private void RenderDial(UIView view, Dial dial, Assumptions a)
    {
        var value = dial.Get(a);

        void Set(double next)
        {
            var clamped = dial.Clamp(next);

            if (Math.Abs(clamped - value) < dial.Step / 1000)
            {
                return;
            }

            _assumptions.Value = dial.Set(a, clamped);
            Replay();
        }

        view.Column(["gap-2.5"], key: $"dial-{dial.Key}", content: column =>
        {
            column.Row(["items-baseline justify-between gap-3"], content: head =>
            {
                head.Text([Text.Caption, "font-semibold flex-1"], text: dial.Label);
                head.Text([Text.Caption, "font-mono text-brand-secondary font-semibold shrink-0"],
                    text: dial.Say(value));
            });

            column.Row(["items-center gap-3"], content: row =>
            {
                row.Button([Button.OutlineSm, "w-9 h-9 p-0 justify-center shrink-0"],
                    text: $"Decrease {dial.Label}",
                    onClick: async () => Set(value - dial.Step),
                    content: icon => icon.Icon([Icon.Sm], name: "minus"));

                row.Box(["flex-1"], content: track =>
                    track.Slider([Slider.Default],
                        value: [value],
                        min: dial.Min,
                        max: dial.Max,
                        step: dial.Step,
                        ariaLabel: dial.Label,
                        onValueChange: async values => Set(values[0])));

                row.Button([Button.OutlineSm, "w-9 h-9 p-0 justify-center shrink-0"],
                    text: $"Increase {dial.Label}",
                    onClick: async () => Set(value + dial.Step),
                    content: icon => icon.Icon([Icon.Sm], name: "plus"));
            });

            column.Text([Text.Caption, "text-muted-foreground leading-snug"], text: dial.Why);
        });
    }
}
