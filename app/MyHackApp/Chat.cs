// The chatbot. It can read the figures and it can move the dials — and that is all it can
// do, because the arithmetic stays in Model.cs. Ask it "what if the module cost twice as
// much" and it does not write you a number: it moves the input, and the squares above
// re-animate as the answer.

/// One input the chatbot moved, written out so the reader sees the change in the answer
/// rather than having to go and find the slider it happened to.
public sealed record DialChange(string Label, string From, string To);

/// One line of the transcript. UndoTo carries the assumptions as they were before this turn
/// moved anything, so every change the chatbot makes has a way back.
public sealed record ChatTurn(
    string Role,
    string Text,
    Assumptions? UndoTo = null,
    IReadOnlyList<DialChange>? Changes = null,
    Assumptions? Result = null)
{
    public bool IsUser => Role == UserRole;

    public const string UserRole = "you";
    public const string ModelRole = "flext";
}

public partial class MyHackApp
{
    private const int MaxToolLoops = 8;

    // Four openings that show what the box is for without anyone having to guess.
    private static readonly string[] Openers =
    [
        "What if carpenters cost a fifth more?",
        "Show me the worst case for Flext",
        "What if the module cost twice as much?",
        "What happens at only 250 roofs a year?",
    ];

    private readonly UserReactiveList<ChatTurn> _turns = new();
    private readonly UserReactive<string> _draft = new("");
    private readonly UserReactive<string> _streaming = new("");
    private readonly UserReactive<bool> _chatBusy = new(false);
    private readonly UserReactive<bool> _chatWorking = new(false);

    // Turns whose changes have been taken back. The record of what the chatbot did stays
    // in the transcript — it is history — but the button that undid it must not sit there
    // afterwards offering to do it again.
    private readonly UserReactiveHashSet<int> _undone = new();

    // Per-user conversation memory. A UserReactive purely for per-user storage with no scope
    // plumbing — never read in a render lambda. One shared field would merge every visitor's
    // conversation into one.
    private readonly UserReactive<KernelContext> _chatCtx = new(new KernelContext());

    /// Every figure the model is allowed to quote, already computed and already formatted.
    /// Shared with the pitch so the two can never describe this page differently.
    private string Brief(Outcome outcome)
    {
        var a = outcome.A;
        var hours = outcome.PerYear(Metric.Hours);
        var money = outcome.PerYear(Metric.Money);
        var co2 = outcome.PerYear(Metric.Co2);

        return $"""
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

            Over {HorizonYears} years with that growth: {Units.Say(Metric.Money, outcome.CumulativeSaved(Metric.Money, HorizonYears))} and {Units.Say(Metric.Hours, outcome.CumulativeSaved(Metric.Hours, HorizonYears))}.
            """;
    }

    /// Every dial the chatbot may move, with the range it has to stay inside. Generated from
    /// the same table the sliders render, so the two cannot drift apart.
    private static string DialInventory(Assumptions a) =>
        string.Join("\n", Tunable.Select(d =>
            $"- {d.Key} — \"{d.Label}\" — now {d.Say(d.Get(a))}, allowed {d.Min:0.##} to {d.Max:0.##} in steps of {d.Step:0.##}. {d.Why}"));

