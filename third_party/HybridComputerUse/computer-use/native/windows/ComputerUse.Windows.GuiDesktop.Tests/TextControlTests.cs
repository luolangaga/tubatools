using System.Text.Json.Nodes;
using ComputerUse.Windows.Runtime;

internal static class TextControlTests
{
    internal static void Run()
    {
        var cases = 0;
        foreach (var name in new[] {
            "WindowsForms10.EDIT.app.0.105da89_r8_ad1",
            "WindowsForms10.RichEdit20W.app.0.105da89_r8_ad1",
            "WindowsForms10.RICHEDIT50W.app.0.2004eee_r6_ad2" })
        {
            NativeDesktop.Reset();
            NativeDesktop.TextControlClass = name;
            NativeDesktop.TypeText("中😀\té\r\nnext");
            var newline = name.Contains(".EDIT.") ? "\r\n" : "\r";
            Check(NativeDesktop.TextSelections.Select(item => item.Text).SequenceEqual(
                    new[] { "中😀\té", newline, "next" }) && NativeDesktop.TextMessages.Count == 0,
                "A WinForms native edit wrapper must preserve its native newline contract.");
            NativeDesktop.Reset();
            NativeDesktop.TextControlClass = name;
            NativeDesktop.TextControlReadOnly = true;
            Fails(() => NativeDesktop.TypeText("blocked"));
            Check(NativeDesktop.TextSelections.Count == 0 && NativeDesktop.TextMessages.Count == 0,
                "A read-only wrapped edit must reject before input.");
            NativeDesktop.TextControlReadOnly = false;
            NativeDesktop.TextControlMultiline = false;
            Fails(() => NativeDesktop.TypeText("prefix\nsuffix"));
            Check(NativeDesktop.TextSelections.Count == 0 && NativeDesktop.TextMessages.Count == 0,
                "A single-line wrapped edit must reject the entire multiline input.");
            cases += 3;
        }
        foreach (var name in new[] {
            "UnknownRichEdit20W", "WindowsForms10.RichEdit20W.app.0.bad_suffix",
            "WindowsForms10.RichEdit20W.app.0.105da89_r8_ad1.extra",
            "WindowsForms11.RichEdit20W.app.0.105da89_r8_ad1",
            "WindowsForms10.CustomRichEdit20W.app.0.105da89_r8_ad1" })
        {
            Check(!NativeDesktop.IsStandardTextControlClass(name),
                "A similar class name is not a native edit control contract.");
            cases++;
        }
        foreach (var name in new[] { "Edit", "richedit20w", "RICHEDIT50W", "RichEditD2D", "rIcHeDiTd2DpT" })
        {
            NativeDesktop.Reset();
            NativeDesktop.TextControlClass = name;
            var generation = NativeDesktop.InputGeneration;
            NativeDesktop.TypeText("中文\t😀e\u0301", 1);
            Check(NativeDesktop.TextSelections.Single().Text == "中文\t😀e\u0301" &&
                NativeDesktop.TextSelections[0].Window == 2 && NativeDesktop.TextMessages.Count == 0 &&
                NativeDesktop.Calls.Count == 0 && NativeDesktop.TextWakes.Count == 1 &&
                NativeDesktop.InputGeneration == generation + 1,
                $"Standard control {name} must insert the full Unicode segment once at the pinned focus.");
            cases++;
        }
        foreach (var name in new[] { "RichEdit", "RichEdit20A", "RichEditD2DPT-custom", "Chrome_RenderWidgetHostHWND", "Unknown" })
        {
            NativeDesktop.Reset();
            NativeDesktop.TextControlClass = name;
            NativeDesktop.TypeText("AB");
            Check(NativeDesktop.TextSelections.Count == 0 && NativeDesktop.TextMessages.Count == 2,
                $"Unrecognized class {name} must retain character messages.");
            cases++;
        }

        NativeDesktop.Reset();
        NativeDesktop.TextControlClass = "Edit";
        NativeDesktop.TypeText("中😀\te\u0301\r\n\n日本\r한국\n");
        Check(NativeDesktop.TextEmissionOrder.SequenceEqual(new[] {
            "text:中😀\te\u0301", "text:\r\n", "text:\r\n", "text:日本",
            "text:\r\n", "text:한국", "text:\r\n" }),
            "Mixed Unicode, literal tabs, CRLF, empty lines and trailing newline must preserve order.");
        Check(NativeDesktop.TextWakes.Count == 1 && NativeDesktop.Calls.Count == 0,
            "Segmented input must wake once without queued keyboard input.");
        cases++;

        // A receiver can handle Enter keydowns and standalone CR characters.
        // Synchronous sends do not put the later character in its queue, so
        // it cannot pair those two messages as one physical keystroke.
        foreach (var name in new[] { "Edit", "richedit20w", "RICHEDIT50W", "RichEditD2D", "rIcHeDiTd2DpT", "Unknown" })
        {
            NativeDesktop.Reset();
            NativeDesktop.TextControlClass = name;
            var lines = new List<string> { "" };
            NativeDesktop.OnTextSelection = _ =>
            {
                var inserted = NativeDesktop.TextSelections[^1].Text.Split(
                    name == "Edit" ? "\r\n" : "\r", StringSplitOptions.None);
                lines[^1] += inserted[0];
                lines.AddRange(inserted.Skip(1));
                return true;
            };
            NativeDesktop.OnTextMessage = _ =>
            {
                var message = NativeDesktop.TextMessages[^1];
                // RichEdit ignores standalone CR character messages.
                if (name != "Edit" && name != "Unknown" && message.Message == 0x102 && message.Value == 0x0D)
                    return true;
                if ((message.Message == 0x100 || message.Message == 0x102) && message.Value == 0x0D)
                    lines.Add("");
                else if (message.Message == 0x102)
                    lines[^1] += (char)message.Value;
                return true;
            };
            NativeDesktop.TypeText("\nA\n\nB\rC\r\nD\n");
            Check(lines.SequenceEqual(new[] { "", "A", "", "B", "C", "D", "" }) &&
                NativeDesktop.TextWakes.Count == 1 && NativeDesktop.Calls.Count == 0,
                $"{name} must preserve each logical newline once, including empty and trailing lines.");
            cases++;
        }

        foreach (var name in new[] { "Edit", "RICHEDIT50W", "Unknown" })
        foreach (var newline in new[] { "\r", "\n", "\r\n" })
        foreach (var prefix in new[] { "", "😀" })
        {
            NativeDesktop.Reset();
            NativeDesktop.TextControlClass = name;
            NativeDesktop.OnTextMessage = _ => NativeDesktop.TextMessages[^1].Value != 0x0D;
            NativeDesktop.OnTextSelection = _ => !NativeDesktop.TextSelections[^1].Text.Contains('\r');
            var newlineFailure = Fails(() => DesktopProvider.ExecuteBatch(new JsonObject {
                ["actions"] = new JsonArray(
                    new JsonObject { ["type"] = "type", ["text"] = prefix + newline + "never" },
                    new JsonObject { ["type"] = "click", ["x"] = 5d, ["y"] = 5d }) }));
            var prefixMessages = prefix.Length == 0 ? 0 : name == "Unknown" ? 2 : 1;
            Check(newlineFailure.InputEventsEmitted == (prefixMessages > 0) && newlineFailure.MutationUnknown &&
                Evidence(newlineFailure, "source_utf16_index") == prefix.Length &&
                Evidence(newlineFailure, "source_utf16_count") == newline.Length &&
                Evidence(newlineFailure, "attempted_messages") == prefixMessages + 1 &&
                Evidence(newlineFailure, "returned_messages") == prefixMessages &&
                Evidence(newlineFailure, "unknown_message_index") == prefixMessages &&
                newlineFailure.ActionEvidence!["sequence_phase"]!.GetValue<string>() ==
                    (name == "Unknown" ? "enter_character" : "selection_newline") &&
                (name == "Unknown" ? NativeDesktop.TextMessages[^1].Value == 0x0D :
                    NativeDesktop.TextMessages.Count == 0 && NativeDesktop.TextSelections.Count == prefixMessages + 1) &&
                NativeDesktop.TextWakes.Count == 1 && NativeDesktop.Calls.Count == 0,
                "An unknown newline must retain its original UTF-16 span and stop the suffix and later actions.");
            cases++;
        }

        NativeDesktop.Reset();
        NativeDesktop.TextControlClass = "Edit";
        NativeDesktop.OnTextSelection = _ => false;
        var failure = Fails(() => NativeDesktop.TypeText("中😀\tline\r\nlater"));
        Check(!failure.InputEventsEmitted && failure.MutationUnknown &&
            Evidence(failure, "attempted_messages") == 1 && Evidence(failure, "returned_messages") == 0 &&
            Evidence(failure, "unknown_message_index") == 0 && Evidence(failure, "source_utf16_index") == 0 &&
            Evidence(failure, "source_utf16_count") == "中😀\tline".Length &&
            NativeDesktop.TextSelections.Count == 1 && NativeDesktop.TextMessages.Count == 0 &&
            NativeDesktop.TextWakes.Count == 1,
            "First unknown replacement must cover its full source segment, stop the suffix and never replay.");
        cases++;

        foreach (var name in new[] { "Edit", "RICHEDIT50W" })
        {
            NativeDesktop.Reset();
            NativeDesktop.TextControlClass = name;
            NativeDesktop.EnterScan = 0;
            NativeDesktop.TypeText("\r\nA\n");
            Check(NativeDesktop.TextSelections.Count == 3 && NativeDesktop.TextMessages.Count == 0,
                "Selection insertion must not require a keyboard scan code for literal newlines.");
            cases++;

            NativeDesktop.Reset();
            NativeDesktop.TextControlClass = name;
            NativeDesktop.TextControlReadOnly = true;
            var generation = NativeDesktop.InputGeneration;
            var readOnlyFailure = Fails(() => NativeDesktop.TypeText("prefix\r\nsuffix"));
            Check(!readOnlyFailure.InputEventsEmitted && !readOnlyFailure.MutationUnknown &&
                NativeDesktop.TextSelections.Count == 0 && NativeDesktop.TextMessages.Count == 0 &&
                NativeDesktop.TextWakes.Count == 0 && NativeDesktop.InputGeneration == generation,
                "Read-only multiline input must reject its entire prefix before any side effect.");
            cases++;

            foreach (var newline in new[] { "\r", "\n", "\r\n" })
            {
                NativeDesktop.Reset();
                NativeDesktop.TextControlClass = name;
                NativeDesktop.TextControlMultiline = false;
                generation = NativeDesktop.InputGeneration;
                var singleLineFailure = Fails(() => NativeDesktop.TypeText("prefix" + newline + "suffix"));
                Check(singleLineFailure.Phase == "preparation" &&
                    !singleLineFailure.InputEventsEmitted && !singleLineFailure.MutationUnknown &&
                    NativeDesktop.TextSelections.Count == 0 && NativeDesktop.TextMessages.Count == 0 &&
                    NativeDesktop.TextWakes.Count == 0 && NativeDesktop.InputGeneration == generation,
                    "Single-line controls must reject newlines before sending even a valid prefix.");
                cases++;
            }

            NativeDesktop.Reset();
            NativeDesktop.TextControlClass = name;
            NativeDesktop.TextControlMultiline = false;
            NativeDesktop.TypeText("ordinary 中文😀");
            Check(NativeDesktop.TextSelections.Single().Text == "ordinary 中文😀",
                "Single-line controls must still accept ordinary Unicode text.");
            cases++;

            NativeDesktop.Reset();
            NativeDesktop.TextControlClass = name;
            NativeDesktop.OnTextSelection = _ => { NativeDesktop.TextControlMultiline = false; return true; };
            var changedStyleFailure = Fails(() => NativeDesktop.TypeText("prefix\nsuffix"));
            Check(changedStyleFailure.InputEventsEmitted && changedStyleFailure.MutationUnknown &&
                NativeDesktop.TextSelections.Count == 1 && NativeDesktop.TextMessages.Count == 0 &&
                NativeDesktop.TextWakes.Count == 1,
                "A control becoming single-line must stop before the newline and preserve the emitted prefix.");
            cases++;
        }

        NativeDesktop.Reset();
        NativeDesktop.TextControlClass = "RICHEDIT50W";
        NativeDesktop.OnTextSelection = _ =>
        {
            NativeDesktop.OnReadTextControlMultiline = () => throw new ProviderFailureException(
                "desktop_input_failed", "preparation", "Control style unavailable.");
            return true;
        };
        var styleFailure = Fails(() => NativeDesktop.TypeText("prefix\nsuffix"));
        Check(styleFailure.Phase == "emission" && styleFailure.InputEventsEmitted && styleFailure.MutationUnknown &&
            NativeDesktop.TextSelections.Count == 1 && NativeDesktop.TextMessages.Count == 0,
            "Failed multiline style inspection must preserve prefix evidence and stop subsequent input.");
        cases++;

        NativeDesktop.Reset();
        NativeDesktop.TextControlClass = "Edit";
        NativeDesktop.OnTextSelection = count => count != 3;
        failure = Fails(() => DesktopProvider.ExecuteBatch(new JsonObject { ["actions"] = new JsonArray(
            new JsonObject { ["type"] = "type", ["text"] = "A\r\n中😀\tB\nnever" },
            new JsonObject { ["type"] = "click", ["x"] = 5d, ["y"] = 5d }) }));
        Check(failure.InputEventsEmitted && failure.MutationUnknown &&
            Evidence(failure, "attempted_messages") == 3 && Evidence(failure, "returned_messages") == 2 &&
            Evidence(failure, "unknown_message_index") == 2 && Evidence(failure, "source_utf16_index") == 3 &&
            Evidence(failure, "source_utf16_count") == "中😀\tB".Length &&
            NativeDesktop.TextSelections.Count == 3 && NativeDesktop.TextMessages.Count == 0 &&
            NativeDesktop.Calls.Count == 0,
            "GUI must retain message counts and the whole unknown segment, without emitting the later click.");
        cases++;

        NativeDesktop.Reset();
        NativeDesktop.TextControlClass = "Edit";
        NativeDesktop.TextControlReadOnly = true;
        var before = NativeDesktop.InputGeneration;
        failure = Fails(() => NativeDesktop.TypeText("blocked"));
        Check(failure.Phase == "preparation" && !failure.InputEventsEmitted && !failure.MutationUnknown &&
            NativeDesktop.TextSelections.Count == 0 && NativeDesktop.TextMessages.Count == 0 &&
            NativeDesktop.TextWakes.Count == 0 && NativeDesktop.InputGeneration == before,
            "A read-only standard control must fail without input, wake or cache invalidation.");
        cases++;

        NativeDesktop.Reset();
        NativeDesktop.TextControlClass = "Edit";
        NativeDesktop.OnTextSelection = _ => { NativeDesktop.TextControlReadOnly = true; return true; };
        failure = Fails(() => NativeDesktop.TypeText("first\nsecond"));
        Check(failure.InputEventsEmitted && NativeDesktop.TextSelections.Count == 1 &&
            NativeDesktop.TextMessages.Count == 0 && NativeDesktop.TextWakes.Count == 1,
            "A control becoming read-only must stop before the next message and preserve prior input.");
        cases++;

        NativeDesktop.Reset();
        NativeDesktop.TextControlClass = "Edit";
        NativeDesktop.OnTextSelection = _ =>
        {
            NativeDesktop.OnReadTextControlReadOnly = () => throw new ProviderFailureException(
                "desktop_input_failed", "preparation", "Control style unavailable.");
            return true;
        };
        failure = Fails(() => NativeDesktop.TypeText("first\nsecond"));
        Check(failure.Phase == "emission" && failure.InputEventsEmitted && failure.MutationUnknown &&
            Evidence(failure, "returned_messages") == 1 && NativeDesktop.TextSelections.Count == 1 &&
            NativeDesktop.TextMessages.Count == 0 && NativeDesktop.TextWakes.Count == 1,
            "A later style-read failure must preserve the emitted prefix and stop further input.");
        cases++;

        NativeDesktop.Reset();
        NativeDesktop.TextControlClass = "Edit";
        var original = NativeDesktop.Focus!.Value;
        NativeDesktop.OnTextSelection = _ => { NativeDesktop.Focus = original with { Focus = 99 }; return true; };
        failure = Fails(() => NativeDesktop.TypeText("first\nsecond"));
        Check(failure.InputEventsEmitted && NativeDesktop.TextSelections.Count == 1 &&
            NativeDesktop.TextMessages.Count == 0 && NativeDesktop.TextWakes.SequenceEqual(new[] { original }),
            "Selection insertion must not follow new focus and must wake only its original receiver.");
        cases++;

        NativeDesktop.Reset();
        NativeDesktop.TextControlClass = "Edit";
        before = NativeDesktop.InputGeneration;
        failure = Fails(() => NativeDesktop.TypeText("wrong root", 99));
        Check(!failure.InputEventsEmitted && NativeDesktop.TextSelections.Count == 0 &&
            NativeDesktop.TextWakes.Count == 0 && NativeDesktop.InputGeneration == before,
            "AX root mismatch must reject standard-control input before insertion.");
        cases++;

        foreach (var name in new[] { "Edit", "Unknown" })
        {
            NativeDesktop.Reset();
            NativeDesktop.TextControlClass = name;
            before = NativeDesktop.InputGeneration;
            failure = Fails(() => DesktopProvider.ExecuteBatch(new JsonObject { ["actions"] = new JsonArray(
                new JsonObject { ["type"] = "click", ["x"] = 5d, ["y"] = 5d },
                new JsonObject { ["type"] = "type", ["text"] = "valid\0suffix" }) }));
            Check(failure.Code == "invalid_argument" && !failure.InputEventsEmitted &&
                NativeDesktop.Calls.Count == 0 && NativeDesktop.TextSelections.Count == 0 &&
                NativeDesktop.TextMessages.Count == 0 && NativeDesktop.Captures.Count == 0 &&
                NativeDesktop.TextWakes.Count == 0 && NativeDesktop.InputGeneration == before,
                "NUL in later text must reject the whole GUI batch before any side effect.");
            cases++;
        }
        NativeDesktop.Reset();
        NativeDesktop.ModifierDown = true;
        failure = Fails(() => NativeDesktop.TypeText("blocked"));
        Check(!failure.InputEventsEmitted && NativeDesktop.TextMessages.Count == 0,
            "Text must not run while a physical modifier remains held.");
        NativeDesktop.ModifierDown = false;
        cases++;
        Console.WriteLine($"Standard text control input tests passed ({cases} cases).");
    }

    private static int Evidence(ProviderFailureException failure, string key) =>
        failure.ActionEvidence![key]!.GetValue<int>();
    private static ProviderFailureException Fails(Action action)
    {
        try { action(); }
        catch (ProviderFailureException failure) { return failure; }
        throw new InvalidOperationException("Expected a provider failure.");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
