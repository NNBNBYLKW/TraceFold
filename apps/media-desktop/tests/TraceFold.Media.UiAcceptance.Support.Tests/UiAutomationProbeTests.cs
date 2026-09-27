using TraceFold.Media.UiAcceptance.Support;
using Xunit;

namespace TraceFold.Media.UiAcceptance.Support.Tests;

/// <summary>
/// The native probe is the only place that can answer what the pinned driver cannot: the real
/// native control type and whether a required UIA pattern is genuinely available. These tests
/// pin the ownership gate, the "could not ask" semantics, and the refusal taxonomy, because a
/// guess here would silently turn an unknown into a pass or a failure.
/// </summary>
public sealed class UiAutomationProbeTests
{
    private sealed class RecordingNative : IUiAutomationNative
    {
        public int Calls { get; private set; }

        public IntPtr LastWindow { get; private set; }

        public string? LastAutomationId { get; private set; }

        public IReadOnlyList<string> LastPatterns { get; private set; } = [];

        public UiElementFact? Result { get; set; }

        public UiElementFact? FindByAutomationId(
            IntPtr window,
            string automationId,
            IReadOnlyList<string> patternNames)
        {
            Calls++;
            LastWindow = window;
            LastAutomationId = automationId;
            LastPatterns = patternNames;
            return Result;
        }
    }

    [Theory]
    [InlineData("", 1, "TraceFold.Media", true, false, UiLookupRejection.MissingAutomationId)]
    [InlineData("   ", 1, "TraceFold.Media", true, false, UiLookupRejection.MissingAutomationId)]
    [InlineData("NavInbox", 0, "TraceFold.Media", true, false, UiLookupRejection.MissingProcessId)]
    [InlineData("NavInbox", 1, "TraceFold.Media.exe", false, false, UiLookupRejection.UnownedProcess)]
    [InlineData("NavInbox", 1, "notepad.exe", true, false, UiLookupRejection.UnexpectedExecutable)]
    [InlineData("NavInbox", 1, "TraceFold.Media", true, true, UiLookupRejection.WindowUnavailable)]
    public void Lookup_refuses_every_precondition(
        string automationId,
        int processId,
        string executable,
        bool owned,
        bool zeroWindow,
        UiLookupRejection expected)
    {
        UiLookupRejection rejection = UiAutomationProbe.Validate(
            automationId, processId, executable, owned,
            zeroWindow ? IntPtr.Zero : new IntPtr(1));

        Assert.Equal(expected, rejection);
    }

    [Fact]
    public void Lookup_accepts_an_owned_product_window()
    {
        Assert.Equal(UiLookupRejection.None, UiAutomationProbe.Validate(
            "NavInbox", 42, "TraceFold.Media.exe", true, new IntPtr(1)));
    }

    [Fact]
    public void Lookup_forwards_an_exact_automation_id_and_the_requested_patterns()
    {
        var native = new RecordingNative
        {
            Result = new UiElementFact(1, "n", "ListItem", "c", true, false, 1, 2, 3, 4,
                [new UiPatternFact("SelectionItem", true)]),
        };

        UiElementFact? fact = UiAutomationProbe.Lookup(
            new IntPtr(7), "NavInbox", ["SelectionItem"], native);

        Assert.NotNull(fact);
        Assert.Equal(1, native.Calls);
        Assert.Equal(new IntPtr(7), native.LastWindow);
        Assert.Equal("NavInbox", native.LastAutomationId);
        Assert.Equal(["SelectionItem"], native.LastPatterns);
    }

    [Fact]
    public void Lookup_cannot_answer_without_a_window_or_an_id()
    {
        var native = new RecordingNative();

        Assert.Null(UiAutomationProbe.Lookup(IntPtr.Zero, "NavInbox", [], native));
        Assert.Null(UiAutomationProbe.Lookup(new IntPtr(1), "  ", [], native));
        Assert.Equal(0, native.Calls);
    }

    [Fact]
    public void Lookup_rejects_an_unknown_pattern_name_instead_of_answering_unsupported()
    {
        var native = new RecordingNative();

        Assert.Throws<ArgumentException>(() =>
            UiAutomationProbe.Lookup(new IntPtr(1), "NavInbox", ["Teleport"], native));
        Assert.Equal(0, native.Calls);
    }

    [Fact]
    public void Unknown_pattern_names_are_not_silently_treated_as_available_or_absent()
    {
        Assert.True(UiAutomationProbe.IsKnownPatternName("SelectionItem"));
        Assert.False(UiAutomationProbe.IsKnownPatternName("IsSelectionItemPatternAvailable"));
        Assert.False(UiAutomationProbe.IsKnownPatternName(""));
        Assert.Equal(
            ["Invoke", "SelectionItem", "Selection", "ExpandCollapse", "Toggle", "Value", "Scroll"],
            UiAutomationProbe.KnownPatternNames);
    }

    [Fact]
    public void An_unanswered_pattern_stays_null_and_a_zero_match_stays_a_real_answer()
    {
        // The probe must hand the caller the same ambiguity it observed: a pattern the query
        // could not establish is null, while "no such element" is a real count of zero.
        var native = new RecordingNative
        {
            Result = new UiElementFact(1, "n", "ListItem", "c", null, null, null, null, null,
                null, [new UiPatternFact("SelectionItem", null)]),
        };
        UiElementFact? answered = UiAutomationProbe.Lookup(
            new IntPtr(1), "NavInbox", ["SelectionItem"], native);
        Assert.NotNull(answered);
        Assert.Null(answered!.Patterns[0].Available);
        Assert.Null(answered.IsEnabled);

        native.Result = new UiElementFact(0, null, null, null, null, null, null, null, null,
            null, []);
        UiElementFact? absent = UiAutomationProbe.Lookup(new IntPtr(1), "Nope", [], native);
        Assert.NotNull(absent);
        Assert.Equal(0, absent!.MatchCount);
    }

    [Fact]
    public void A_probe_that_cannot_answer_is_distinguishable_from_an_absent_element()
    {
        var native = new RecordingNative { Result = null };
        Assert.Null(UiAutomationProbe.Lookup(new IntPtr(1), "NavInbox", [], native));
    }
}