    private async Task SendChatAsync(string input)
    {
        var text = input.Trim();

        if (string.IsNullOrEmpty(text) || _chatBusy.Value)
        {
            return;
        }

        _turns.Add(new ChatTurn(ChatTurn.UserRole, text));
        _draft.Value = "";
        _streaming.Value = "";

        using var _ = _chatBusy.AsToken();

        // The tools work on a local copy, not on the reactive. One write at the end means the
        // grid replays once, when the answer lands, and the undo point is the whole turn
        // rather than whichever dial the model happened to move last.
        var before = _assumptions.Value;
        var working = before;

        // Keyed by dial so a model that moves the same one twice is reported once, from
        // where it actually started to where it actually ended.
        var changes = new Dictionary<string, DialChange>();

        var reply = new StringBuilder();
        var afterTool = false;

        try
        {
            _chatCtx.Value = _chatCtx.Value.Add(new MessageBlock(MessageBlockRole.User, text));

            // Run<string> is what keeps the stream readable: any other T turns JSON mode on
            // and the bubble fills with raw JSON instead of the reply.
            await foreach (var ev in Emerge.Run<string>(LLMModel.Claude46Sonnet, _chatCtx.Value, pass =>
            {
                // The state goes in the system prompt, which is rebuilt every turn, rather
                // than relying on what the conversation remembers. The reader can move the
                // sliders or press Reset between two questions, so figures from an earlier
                // turn are not just old — they are wrong, and the model cannot tell.
                pass.SystemPrompt = ChatSystemPrompt(working);
                pass.MaxIterations = MaxToolLoops;

                pass.AddTool(Tool.Of(
                    "read_figures",
                    "The figures the page shows and every input with the range it must stay inside. For a "
                        + "question that needs NO change to the inputs. After a move_dial call you already "
                        + "have the recomputed page in that call's result and do not need this.",
                    () =>
                    {
                        var outcome = Outcome.From(working);

                        return $"""
                            The page AFTER your changes in this reply (not what the reader started with):

                            {Brief(outcome)}

                            The inputs:
                            {DialInventory(working)}
                            """;
                    }));

                pass.AddTool(Tool.Of(
                    "move_dial",
                    "Move one of the page's inputs and recompute everything from it. Names the dial by its key "
                        + "and gives the new value in the dial's own unit. Returns what the value actually "
                        + "became, which may differ from what you asked for if the dial has a narrower range. "
                        + "Call it once per dial.",
                    ([System.ComponentModel.Description("The dial key, e.g. hourly_rate")] string dial,
                     [System.ComponentModel.Description("The new value, in that dial's unit")] double value) =>
                    {
                        if (FindDial(dial) is not { } found)
                        {
                            return $"There is no dial called \"{dial}\". The dials are: "
                                + $"{string.Join(", ", Tunable.Select(d => d.Key))}.";
                        }

                        // One move per dial per reply, enforced rather than asked for. Left
                        // open, a model that re-reads the page after its own change treats
                        // that change as the reader's starting point and applies "a fifth
                        // more" again and again until the dial hits its limit.
                        if (changes.TryGetValue(found.Key, out var already))
                        {
                            return $"""
                                Stop and answer the reader now. You have already changed "{found.Label}" in
                                this reply and that change stands: the reader's page was at {already.From},
                                and you set it to {already.To}.

                                Do NOT move it again. Do NOT write that it "was already" at {already.To} —
                                {already.To} is your own change. The reader started at {already.From}.
                                """;
                        }

                        var old = found.Get(working);
                        var next = found.Clamp(value);

                        working = found.Set(working, next);

                        changes[found.Key] = new DialChange(found.Label, found.Say(old), found.Say(next));

                        // Say what actually happened, so a clamped request is never reported
                        // back to the reader as though it had been granted — and hand back the
                        // recomputed page in the same breath, so answering needs no second
                        // call and there is no later reading to mistake for the starting state.
                        var moveLine = Math.Abs(next - value) < found.Step / 1000
                            ? $"\"{found.Label}\" moved from {found.Say(old)} to {found.Say(next)}."
                            : $"\"{found.Label}\" moved from {found.Say(old)} to {found.Say(next)} — "
                                + $"{found.Say(value)} is outside the {found.Say(found.Min)} to {found.Say(found.Max)} "
                                + "this dial allows, so it stopped at the limit. Say so in your answer.";

                        return $"""
                            {moveLine}

                            The reader's page was at {found.Say(old)} before this reply. Describe
                            {found.Say(old)} as where they started and {found.Say(next)} as where you moved
                            it to — never the other way round, and never say {found.Say(next)} was already
                            set.

                            The page now reads:

                            {Brief(Outcome.From(working))}

                            You now have everything you need. Answer the reader.
                            """;
                    }));
            }))
            {
                switch (ev)
                {
                    case ModelText<string> token:
                    {
                        // Text streamed before a tool call is the model thinking out loud; the
                        // real answer is what it says once the tool has come back.
                        if (afterTool)
                        {
                            reply.Clear();
                            afterTool = false;
                        }

                        reply.Append(token.Text);
                        _streaming.Value = reply.ToString();
                        break;
                    }

                    case Retry<string>:
                    {
                        // The generation starts over; what streamed before is superseded.
                        reply.Clear();
                        _streaming.Value = "";
                        break;
                    }

                    case ToolCallPlanned<string>:
                    {
                        _chatWorking.Value = true;
                        break;
                    }

                    case ToolCallResult<string>:
                    {
                        _chatWorking.Value = false;
                        afterTool = true;
                        break;
                    }

                    case Completed<string> done:
                    {
                        _chatCtx.Value = done.Context;
                        Finish(done.Result ?? reply.ToString());
                        break;
                    }

                    case Stopped<string> stopped:
                    {
                        // Enumerating a run hands a stop back as an event, not an exception.
                        // Skipping this case would leave the bubble spinning for good.
                        _chatCtx.Value = stopped.Context;
                        Log.Instance.Warning($"Chat run stopped for client {ReactiveScope.ClientIdOrNull}: {stopped.Reason}");
                        Finish("I didn't finish that one — send it again.");
                        break;
                    }
                }
            }
        }
        catch (Exception e)
        {
            // The whole model is still on screen, so a failed reply only needs saying plainly —
            // never the exception, which names a provider and a socket to someone who wanted
            // an answer. The question goes back in the box so sending again is one press.
            Log.Instance.Warning($"Chat failed for client {ReactiveScope.ClientIdOrNull}: {e.GetType().Name}: {e.Message}");
            _chatWorking.Value = false;
            _streaming.Value = "";
            _draft.Value = text;
            _turns.Add(new ChatTurn(ChatTurn.ModelRole,
                "Couldn't answer that just now. The numbers above are unaffected — try sending it again."));
        }

        void Finish(string answer)
        {
            _chatWorking.Value = false;
            _streaming.Value = "";

            var said = string.IsNullOrWhiteSpace(answer)
                ? "I didn't finish that one — send it again."
                : answer.Trim();

            var moved = changes.Count > 0;

            // The resulting assumptions rather than the resulting figures: the turn stores
            // the inputs and the bubble computes from them through Model.cs at render time,
            // so the answer in the chat can never drift from the page above it.
            _turns.Add(new ChatTurn(
                ChatTurn.ModelRole,
                said,
                moved ? before : null,
                moved ? [.. changes.Values] : null,
                moved ? working : null));

            if (moved)
            {
                _assumptions.Value = working;

                // Open the assumptions so the dial it moved is visible: a change the reader
                // cannot see is a change they cannot check.
                _showAssumptions.Value = true;
                Replay();
            }
        }
    }

