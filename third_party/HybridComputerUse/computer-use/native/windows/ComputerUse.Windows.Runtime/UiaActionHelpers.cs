using System.Diagnostics;
using System.Drawing;
using System.Text.Json.Nodes;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace ComputerUse.Windows.Runtime;

internal sealed partial class UiaProvider
{
    private static void EnsureTargetStillMatches(
        AutomationElement element,
        JsonObject? target,
        JsonObject? record)
    {
        var requestedName = CanonicalResolutionRecord.ExpectedProviderName(
            target,
            record,
            out var hasName);
        if ((hasName && !string.Equals(
                Safe(() => element.Name, string.Empty),
                requestedName,
                StringComparison.Ordinal)) ||
            !RoleMatchesTarget(element, target, record) ||
            !TargetPredicatesMatch(element, target, record))
        {
            throw new ProviderFailureException(
                "stale_element_ref",
                "precondition",
                "The exact target no longer matches its observed identity; no input was emitted.");
        }
    }

    private static bool RoleMatchesTarget(
        AutomationElement element,
        JsonObject? target,
        JsonObject? record)
    {
        var actual = CanonicalRole(element.ControlType);
        if (target?["providerRoleAliases"] is JsonArray aliases &&
            aliases.Count > 0)
        {
            return aliases
                .OfType<JsonValue>()
                .Any(value =>
                    value.TryGetValue<string>(out var alias) &&
                    string.Equals(
                        actual,
                        alias,
                        StringComparison.OrdinalIgnoreCase));
        }
        var providerRole = OptionalString(
            record,
            "controlType",
            string.Empty);
        if (!string.IsNullOrEmpty(providerRole))
        {
            return string.Equals(
                actual,
                providerRole,
                StringComparison.OrdinalIgnoreCase);
        }
        var modelRole = OptionalString(target, "role", string.Empty);
        return string.IsNullOrEmpty(modelRole) ||
            modelRole == "generic" ||
            string.Equals(
                actual,
                modelRole,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool TargetPredicatesMatch(
        AutomationElement element,
        JsonObject? target,
        JsonObject? record)
    {
        var predicates = target?["predicates"] as JsonArray ??
            record?["predicates"] as JsonArray;
        if (predicates is null)
        {
            return true;
        }
        foreach (var node in predicates.OfType<JsonObject>())
        {
            var kind = OptionalString(node, "kind", string.Empty);
            var value = OptionalString(node, "value", string.Empty);
            switch (kind)
            {
                case "state":
                    if (!ElementStateMatches(element, value))
                    {
                        return false;
                    }
                    break;
                case "desc":
                    if (!string.Equals(
                            Safe(() => element.HelpText, string.Empty),
                            value,
                            StringComparison.Ordinal))
                    {
                        return false;
                    }
                    break;
                case "placeholder":
                    return false;
                case "action":
                    if (!ElementActionMatches(element, value))
                    {
                        return false;
                    }
                    break;
                default:
                    return false;
            }
        }
        return true;
    }

    private static bool ElementStateMatches(
        AutomationElement element,
        string value)
    {
        return Normalize(value) switch
        {
            "enabled" => Safe(() => element.IsEnabled, false),
            "focused" => Safe(
                () => element.Properties.HasKeyboardFocus.ValueOrDefault,
                false),
            "checked" => Safe(
                () => element.Patterns.Toggle.PatternOrDefault is { } toggle &&
                    toggle.ToggleState.ValueOrDefault == ToggleState.On,
                false),
            "selected" => Safe(
                () => element.Patterns.SelectionItem.PatternOrDefault?
                    .IsSelected.Value ?? false,
                false),
            "expanded" => Safe(
                () => element.Patterns.ExpandCollapse.PatternOrDefault is { } expand &&
                    expand.ExpandCollapseState.Value ==
                        ExpandCollapseState.Expanded,
                false),
            "has-click-action" => ElementActionMatches(element, "invoke") ||
                element.Patterns.SelectionItem.PatternOrDefault is not null ||
                ElementActionMatches(element, "toggle"),
            _ => false,
        };
    }

    private static bool ElementActionMatches(
        AutomationElement element,
        string value)
    {
        return Normalize(value) switch
        {
            "invoke" => element.Patterns.Invoke.PatternOrDefault is not null,
            "toggle" => element.Patterns.Toggle.PatternOrDefault is not null,
            "select" => element.Patterns.SelectionItem.PatternOrDefault is not null ||
                element.Patterns.Text.PatternOrDefault is { } text &&
                text.SupportedTextSelection != SupportedTextSelection.None,
            "expand" or "collapse" =>
                element.Patterns.ExpandCollapse.PatternOrDefault is not null,
            "scrollintoview" =>
                element.Patterns.ScrollItem.PatternOrDefault is not null,
            "scroll" => element.Patterns.Scroll.PatternOrDefault is not null,
            _ => false,
        };
    }

    // Waits for the bound native window instance to retire. Leaving the
    // visible inventory is not enough: a window can hide itself or shrink to
    // zero size instead of closing.
    private IReadOnlyList<NativeDesktop.WindowRecord> WaitForWindowToDisappear(
        int processId,
        WindowBinding binding)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        do
        {
            if (BoundWindowLifecycle(binding) == WindowLifecycleStatus.Gone)
            {
                break;
            }
            Thread.Sleep(50);
        }
        while (DateTime.UtcNow < deadline);
        return NativeDesktop.ListWindows(processId);
    }

    private static bool TryInvoke(
        AutomationElement? element,
        Action<string, string> beforeInvoke)
    {
        if (element is null)
        {
            return false;
        }
        var invoke = element.Patterns.Invoke.PatternOrDefault;
        var selection = element.Patterns.SelectionItem.PatternOrDefault;
        var toggle = element.Patterns.Toggle.PatternOrDefault;
        switch (UiaClickRouting.Select(
            CanonicalRole(element.ControlType),
            invoke is not null,
            selection is not null,
            toggle is not null,
            selection is not null &&
                element.Patterns.LegacyIAccessible.PatternOrDefault is not null))
        {
            case UiaClickRoute.Invoke:
                beforeInvoke("uia.pattern.invoke", "provider_semantic");
                invoke!.Invoke();
                return true;
            case UiaClickRoute.SelectionItem:
                beforeInvoke(
                    "uia.pattern.selection_item",
                    "provider_semantic");
                selection!.Select();
                return true;
            case UiaClickRoute.Toggle:
                beforeInvoke("uia.pattern.toggle", "provider_semantic");
                toggle!.Toggle();
                return true;
            default:
                return false;
        }
    }

    private static bool TryScroll(
        AutomationElement? element,
        string direction,
        Action beforeScroll)
    {
        var scroll = element?.Patterns.Scroll.PatternOrDefault;
        if (scroll is null)
        {
            return false;
        }
        var horizontal = ScrollAmount.NoAmount;
        var vertical = ScrollAmount.NoAmount;
        switch (direction)
        {
            case "up":
                vertical = ScrollAmount.LargeDecrement;
                break;
            case "down":
                vertical = ScrollAmount.LargeIncrement;
                break;
            case "left":
                horizontal = ScrollAmount.LargeDecrement;
                break;
            case "right":
                horizontal = ScrollAmount.LargeIncrement;
                break;
        }
        beforeScroll();
        scroll.Scroll(horizontal, vertical);
        return true;
    }

    private void EnsureExactFocus(
        AutomationElement? element, Action verifyOwnership)
    {
        if (element is null)
        {
            throw new ProviderFailureException(
                "semantic_target_not_found",
                "precondition",
                "Keyboard input requires an exact target.");
        }
        ExactFocusSettlement.RequestAndVerify(
            () => element.Focus(),
            () =>
            {
                var focused = _automation.FocusedElement();
                return focused is not null && _automation.Compare(element, focused);
            },
            verifyOwnership: verifyOwnership);
    }

    private static WindowsVisibilityRead ReadVisibility(AutomationElement element)
    {
        return WindowsVisibilityRead.Read(() =>
        {
            var supported = element.Properties.IsOffscreen.TryGetValue(out var value);
            return (supported, value);
        });
    }

    private Point VerifiedPointerPoint(
        AutomationElement element,
        Process process,
        NativeDesktop.WindowRecord window,
        bool exactElementRequired = true)
    {
        using var cache = CacheRequest.ForceNoCache();
        try
        {
            return WindowsPointerPreflight.Verify(
                () => ReadVisibility(element).IsOffscreen,
                () => element.IsEnabled,
                () => element.BoundingRectangle,
                () => EnsureTargetLogicalWindow(element, process, window),
                point =>
                {
                    var native = NativeDesktop.WindowAtPoint(point);
                    return native != 0 &&
                        NativeDesktop.RootWindow(native) == NativeDesktop.RootWindow(window.Handle);
                },
                point =>
                {
                    var hit = _automation.FromPoint(point);
                    if (hit is null) { return false; }
                    EnsureTargetLogicalWindow(hit, process, window);
                    return !exactElementRequired || _automation.Compare(element, hit);
                });
        }
        catch (PointerPreflightException error)
        {
            throw new ProviderFailureException(error.Code, "precondition", error.Message);
        }
        catch (ProviderFailureException) { throw; }
        catch (Exception error)
        {
            Console.Error.WriteLine(
                $"[provider] Pointer preflight read failed: {error.GetType().Name} HRESULT=0x{error.HResult:X8}");
            throw new ProviderFailureException(
                "point_owner_unverified", "precondition",
                "Live visibility, geometry or point ownership could not be verified; no pointer input was emitted.");
        }
    }

    private static void RequirePointerEligibility(AutomationElement element)
    {
        using var cache = CacheRequest.ForceNoCache();
        try
        {
            _ = WindowsPointerPreflight.ReadTarget(
                () => ReadVisibility(element).IsOffscreen,
                () => element.IsEnabled,
                () => element.BoundingRectangle);
        }
        catch (PointerPreflightException error)
        {
            throw new ProviderFailureException(error.Code, "precondition", error.Message);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(
                $"[provider] Pointer eligibility read failed: {error.GetType().Name} HRESULT=0x{error.HResult:X8}");
            throw new ProviderFailureException(
                "pointer_precondition_unverified", "precondition",
                "Live visibility, enabled state or geometry could not be verified; no pointer input was emitted.");
        }
    }

    private static string CanonicalRole(ControlType type)
    {
        return type switch
        {
            ControlType.Window => "window",
            ControlType.Button => "button",
            ControlType.CheckBox => "checkbox",
            ControlType.RadioButton => "radio",
            ControlType.Edit or ControlType.Document => "textbox",
            ControlType.ComboBox => "combobox",
            ControlType.List => "list",
            ControlType.ListItem => "listitem",
            ControlType.Menu => "menu",
            ControlType.MenuItem => "menuitem",
            ControlType.Tab => "tablist",
            ControlType.TabItem => "tab",
            ControlType.Table or ControlType.DataGrid => "table",
            ControlType.DataItem => "cell",
            ControlType.Text => "text",
            ControlType.Tree => "tree",
            ControlType.TreeItem => "treeitem",
            ControlType.Slider => "slider",
            ControlType.Spinner => "spinbutton",
            ControlType.ProgressBar => "progressbar",
            ControlType.Image => "image",
            ControlType.Hyperlink => "link",
            _ => type.ToString().ToLowerInvariant(),
        };
    }

}
