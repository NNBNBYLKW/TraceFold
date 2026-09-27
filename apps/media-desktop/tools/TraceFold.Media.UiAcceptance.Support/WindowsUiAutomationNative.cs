using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace TraceFold.Media.UiAcceptance.Support;

/// <summary>
/// The managed UIA client, used strictly for reading. It never creates a pattern object that
/// changes state, never invokes, and never takes focus: <see cref="AutomationElement.TryGetCurrentPattern"/>
/// is only asked whether a pattern is available.
/// </summary>
public sealed class WindowsUiAutomationNative : IUiAutomationNative
{
    private static readonly Dictionary<string, AutomationPattern> Patterns =
        new(StringComparer.Ordinal)
        {
            ["Invoke"] = InvokePattern.Pattern,
            ["SelectionItem"] = SelectionItemPattern.Pattern,
            ["Selection"] = SelectionPattern.Pattern,
            ["ExpandCollapse"] = ExpandCollapsePattern.Pattern,
            ["Toggle"] = TogglePattern.Pattern,
            ["Value"] = ValuePattern.Pattern,
            ["Scroll"] = ScrollPattern.Pattern,
        };

    public UiElementFact? FindByAutomationId(
        IntPtr window,
        string automationId,
        IReadOnlyList<string> patternNames,
        string? descendantClassName,
        string? descendantName)
    {
        if (window == IntPtr.Zero || string.IsNullOrWhiteSpace(automationId))
        {
            return null;
        }

        try
        {
            AutomationElement root = AutomationElement.FromHandle(window);
            if (root is null)
            {
                return null;
            }

            var condition = new PropertyCondition(
                AutomationElement.AutomationIdProperty, automationId);
            AutomationElementCollection outerMatches = root.FindAll(TreeScope.Subtree, condition);
            if (outerMatches.Count != 1)
            {
                // Zero matches is a real answer; more than one is ambiguity and is reported as
                // such by the count, never resolved by picking one.
                return new UiElementFact(outerMatches.Count, 0, null, null, null, null, null,
                    null, null, null, null, []);
            }

            AutomationElement target = outerMatches[0];
            if (descendantClassName is null)
            {
                return Read(1, 1, target, patternNames);
            }

            AutomationElementCollection descendants = target.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ClassNameProperty, descendantClassName));
            var candidates = new List<AutomationElement>();
            foreach (AutomationElement candidate in descendants)
            {
                if (descendantName is null
                    || string.Equals(ReadName(candidate), descendantName, StringComparison.Ordinal))
                {
                    candidates.Add(candidate);
                }
            }

            if (candidates.Count != 1)
            {
                // A missing or ambiguous descendant is reported by its count; no fact and no
                // pattern answer is produced from a guess.
                return new UiElementFact(1, candidates.Count, null, null, null, null, null,
                    null, null, null, null, []);
            }

            return Read(1, 1, candidates[0], patternNames);
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (COMException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? ReadName(AutomationElement element)
    {
        try
        {
            return element.Current.Name;
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    private static UiElementFact Read(
        int outerMatchCount,
        int descendantMatchCount,
        AutomationElement element,
        IReadOnlyList<string> patternNames)
    {
        AutomationElement.AutomationElementInformation current;
        try
        {
            current = element.Current;
        }
        catch (ElementNotAvailableException)
        {
            return new UiElementFact(outerMatchCount, descendantMatchCount, null, null, null,
                null, null, null, null, null, null, []);
        }

        int? x = null;
        int? y = null;
        int? width = null;
        int? height = null;
        try
        {
            System.Windows.Rect bounds = current.BoundingRectangle;
            x = (int)Math.Round(bounds.X);
            y = (int)Math.Round(bounds.Y);
            width = (int)Math.Round(bounds.Width);
            height = (int)Math.Round(bounds.Height);
        }
        catch (ElementNotAvailableException)
        {
        }

        var facts = new List<UiPatternFact>(patternNames.Count);
        foreach (string name in patternNames)
        {
            if (!Patterns.TryGetValue(name, out AutomationPattern? pattern))
            {
                throw new ArgumentException("Unknown UIA pattern name: " + name, nameof(patternNames));
            }

            bool? available;
            try
            {
                available = element.TryGetCurrentPattern(pattern, out _);
            }
            catch (ElementNotAvailableException)
            {
                available = null;
            }
            catch (InvalidOperationException)
            {
                available = null;
            }
            catch (COMException)
            {
                available = null;
            }

            facts.Add(new UiPatternFact(name, available));
        }

        return new UiElementFact(
            outerMatchCount,
            descendantMatchCount,
            current.Name,
            StripControlTypePrefix(current.ControlType),
            current.ClassName,
            current.IsEnabled,
            current.IsOffscreen,
            x,
            y,
            width,
            height,
            facts);
    }

    /// <summary>
    /// The pinned driver reports bare control types (<c>ListItem</c>), so the probe does too.
    /// </summary>
    private static string StripControlTypePrefix(ControlType controlType)
    {
        const string prefix = "ControlType.";
        string name = controlType.ProgrammaticName;
        return name.StartsWith(prefix, StringComparison.Ordinal) ? name[prefix.Length..] : name;
    }
}