    /// What Flext is, in the memorandum's own terms. Without this the model knows only the
    /// dials, so a question about the product itself — how it attaches, why it resists rot,
    /// what it fits — has nothing to answer from and comes back as a lecture about the
    /// sliders. Every line here is from Develog's 2024 memorandum; nothing is inferred.
    private const string ProductFacts = """
        WHAT FLEXT IS
        A prefabricated roof-batten system from Develog AB (org. 559268-1588, develog.se),
        patent pending, winner of the SKAPA innovation prize and developed with RISE. Simple
        stiff battens are connected in the pull direction by straps of low-elasticity recycled
        plastic and delivered as a bundle. It is built in line with BBR and current industry
        technique, so a roofer recognises the traditional method and reads Flext's advantages
        as a straight bonus.

        TODAY'S METHOD, AND WHY IT COSTS
        Batten installation is one of the larger time-consuming tasks in erecting a building,
        worst on the roof. It has never been streamlined: many repetitive steps and a great
        deal of material handling, costly in both man-hours and project time, and it carries
        large scaffolding and safety overheads the contractor typically rents.

        THE THREE STEPS
        1. Place — the bundle is laid against the ridge and the first bearing batten fastened.
        2. Pull out — the rest of the bundle is drawn toward the eave until the straps go
           taut, which sets the correct cc dimension across the whole roof in one operation.
           No loose battens need installing first; the blocking wedges replace that task.
        3. Attach — fasten the bearing battens with the pre-selected screws.

        ROT, AND THE LONGER LIFE
        The traditional strolath is replaced by distance elements Develog calls "stropuckar",
        made of a material rot cannot attack. That removes the decay risk at one of the roof's
        real Achilles heels and reasonably extends a normal roof's service life by at least 10
        years, in many cases 20, as far as the ageing of the battens goes. Because the
        recycled material is cheap and eliminates the strolath cost entirely, it also pulls
        down total material cost.

        WHAT IT FITS
        Roofs and walls both. It can replace all types of battens — metal, tile or concrete
        roofs, and every variant of facade cladding installed on battens — in all cc and
        lumber dimensions in use today. Fully customised bundles can be prefabricated to a
        surface's design, with cutouts for chimneys, valleys, dormers, windows and door
        openings. Bundles run 2400 mm to 3600 mm, a format giving uniform timber quality and a
        workable balance of weight and handling; fall length follows the customer's spec.

        THE MEMORANDUM'S OWN FIGURES, for one standard 144 m2 roof, in SEK
        Traditional total 31 480 against Flext total 17 147, made up of:
        material 5 611 against 8 608 — Flext's material is dearer, and the saving is elsewhere;
        installation time 13 750 against 2 063; project overheads 11 719 against 1 758;
        shipping and packaging 400 against 2 800; the Flext machine 1 918, which the
        traditional method has no equivalent of.
        Time studies show up to 85% less installation time on a normal detached house roof,
        85% fewer maintenance days, and up to 1.5 fewer project days per standard roof.
        These are the memorandum's fixed figures for its own reference roof. They are NOT the
        page below, which models a different question and whose inputs the reader can move.
        Never mix the two in one sentence without saying which is which.
        """;

