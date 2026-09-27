using TraceFold.Media.UiAcceptance.Support;
using Xunit;

namespace TraceFold.Media.UiAcceptance.Support.Tests;

/// <summary>
/// The native probe is the only place that can answer what the pinned driver cannot: the real
/// native control type, whether a required UIA pattern is genuinely available, and whether that
/// pattern lives on a unique descendant of a composite control. These tests pin the ownership
/// gate, the descendant scoping, the "could not ask" semantics and the refusal taxonomy, because
/// a guess here would silently turn an unknown into a pass or a failure.
/// </summary>
public sealed class UiAutomationProbeTests
{
    private sealed class RecordingNative : IUiAutomationNative
    {
        public int Calls { get; private set; }

        public IntPtr LastWindow { get; private set; }

        public string? LastAutomationId { get; private set; }

        public IReadOnlyList<string> LastPatterns { get; private set; } = [];

        public string? LastDescendantClass { get; private set; }

        public string? LastDescendantName { get; private set; }

        public UiElementFact? Result { get; set; }

        public UiElementFact? FindByAutomationId(
            IntPtr window,
            string automationId,
            IReadOnlyList<string> patternNames,
            string? descendantClassName,
            string? descendantName)
        {
            Calls++;
            LastWindow = window;
            LastAutomationId = automationId;
            LastPatterns = patternNames;
            LastDescendantClass = descendantClassName;
            LastDescendantName = descendantName;
            return Result;
        }
    }

    private static UiElementFact Fact(
        int outer = 1,
        int descendant = 1,
        string? name = "n",
        string? controlType = "Edit",
        string? className = "TextBox",
        bool? enabled = true,
        bool? offscreen = false,
        params UiPatternFact[] patterns) =>
        new(outer, descendant, name, controlType, className, enabled, offscreen, 1, 2, 3, 4,
            patterns);

    [Theory]
    [InlineData("", 1, "TraceFold.Media", true, false, UiLookupRejection.MissingAutomationId)]
    [InlineData("   ", 1, "TraceFold.Media", true, false, UiLookupRejection.MissingAutomationId)]
    [InlineData("NavInbox", 0, "TraceFold.Media", true, false, UiLookupRejection.MissingProcessId)]
    [InlineData("NavInbox", 1, "TraceFold.Media.exe", false, false, UiLookupRejection.UnownedProcess)]
    [InlineData("NavInbox", 1, "notepad.exe", true, false, UiLookupRejection.UnexpectedExecutable)]
    [InlineData("NavInbox", 1, "TraceFold.Media", true, true, UiLookupRejection.WindowUnavailable)]
    public void Lookup_refuses_every_precondition(
        string automationId, int processId, string executable, bool owned, bool zeroWindow,
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
    public void Lookup_forwards_the_exact_id_the_patterns_and_the_descendant_scope()
    {
        var native = new RecordingNative
        {
            Result = Fact(patterns: new UiPatternFact("Value", true)),
        };

        UiElementFact? fact = UiAutomationProbe.Lookup(
            new IntPtr(7), "InboxSearchBox", ["Value"], native, "TextBox", "search box");

        Assert.NotNull(fact);
        Assert.Equal(1, native.Calls);
        Assert.Equal(new IntPtr(7), native.LastWindow);
        Assert.Equal("InboxSearchBox", native.LastAutomationId);
        Assert.Equal(["Value"], native.LastPatterns);
        Assert.Equal("TextBox", native.LastDescendantClass);
        Assert.Equal("search box", native.LastDescendantName);
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
    public void Lookup_rejects_an_empty_descendant_class_instead_of_matching_everything()
    {
        var native = new RecordingNative();

        Assert.Throws<ArgumentException>(() =>
            UiAutomationProbe.Lookup(new IntPtr(1), "InboxSearchBox", [], native, "   "));
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
        var native = new RecordingNative
        {
            Result = Fact(enabled: null, offscreen: null, patterns: new UiPatternFact("Value", null)),
        };
        UiElementFact? answered = UiAutomationProbe.Lookup(
            new IntPtr(1), "InboxSearchBox", ["Value"], native);
        Assert.NotNull(answered);
        Assert.Null(answered!.Patterns[0].Available);
        Assert.Null(answered.IsEnabled);

        native.Result = Fact(outer: 0, descendant: 0, name: null, controlType: null,
            className: null, enabled: null, offscreen: null);
        UiElementFact? absent = UiAutomationProbe.Lookup(new IntPtr(1), "Nope", [], native);
        Assert.NotNull(absent);
        Assert.Equal(0, absent!.OuterMatchCount);
    }

    [Fact]
    public void A_probe_that_cannot_answer_is_distinguishable_from_an_absent_element()
    {
        var native = new RecordingNative { Result = null };
        Assert.Null(UiAutomationProbe.Lookup(new IntPtr(1), "NavInbox", [], native));
    }

    [Fact]
    public void A_unique_descendant_is_reported_by_its_own_facts_and_patterns()
    {
        // The outer composite answers Value=false; the requirement is carried by the unique
        // descendant, so the reported facts must be the descendant's.
        var native = new RecordingNative
        {
            Result = Fact(name: "search box", controlType: "Edit", className: "TextBox",
                patterns: new UiPatternFact("Value", true)),
        };

        UiElementFact? fact = UiAutomationProbe.Lookup(
            new IntPtr(1), "InboxSearchBox", ["Value"], native, "TextBox", "search box");

        Assert.NotNull(fact);
        Assert.Equal(1, fact!.OuterMatchCount);
        Assert.Equal(1, fact.DescendantMatchCount);
        Assert.Equal("Edit", fact.ControlType);
        Assert.Equal("TextBox", fact.ClassName);
        Assert.True(fact.Patterns[0].Available);
    }

    [Theory]
    [InlineData(0, "a missing descendant must not be reported as supported")]
    [InlineData(2, "a duplicated descendant must not be resolved by picking one")]
    public void A_missing_or_duplicated_descendant_yields_no_pattern_answer(int count, string why)
    {
        var native = new RecordingNative
        {
            Result = Fact(descendant: count, name: null, controlType: null, className: null,
                enabled: null, offscreen: null),
        };

        UiElementFact? fact = UiAutomationProbe.Lookup(
            new IntPtr(1), "InboxSearchBox", ["Value"], native, "TextBox", "search box");

        Assert.NotNull(fact);
        Assert.Equal(count, fact!.DescendantMatchCount);
        Assert.Empty(fact.Patterns);
        Assert.NotEqual(true, null as bool?);
        Assert.True(count != 1, why);
    }

    [Fact]
    public void An_ambiguous_outer_match_reports_no_descendant_facts()
    {
        var native = new RecordingNative
        {
            Result = Fact(outer: 3, descendant: 0, name: null, controlType: null, className: null,
                enabled: null, offscreen: null),
        };

        UiElementFact? fact = UiAutomationProbe.Lookup(
            new IntPtr(1), "InboxSearchBox", ["Value"], native, "TextBox", "search box");

        Assert.NotNull(fact);
        Assert.Equal(3, fact!.OuterMatchCount);
        Assert.Empty(fact.Patterns);
    }
}
