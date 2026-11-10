using System.Text;

namespace ComputerUse.Windows.Runtime;

internal sealed record DocumentTextContent(string? Text, bool IsTextBearing = true);
internal sealed record DocumentContentProbe(bool? HasContent, string? IncompleteReason = null);

internal sealed record DocumentTextRead(string? Text, string? IncompleteReason = null)
{
    // Native document text fills an opaque UIA document. When UIA exposes
    // content, canonical traversal owns that content and its acquisition gaps.
    internal DocumentTextRead WithContentFallback(
        Func<DocumentContentProbe> probeContent, Func<DocumentTextRead> readLegacy)
    {
        if (Text is not null) { return this; }
        DocumentContentProbe probe;
        try { probe = probeContent(); }
        catch (ObservationDeadlineExceededException)
        {
            return new(null, "document_text_time_budget");
        }
        catch
        {
            return new(null, "children_read_failed");
        }
        return probe.HasContent switch
        {
            true => new(Text: null),
            false => readLegacy(),
            null => new(null, probe.IncompleteReason ?? "document_content_probe_incomplete"),
        };
    }

    internal static DocumentContentProbe ProbeContent<T>(T root,
        Func<T, T?> firstChild, Func<T, T?> nextSibling, Func<T, bool> isContent,
        Action checkTime, int maxNodes = 16, int maxDepth = 4) where T : class
    {
        var visited = 0;
        string? reason = null;
        try
        {
            var found = Descendants(root, 0);
            return new(found ? true : reason is null ? false : null, reason);
        }
        catch (ObservationDeadlineExceededException) { return new(null, "document_text_time_budget"); }
        catch { return new(null, "children_read_failed"); }

        bool Descendants(T parent, int depth)
        {
            if (depth >= maxDepth)
            {
                reason = "document_content_probe_depth_limit";
                return false;
            }
            if (visited >= maxNodes)
            {
                reason = "document_content_probe_node_limit";
                return false;
            }
            checkTime();
            var current = firstChild(parent);
            while (current is not null)
            {
                checkTime();
                if (visited >= maxNodes)
                {
                    reason = "document_content_probe_node_limit";
                    return false;
                }
                visited++;
                // A table is content already: never enumerate its cells here.
                if (isContent(current)) { return true; }
                if (Descendants(current, depth + 1)) { return true; }
                if (reason is not null) { return false; }
                checkTime();
                current = nextSibling(current);
            }
            return false;
        }
    }

    internal static DocumentTextRead Read(Func<int, string?> read, int maxCharacters)
    {
        try
        {
            var text = read(checked(maxCharacters + 1));
            if (text is null) { return new(null, "document_text_unavailable"); }
            return text.Length > maxCharacters
                ? new(text[..maxCharacters], "document_text_character_limit")
                : new(text);
        }
        catch (ObservationDeadlineExceededException)
        {
            return new(null, "document_text_time_budget");
        }
        catch
        {
            return new(null, "document_text_read_failed");
        }
    }

    // Read only the selected document's accessible descendants. Values on
    // text-bearing nodes are content; labels on other controls are not.
    // A synchronous native call can exceed the sampling deadline. The caller
    // checks it before each call; the sidecar process deadline bounds a hang.
    internal static DocumentTextRead Collect<T>(
        T root,
        Func<T, DocumentTextContent> readText,
        Func<T, int> childCount,
        Func<T, int, T> child,
        Action checkTime,
        int maxCharacters,
        int maxNodes = 256,
        int maxDepth = 32)
    {
        var output = new StringBuilder();
        var visited = 0;
        string? stopReason = null;
        string? gapReason = null;
        var hasText = false;
        Visit(root, 0);
        return new(hasText ? output.ToString() : null,
            stopReason ?? gapReason ?? (hasText ? null : "document_text_unavailable"));

        // A global budget explains why all later nodes are absent. Within
        // the sampled region, a failed read takes precedence over no value.
        void Gap(string reason)
        {
            if (gapReason is null || reason == "document_text_read_failed")
            {
                gapReason = reason;
            }
        }

        bool Read<TValue>(Func<TValue> read, out TValue value)
        {
            value = default!;
            if (stopReason is not null) { return false; }
            try
            {
                checkTime();
                value = read();
                return true;
            }
            catch (ObservationDeadlineExceededException)
            {
                stopReason = "document_text_time_budget";
            }
            catch { Gap("document_text_read_failed"); }
            return false;
        }

        void AppendText(string text)
        {
            var chunk = (hasText ? "\n" : string.Empty) + text;
            var remaining = maxCharacters - output.Length;
            hasText = true;
            if (chunk.Length > remaining)
            {
                output.Append(chunk.AsSpan(0, remaining));
                stopReason = "document_text_character_limit";
            }
            else { output.Append(chunk); }
        }

        void Visit(T node, int depth)
        {
            if (stopReason is not null) { return; }
            if (visited >= maxNodes) { stopReason = "document_text_node_limit"; return; }
            visited++;
            var contentRead = Read(() => readText(node), out var content);
            var text = contentRead && content.IsTextBearing ? content.Text : null;
            if (!string.IsNullOrEmpty(text))
            {
                AppendText(text);
                return;
            }
            if (!Read(() => childCount(node), out var count)) { return; }
            if (count < 0) { Gap("document_text_read_failed"); return; }
            if (count == 0)
            {
                if (text is not null) { AppendText(text); }
                else if (contentRead && content.IsTextBearing) { Gap("document_text_unavailable"); }
                return;
            }
            if (depth >= maxDepth) { stopReason = "document_text_depth_limit"; return; }
            for (var offset = 0; offset < count && stopReason is null; offset++)
            {
                // Stop before acquiring an extra native child.
                if (visited >= maxNodes) { stopReason = "document_text_node_limit"; return; }
                if (Read(() => child(node, offset), out var descendant))
                {
                    Visit(descendant, depth + 1);
                }
                else
                {
                    // Failed child acquisition consumes work too. Repeated
                    // failures cannot bypass the node budget.
                    visited++;
                }
            }
        }
    }
}