    private string ChatSystemPrompt(Assumptions a) => $"""
        You answer questions about Flext, a patent-pending invention that lets roof battens be
        mass-produced in a factory and delivered as one ready-made module. Today battens are
        the last part of a Nordic modular roof still built by hand on site, in the weather, at
        height. In front of the reader is a live savings model comparing the batten layer
        built the old way against built with Flext, across hours, cost and carbon.

        NOT EVERY QUESTION IS A WHAT-IF. Three kinds arrive, and reaching for a dial when the
        reader did not ask for one is itself a wrong answer:
        - About the product — how it is fitted, why it resists rot, what roofs it suits, who
          makes it. Answer from WHAT FLEXT IS below. No tool call is needed.
        - About the figures on screen — call read_figures and answer.
        - A what-if that needs an input moved — call move_dial, as described below.
        When the reader asks whether something is good, big, worth it or surprising, they want
        your judgement. Give it in a sentence, grounded in a figure already on the page or in
        the memorandum. Do not answer a question of that shape by reporting that nothing
        changed, and do not query the wording of what they asked — a reader saying "that's a
        good improvement?" is asking you to agree or disagree, not to re-measure anything.

        {ProductFacts}

        You never do the arithmetic. The page does it.
        - To answer a what-if, call move_dial for each input the question depends on. Each
          call hands back the recomputed page, so one round of calls is all you need — then
          answer. Quote figures only as the tools write them.
        - Use read_figures only for a question that needs no change at all.
        - Call move_dial AT MOST ONCE for any one input per answer. A relative instruction
          — a fifth more, twice as much, half — is relative to the value in "The page as it
          stands" below, never to a value you have already changed in this reply. Moving the
          same dial again because read_figures now shows your own new value walks it to its
          limit and answers a question nobody asked.
        - The state below is the page BEFORE anything you do in this reply, and a move_dial
          result names the value its dial held before you touched it. Those are the only
          values you may call what the reader started with. Never describe a value you set
          yourself as one that was already there.
        - Never calculate, estimate or extrapolate a figure yourself. Every figure you state
          must be one a tool returned to you, or one written verbatim in the memorandum facts
          above — those are fixed and quotable, but they describe the memorandum's own 144 m2
          reference roof, so say so when you use them and never pass one off as the page's.
          If you need a number and have neither, say you cannot get it.
        - Write every figure exactly as the tool wrote it, down to the separators and the
          unit. The page is written the same way, and a reader checking your answer against
          it should find the same characters.
        - Moving a dial changes the page the reader is looking at, so move the fewest dials
          that honestly answer the question, and always name which you moved and what to.
        - A question needing no change to the inputs just needs read_figures.

        Invent no number, no customer, no date and no comparison to a named competitor.

        Answer in plain English, in common words — two or three sentences for a figure or a
        what-if, up to five for a question about the product that genuinely needs them. No
        exclamation marks, no bullet lists, no headings, and never the words "revolutionary",
        "game-changing", "unlock" or "leverage". You may say when the figures do not favour
        Flext: a model a reader cannot trust is worth nothing. If you are asked something the
        memorandum and the page between them do not answer — a price for a specific job, a
        delivery date, a reference customer — say plainly that you do not have it.

        The page as it stands right now — this, not anything said earlier in the
        conversation, is what the reader is looking at. They can move the sliders
        themselves between two questions, so treat every figure from an earlier turn as
        out of date:

        {Brief(Outcome.From(a))}

        The inputs you can move:
        {DialInventory(a)}
        """;

