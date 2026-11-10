namespace ComputerUse.Windows.Runtime;

internal static class ValueReadbackRoutingTests
{
    internal static void Run()
    {
        var readbackDelayCount = 0;
        var readbackSampleCount = 0;
        var liveReadbackVerified = LiveValueReadback.Verify(
            "Tamiflu",
            () => ++readbackSampleCount < 3 ? "Tami" : "Tamiflu",
            delay =>
            {
                if (delay != LiveValueReadback.PollInterval)
                {
                    throw new InvalidDataException(
                        "set_value live readback used the wrong poll interval.");
                }
                readbackDelayCount++;
            });
        if (!liveReadbackVerified ||
            readbackDelayCount != 2 ||
            readbackSampleCount != 3)
        {
            throw new InvalidDataException(
                "set_value readback did not stop at the first matching sample.");
        }
        var immediateDelays = 0;
        if (!LiveValueReadback.Verify("done", () => "done", _ => immediateDelays++) || immediateDelays != 0)
        {
            throw new InvalidDataException("A value that already matches must not wait.");
        }
        var boundedSamples = 0;
        var boundedDelays = 0;
        if (LiveValueReadback.Verify("expected", () => { boundedSamples++; return "different"; }, _ => boundedDelays++) ||
            boundedDelays != (int)(LiveValueReadback.Timeout / LiveValueReadback.PollInterval) ||
            boundedSamples != boundedDelays + 1)
        {
            throw new InvalidDataException("A persistent mismatch must stop after the bounded timeout.");
        }
        if (LiveValueReadback.Verify(
                "expected",
                () => "different",
                _ => { }) ||
            LiveValueReadback.Verify(
                "expected",
                () => throw new InvalidOperationException("stale"),
                _ => { }))
        {
            throw new InvalidDataException(
                "set_value live readback accepted a mismatch or unreadable target.");
        }
        if (!LiveValueReadback.VerifyTextValue(
                "replaced text",
                () => "replaced tttt\r",
                () => "replaced text\r",
                _ => { }) ||
            LiveValueReadback.VerifyTextValue(
                "replaced text",
                () => "replaced tttt\r",
                () => "replaced tttt\r",
                _ => { }))
        {
            throw new InvalidDataException(
                "A document's text, less its final paragraph mark, must decide when Value lags.");
        }
        var multilineValueReads = 0;
        var multilineTextReads = 0;
        if (!LiveValueReadback.VerifyTextValue(
                "first\r\nsecond",
                () =>
                {
                    multilineValueReads++;
                    return "first second";
                },
                () =>
                {
                    multilineTextReads++;
                    return "first\nsecond";
                },
                _ => { }) ||
            multilineValueReads != 0 ||
            multilineTextReads != 1 ||
            LiveValueReadback.VerifyTextValue(
                "first\r\nsecond",
                () => "first second",
                () => "first second",
                _ => { }) ||
            LiveValueReadback.VerifyTextValue(
                "first\r\nsecond",
                () => "first second",
                () => null,
                _ => { }) ||
            LiveValueReadback.CanonicalTextValue(
                "first second",
                "first\r\nsecond") != "first\r\nsecond" ||
            LiveValueReadback.CanonicalTextValue(
                "single line",
                "single line") != "single line")
        {
            throw new InvalidDataException(
                "multiline UIA set_value did not require exact TextPattern document readback.");
        }

        if (!UiaSetValueRouting.PrefersRange("slider", true) ||
            !UiaSetValueRouting.PrefersRange("spinbutton", true) ||
            !UiaSetValueRouting.PrefersRange("scrollbar", true) ||
            UiaSetValueRouting.PrefersRange("combobox", true) ||
            UiaSetValueRouting.PrefersRange("slider", false))
        {
            throw new InvalidDataException(
                "UIA set_value range routing changed its role or capability boundary.");
        }
        if (!UiaSetValueRouting.PrefersDocumentInput(
                documentControl: true,
                writableValueAvailable: true,
                textPatternAvailable: true) ||
            UiaSetValueRouting.PrefersDocumentInput(
                documentControl: false,
                writableValueAvailable: true,
                textPatternAvailable: true) ||
            UiaSetValueRouting.PrefersDocumentInput(
                documentControl: true,
                writableValueAvailable: false,
                textPatternAvailable: true) ||
            UiaSetValueRouting.PrefersDocumentInput(
                documentControl: true,
                writableValueAvailable: true,
                textPatternAvailable: false))
        {
            throw new InvalidDataException(
                "UIA document set_value input routing crossed its control or pattern boundary.");
        }
        UiaSetValueRouting.RequireDocumentInputAllowed(false);
        try
        {
            UiaSetValueRouting.RequireDocumentInputAllowed(true);
            throw new InvalidDataException("Pending input must reject before document selection.");
        }
        catch (ProviderFailureException error) when (
            error.Code == "desktop_input_failed" && error.Phase == "preparation" &&
            !error.InputEventsEmitted && !error.MutationUnknown) { }
        if (!UiaSetValueRouting.TryParseFiniteRangeValue("24", out var parsedRange) ||
            parsedRange != 24 ||
            UiaSetValueRouting.TryParseFiniteRangeValue("24px", out _) ||
            UiaSetValueRouting.TryParseFiniteRangeValue("NaN", out _) ||
            UiaSetValueRouting.TryParseFiniteRangeValue("Infinity", out _) ||
            !UiaSetValueRouting.IsWithinRange(24, 9, 72) ||
            UiaSetValueRouting.IsWithinRange(73, 9, 72))
        {
            throw new InvalidDataException(
                "UIA set_value range validation accepted a non-finite, malformed, or out-of-bounds value.");
        }
        var rangeReadbackSamples = 0;
        if (!LiveValueReadback.VerifyRange(
                24,
                () =>
                {
                    rangeReadbackSamples++;
                    return 24;
                },
                _ => { }) ||
            rangeReadbackSamples != 1 ||
            LiveValueReadback.VerifyRange(24, () => 23, _ => { }) ||
            LiveValueReadback.VerifyRange(24, () => null, _ => { }))
        {
            throw new InvalidDataException(
                "UIA RangeValue set_value did not require one fresh exact readback.");
        }

        if (UiaClickRouting.Select("radio", true, true, true) is not null ||
            UiaClickRouting.Select("radio", false, true, true) is not null ||
            UiaClickRouting.Select("radio", true, true, false) !=
                UiaClickRoute.SelectionItem ||
            UiaClickRouting.Select("radio", true, false, true) !=
                UiaClickRoute.Toggle ||
            UiaClickRouting.Select("listitem", true, true, true) !=
                UiaClickRoute.SelectionItem ||
            UiaClickRouting.Select("treeitem", true, true, true) !=
                UiaClickRoute.Toggle ||
            UiaClickRouting.Select("treeitem", false, true, true) !=
                UiaClickRoute.Toggle ||
            UiaClickRouting.Select("treeitem", true, true, false) !=
                UiaClickRoute.Invoke ||
            UiaClickRouting.Select("treeitem", false, true, false) !=
                UiaClickRoute.SelectionItem ||
            UiaClickRouting.Select("checkbox", true, true, true) !=
                UiaClickRoute.Toggle ||
            UiaClickRouting.Select("switch", true, true, true) !=
                UiaClickRoute.Toggle ||
            UiaClickRouting.Select("button", true, true, true) !=
                UiaClickRoute.Invoke ||
            UiaClickRouting.Select("radio", true, false, false) !=
                UiaClickRoute.Invoke ||
            UiaClickRouting.Select("checkbox", true, false, false) !=
                UiaClickRoute.Invoke ||
            UiaClickRouting.Select("generic", false, true, false) !=
                UiaClickRoute.SelectionItem ||
            UiaClickRouting.Select("generic", false, false, true) !=
                UiaClickRoute.Toggle ||
            UiaClickRouting.Select("generic", false, false, false) is not null)
        {
            throw new InvalidDataException(
                "UIA click routing lost its role-specific pattern boundary.");
        }

        // Native UIA selection remains semantic. MSAA selection is not a
        // substitute for a click, including when it reports Selected=true.
        // A list item's Invoke can open it, so only a radio may prefer Invoke.
        foreach (var invoke in new[] { false, true })
        {
            if (UiaClickRouting.Select("listitem", invoke, true, false, true) is not null ||
                UiaClickRouting.Select("listitem", invoke, true, false, false) !=
                    UiaClickRoute.SelectionItem ||
                UiaClickRouting.Select("radio", invoke, true, false, true) !=
                    (invoke ? UiaClickRoute.Invoke : null) ||
                UiaClickRouting.Select("radio", invoke, true, true, true) is not null)
            {
                throw new InvalidDataException(
                    "Bridged selection clicked through a non-equivalent semantic action.");
            }
        }
        if (UiaClickRouting.Select("generic", false, true, false, true) is not null ||
            UiaClickRouting.Select("checkbox", false, true, false, true) is not null ||
            UiaClickRouting.Select("button", true, false, false, true) != UiaClickRoute.Invoke ||
            UiaClickRouting.Select("checkbox", true, true, true, true) != UiaClickRoute.Toggle)
        {
            throw new InvalidDataException("Bridged selection changed unrelated click patterns.");
        }
    }
}