    private void RenderChatPanel(UIView view, Outcome outcome)
    {
        var busy = _chatBusy.Value;

        view.Column(["gap-5"], content: panel =>
        {
            panel.Row(["flex-wrap items-end justify-between gap-4"], content: head =>
            {
                head.Column(["gap-2"], content: left =>
                {
                    left.Text(["font-heading uppercase text-3xl sm:text-4xl leading-none"], text: "Ask it anything");
                    left.Text([Text.Body, "text-muted-foreground max-w-2xl"],
                        text: "It can move the inputs above and run the numbers again. It cannot make a number up.");
                });

                // Handing the laptop to the next person needs a way to start over — and the
                // conversation is what the model reads as context, so clearing the transcript
                // has to clear that too or the next question inherits a stale page.
                if (_turns.Count > 0 && !busy)
                {
                    head.Button([Button.GhostMd, "gap-2 shrink-0"],
                        text: "Clear the conversation",
                        onClick: async () =>
                        {
                            _turns.Clear();
                            _undone.Clear();
                            _chatCtx.Value = new KernelContext();
                            _streaming.Value = "";
                        },
                        content: button =>
                        {
                            button.Icon([Icon.Sm], name: "eraser");
                            button.Text(["text-sm font-semibold"], text: "Clear");
                        });
                }
            });

            panel.Column([Card.Elevated, "p-6 sm:p-8 gap-5"], content: card =>
            {
                if (_turns.Count == 0 && !busy)
                {
                    RenderChatEmpty(card);
                }
                else
                {
                    // A DEFINITE height, not max-h: the scrolling viewport inside sizes itself
                    // to 100% of this box, and a percentage against a max-height-only parent
                    // does not constrain — the viewport grew to the full transcript, nothing
                    // overflowed inside it, and the outer simply clipped the rest away.
                    // "default" keeps the themed wiring that a bare style array would replace.
                    card.ChatLog(["default", "h-[26rem]"],
                        autoScrollKey: $"{_turns.Count}-{_streaming.Value.Length}-{_chatWorking.Value}",
                        content: log =>
                        {
                            log.Column(["gap-4"], content: turns =>
                            {
                                foreach (var (turn, index) in _turns.Select((t, i) => (t, i)))
                                {
                                    RenderTurn(turns, turn, index);
                                }

                                if (busy)
                                {
                                    RenderPending(turns);
                                }
                            });
                        });
                }

                card.Row(["items-center gap-3 pt-5 border-t border-primary"], content: composer =>
                {
                    composer.Box(["flex-1"], content: field =>
                        field.TextField([Input.Default, "w-full"],
                            bind: _draft,
                            // Not one of the openers: a placeholder repeating a chip gives the
                            // field and the chip the same handle, and neither can be picked out.
                            placeholder: "Ask what would happen if…",
                            ariaLabel: "Ask about these numbers",
                            disabled: busy,
                            onSubmit: async value => await SendChatAsync(value)));

                    composer.Button([Button.PrimaryMd, "gap-2 shrink-0"],
                        text: "Send",
                        disabled: busy,
                        onClick: async () => await SendChatAsync(_draft.Value),
                        content: button =>
                        {
                            if (busy)
                            {
                                button.Spinner(size: SpinnerSize.Sm);
                            }
                            else
                            {
                                button.Icon([Icon.Sm], name: "sparkles");
                            }

                            button.Text(["text-sm font-semibold"], text: busy ? "Thinking…" : "Send");
                        });
                });
            });
        });
    }

    /// Not a blank panel: the openers are what teach a first-time reader that this box moves
    /// the page rather than just talking about it.
    private void RenderChatEmpty(UIView view)
    {
        view.Column(["items-start gap-4 py-2"], content: empty =>
        {
            empty.Row(["items-center gap-3"], content: row =>
            {
                row.Box(["w-9 h-9 rounded-lg bg-brand-solid/10 items-center justify-center shrink-0"],
                    content: tile => tile.Icon([Icon.Sm, "text-brand-secondary"], name: "message-circle"));

                row.Text([Text.Body, "text-muted-foreground max-w-xl"],
                    text: "Put a doubt to it. It will move the inputs your question depends on, "
                        + "recompute the whole page, and tell you what changed.");
            });

            empty.Row(["flex-wrap gap-2"], content: chips =>
            {
                foreach (var opener in Openers)
                {
                    chips.Button([Button.OutlineSm],
                        key: $"opener-{opener}",
                        text: opener,
                        onClick: async () => await SendChatAsync(opener));
                }
            });
        });
    }

    private void RenderTurn(UIView view, ChatTurn turn, int index)
    {
        var mine = turn.IsUser;

        view.Column([
                "gap-2 max-w-[90%] sm:max-w-[80%]",
                mine ? "self-end items-end" : "self-start items-start",
            ],
            key: $"turn-{index}",
            content: column =>
            {
                column.Box([
                        "px-4 py-3 rounded-xl",
                        mine
                            ? "bg-brand-solid/10 border border-brand"
                            : "bg-foreground/[0.04] border border-primary",
                        "motion-[0:opacity-0_translate-y-[6px],100:opacity-100_translate-y-0]",
                        "motion-duration-300ms motion-fill-both motion-ease-ease-out",
                    ],
                    content: bubble =>
                    {
                        bubble.Text([Text.Body, "leading-relaxed whitespace-pre-wrap"], text: turn.Text);

                        // What it actually moved, written here rather than only reflected in a
                        // slider further up the page. Rendered from the dial table, never from
                        // the model's own words, so it cannot overstate what it did.
                        if (turn.Changes is { Count: > 0 } changes)
                        {
                            bubble.Column(["gap-2 mt-3 pt-3 border-t border-primary"], content: list =>
                            {
                                list.Text([Text.Caption, "uppercase tracking-[0.12em] font-semibold text-muted-foreground"],
                                    text: changes.Count == 1 ? "Input moved" : "Inputs moved");

                                foreach (var change in changes)
                                {
                                    list.Row(["flex-wrap items-baseline gap-x-2 gap-y-0.5"],
                                        key: $"change-{change.Label}",
                                        content: row =>
                                        {
                                            row.Text([Text.Caption, "font-semibold"], text: change.Label);
                                            row.Text([Text.Caption, "font-mono text-muted-foreground line-through"],
                                                text: change.From);
                                            row.Icon([Icon.Sm, "text-muted-foreground"], name: "arrow-right");
                                            row.Text([Text.Caption, "font-mono font-semibold text-brand-secondary"],
                                                text: change.To);
                                        });
                                }
                            });
                        }

                        // The answer itself, here rather than only in the cards further up:
                        // a reader should not have to scroll back to find out what the move
                        // they just asked for did.
                        if (turn.Result is { } result)
                        {
                            RenderTurnResult(bubble, Outcome.From(result));
                        }
                    });

                // Everything the chatbot changed, changeable back. One turn can move several
                // dials, so Reset to defaults is no substitute: it would throw away the
                // reader's own tuning too.
                if (turn.UndoTo is { } undoTo)
                {
                    if (_undone.Contains(index))
                    {
                        column.Row(["items-center gap-2"], content: row =>
                        {
                            row.Icon([Icon.Sm, "text-muted-foreground"], name: "rotate-ccw");
                            row.Text([Text.Caption, "text-muted-foreground"], text: "Undone");
                        });
                    }
                    else
                    {
                        column.Button([Button.OutlineSm, "gap-2"],
                            text: "Undo these changes",
                            onClick: async () =>
                            {
                                _assumptions.Value = undoTo;
                                _undone.Add(index);
                                Replay();
                            },
                            content: button =>
                            {
                                button.Icon([Icon.Sm], name: "rotate-ccw");
                                button.Text(["text-sm"], text: "Undo these changes");
                            });
                    }
                }
            });
    }

    /// What the page now says, computed through Model.cs from the turn's own inputs and
    /// written in the same words the cards above use — including when the figures stop
    /// favouring Flext.
    private static void RenderTurnResult(UIView view, Outcome outcome)
    {
        view.Column(["gap-2 mt-3 pt-3 border-t border-primary"], content: block =>
        {
            block.Text([Text.Caption, "uppercase tracking-[0.12em] font-semibold text-muted-foreground"],
                text: $"A year at {Units.Group(outcome.A.HousesPerYear)} roofs");

            foreach (var metric in AllMetrics)
            {
                var year = outcome.PerYear(metric);

                block.Row(["flex-wrap items-baseline gap-x-2 gap-y-0.5"], key: $"result-{metric}", content: row =>
                {
                    row.Text([Text.Caption, "font-semibold w-16 shrink-0"], text: Units.Short(metric));

                    row.Text([
                            "font-mono text-sm font-semibold",
                            year.Adverse ? "text-error-700 dark:text-error-400" : FreedText,
                        ],
                        text: Units.Say(metric, year.Magnitude));

                    row.Text([Text.Caption, "text-muted-foreground"],
                        text: year.Adverse
                            ? $"added · {Units.Cost(metric, year.AdverseFraction)}"
                            : $"saved · {Units.Gain(metric, year.SavedFraction)}");
                });
            }
        });
    }

    /// The reply as it arrives, in the bubble it will end up in, so nothing jumps when the
    /// stream finishes.
    private void RenderPending(UIView view)
    {
        var partial = _streaming.Value;

        view.Column(["gap-2 max-w-[90%] sm:max-w-[80%] self-start items-start"], key: "turn-pending", content: column =>
        {
            column.Box(["px-4 py-3 rounded-xl bg-foreground/[0.04] border border-primary"], content: bubble =>
            {
                if (partial.Length > 0)
                {
                    bubble.Text([Text.Body, "leading-relaxed whitespace-pre-wrap"], text: partial);
                }
                else
                {
                    bubble.Row(["items-center gap-2"], content: row =>
                    {
                        row.Spinner(size: SpinnerSize.Sm);
                        row.Text([Text.Caption, "text-muted-foreground"],
                            text: _chatWorking.Value ? "Moving the dials…" : "Reading the figures…");
                    });
                }
            });

            if (partial.Length > 0 && _chatWorking.Value)
            {
                column.Row(["items-center gap-2"], content: row =>
                {
                    row.Spinner(size: SpinnerSize.Sm);
                    row.Text([Text.Caption, "text-muted-foreground"], text: "Moving the dials…");
                });
            }
        });
    }
}
