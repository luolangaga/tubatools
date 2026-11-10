using ComputerUse.Windows.Runtime;

internal static class DocumentTextTests
{
    internal static void Run()
    {
        var textOnly = DocumentTextRead.Read(limit => "First paragraph\nUnicode café 中文", 100);
        Check(textOnly.Text == "First paragraph\nUnicode café 中文" && textOnly.IncompleteReason is null,
            "Text-only content does not require a Value pattern.");
        var empty = DocumentTextRead.Read(limit => "", 5);
        Check(empty.Text == "" && empty.IncompleteReason is null, "A successfully read empty range is known empty.");
        var failed = DocumentTextRead.Read(limit => throw new InvalidOperationException(), 5);
        Check(failed.Text is null && failed.IncompleteReason == "document_text_read_failed",
            "Failed document ranges are not empty documents.");
        var legacyCalls = 0;
        DocumentTextRead Legacy() { legacyCalls++; return new("Native paragraphs"); }
        var childProbes = 0;
        DocumentContentProbe HasContent() { childProbes++; return new(true); }
        Check(textOnly.WithContentFallback(HasContent, Legacy) == textOnly && childProbes == 0 && legacyCalls == 0,
            "Successful document text does not start child or native probes.");
        Check(empty.WithContentFallback(HasContent, Legacy) == empty && childProbes == 0 && legacyCalls == 0,
            "Known empty text must not trigger a native fallback.");
        var existingTree = failed.WithContentFallback(HasContent, Legacy);
        Check(existingTree.Text is null && existingTree.IncompleteReason is null && childProbes == 1 && legacyCalls == 0,
            "An existing UIA subtree owns content acquisition without duplicate native text or aggregate-text gaps.");
        Check(failed.WithContentFallback(() => new(false), Legacy).Text == "Native paragraphs" && legacyCalls == 1,
            "A confirmed opaque document leaf reads native text once.");
        var failedProbe = failed.WithContentFallback(() => throw new InvalidOperationException(), Legacy);
        Check(failedProbe.IncompleteReason == "children_read_failed" && legacyCalls == 1,
            "A failed child probe is not mistaken for an empty UIA subtree.");
        var expiredProbe = failed.WithContentFallback(() => throw new ObservationDeadlineExceededException(), Legacy);
        Check(expiredProbe.IncompleteReason == "document_text_time_budget" && legacyCalls == 1,
            "An expired child probe preserves the deadline gap and starts no native fallback.");
        CheckContentProbe();
        var lengthRequested = 0;
        var exact = DocumentTextRead.Read(limit => { lengthRequested = limit; return "12345"; }, 5);
        Check(lengthRequested == 6 && exact.IncompleteReason is null, "One extra character distinguishes exact length from truncation.");
        Check(DocumentTextRead.Read(limit => "123456", 5) == new DocumentTextRead("12345", "document_text_character_limit"),
            "A truncated range reports the missing suffix.");

        var document = new Node(null, new("Title"), new(null, new("First paragraph"), new("Unicode café 中文")));
        var result = Collect(document);
        Check(result.Text == "Title\nFirst paragraph\nUnicode café 中文" && result.IncompleteReason is null,
            "Nested accessible content retains document order and Unicode.");
        var calls = 0;
        result = DocumentTextRead.Collect(document, Content, n => n.Children.Length,
            (n, i) => { calls++; return n.Children[i]; }, () => { }, 100, maxNodes: 2);
        Check(result.Text == "Title" && result.IncompleteReason == "document_text_node_limit" && calls == 1,
            "Node limit stops native child acquisition and preserves acquired text.");
        result = Collect(document, 8);
        Check(result.Text == "Title\nFi" && result.IncompleteReason == "document_text_character_limit",
            "Content overflow preserves a bounded prefix and reports truncation.");
        result = Collect(new Node(null, new Node(null, new Node("deep"))), maxDepth: 1);
        Check(result.Text is null && result.IncompleteReason == "document_text_depth_limit", "Deep trees cannot escape the traversal bound.");
        result = DocumentTextRead.Collect(document, Content, n => n.Children.Length,
            (n, i) => n.Children[i], () => throw new ObservationDeadlineExceededException(), 100);
        Check(result.Text is null && result.IncompleteReason == "document_text_time_budget", "Expired sampling never begins a native read.");
        result = DocumentTextRead.Collect(document, Content, n => n.Children.Length,
            (n, i) => i == 1 ? throw new InvalidOperationException() : n.Children[i], () => { }, 100);
        Check(result.Text == "Title" && result.IncompleteReason == "document_text_read_failed", "A missing child preserves partial content without claiming completeness.");
        Check(Collect(new Node(null)).IncompleteReason == "document_text_unavailable", "An opaque empty shell does not prove an empty document.");
        var childRead = false;
        result = DocumentTextRead.Collect(new Node("Complete text", new Node("Duplicate")), Content,
            n => n.Children.Length, (n, i) => { childRead = true; return n.Children[i]; }, () => { }, 100);
        Check(result.Text == "Complete text" && !childRead, "A complete node value is not duplicated through its descendants.");
        result = Collect(new Node(null, new Node(null) { IsTextBearing = false }, new Node("Paragraph after decoration")));
        Check(result.Text == "Paragraph after decoration" && result.IncompleteReason is null,
            "A known non-text leaf neither becomes content nor prevents reading following paragraphs.");
        result = Collect(new Node(null, new Node("Readable prefix"), new Node(null), new Node("Readable suffix")));
        Check(result.Text == "Readable prefix\nReadable suffix" && result.IncompleteReason == "document_text_unavailable",
            "An unknown text-bearing leaf preserves its gap without suppressing later paragraphs.");
        CheckRecoverableContentGaps();
        CheckEmptyParagraphs();
        Console.WriteLine("Document text reading tests passed.");
    }

    private static DocumentTextRead Collect(Node root, int maxCharacters = 100, int maxDepth = 32) =>
        DocumentTextRead.Collect(root, Content, n => n.Children.Length,
            (n, i) => n.Children[i], () => { }, maxCharacters, maxDepth: maxDepth);

    private static void CheckRecoverableContentGaps()
    {
        var document = new Node(null, new Node("Heading"), new Node(null), new Node("Later paragraph"));
        var result = DocumentTextRead.Collect(document, Content, n => n.Children.Length,
            (n, i) => i == 1 ? throw new InvalidOperationException() : n.Children[i], () => { }, 100);
        Check(result.Text == "Heading\nLater paragraph" && result.IncompleteReason == "document_text_read_failed",
            "A failed child read does not prevent reading the following sibling.");

        var failedContainer = new Node(null, new Node("Contained paragraph"));
        document = new Node(null, failedContainer, new Node("Following paragraph"));
        result = DocumentTextRead.Collect(document,
            n => ReferenceEquals(n, failedContainer) ? throw new InvalidOperationException() : Content(n),
            n => n.Children.Length, (n, i) => n.Children[i], () => { }, 100);
        Check(result.Text == "Contained paragraph\nFollowing paragraph" && result.IncompleteReason == "document_text_read_failed",
            "A failed container value read still permits its readable descendants and later siblings.");
        result = DocumentTextRead.Collect(document, Content,
            n => ReferenceEquals(n, failedContainer) ? throw new InvalidOperationException() : n.Children.Length,
            (n, i) => n.Children[i], () => { }, 100);
        Check(result.Text == "Following paragraph" && result.IncompleteReason == "document_text_read_failed",
            "A failed child-count read skips that subtree but preserves later siblings.");

        document = new Node(null, new Node(null), new Node("Ignored"), new Node("Last paragraph"));
        result = DocumentTextRead.Collect(document, Content, n => n.Children.Length,
            (n, i) => i == 1 ? throw new InvalidOperationException() : n.Children[i], () => { }, 100);
        Check(result.Text == "Last paragraph" && result.IncompleteReason == "document_text_read_failed",
            "A later failed read takes precedence over an earlier unknown value.");
        result = Collect(new Node(null, new Node(null), new Node("Long paragraph")), maxCharacters: 4);
        Check(result.Text == "Long" && result.IncompleteReason == "document_text_character_limit",
            "A character limit is not hidden by an earlier unknown value.");
        result = Collect(new Node(null, new Node(null), new Node(null, new Node("Too deep"))), maxDepth: 1);
        Check(result.Text is null && result.IncompleteReason == "document_text_depth_limit",
            "A depth limit stops all later acquisition and takes precedence over earlier gaps.");

        var childCalls = 0;
        result = DocumentTextRead.Collect(document, Content, n => n.Children.Length,
            (n, i) => { childCalls++; throw new InvalidOperationException(); }, () => { }, 100, maxNodes: 3);
        Check(childCalls == 2 && result.IncompleteReason == "document_text_node_limit",
            "Failed child acquisition consumes node budget instead of permitting unlimited retries.");
        var expired = false;
        result = DocumentTextRead.Collect(document, Content, n => n.Children.Length,
            (n, i) => { if (i == 1) { expired = true; } return n.Children[i]; },
            () => { if (expired) { throw new ObservationDeadlineExceededException(); } }, 100);
        Check(result.Text is null && result.IncompleteReason == "document_text_time_budget",
            "An expired global deadline takes precedence over earlier recoverable gaps.");
        result = Collect(new Node(""));
        Check(result.Text == "" && result.IncompleteReason is null,
            "A known empty text value remains empty without creating an acquisition gap.");
    }

    private static void CheckEmptyParagraphs()
    {
        foreach (var paragraphs in new[]
                 {
                     new[] { "A", "", "B" }, new[] { "", "A" }, new[] { "A", "" },
                     new[] { "A", "", "", "B" }, new[] { "", "", "" },
                 })
        {
            var result = Collect(new Node(null, paragraphs.Select(text => new Node(text)).ToArray()));
            Check(result.Text == string.Join('\n', paragraphs) && result.IncompleteReason is null,
                "Known empty paragraphs retain leading, middle, consecutive and trailing line breaks.");
        }
        var decorated = new Node(null, new Node("") { IsTextBearing = false },
            new Node("A"), new Node(null) { IsTextBearing = false }, new Node(""),
            new Node("") { IsTextBearing = false }, new Node("B"));
        Check(Collect(decorated) == new DocumentTextRead("A\n\nB"),
            "Non-text decorations never contribute an empty paragraph, even with a known empty value.");
        Check(Collect(new Node(null, new Node("A"), new Node(""), new Node(null), new Node("B"))) ==
            new DocumentTextRead("A\n\nB", "document_text_unavailable"),
            "Unknown text retains its gap without erasing adjacent known empty paragraphs.");
        Check(Collect(new Node(null, new Node("A"), new Node("")), maxCharacters: 1) ==
            new DocumentTextRead("A", "document_text_character_limit"),
            "A trailing empty paragraph still consumes a separator within the character budget.");
        Check(Collect(new Node(null, new Node("A"), new Node("")), maxCharacters: 2) ==
            new DocumentTextRead("A\n"), "An exact character budget retains a trailing empty paragraph.");
    }

    private static void CheckContentProbe()
    {
        var emptyShell = new ProbeNode(false, new ProbeNode(false));
        var result = Probe(emptyShell);
        Check(result.HasContent == false && result.IncompleteReason is null,
            "An empty generic or caret child does not establish document content.");
        var fallbackCalls = 0;
        var opaqueText = new DocumentTextRead(null).WithContentFallback(() => result,
            () => { fallbackCalls++; return new("Native paragraph after empty UIA shell"); });
        Check(fallbackCalls == 1 && opaqueText.Text == "Native paragraph after empty UIA shell",
            "Opaque documents with empty generic children retain their native content fallback.");

        var table = new ProbeNode(true, new ProbeNode(true));
        var wrappedTable = new ProbeNode(false, new ProbeNode(false, table));
        var tableChildReads = 0;
        result = Probe(wrappedTable, node =>
        {
            if (ReferenceEquals(node, table)) { tableChildReads++; }
            return node.Children.FirstOrDefault();
        });
        Check(result.HasContent == true && tableChildReads == 0,
            "A table within a generic wrapper stops probing before any grid descendant read.");
        var noDuplicate = new DocumentTextRead(null).WithContentFallback(() => result,
            () => { fallbackCalls++; return new("Duplicate cell text"); });
        Check(noDuplicate.Text is null && noDuplicate.IncompleteReason is null && fallbackCalls == 1,
            "Existing canonical content does not trigger duplicate document aggregation.");

        var wide = new ProbeNode(false, Enumerable.Range(0, 20).Select(_ => new ProbeNode(false)).ToArray());
        result = Probe(wide);
        Check(result.HasContent is null && result.IncompleteReason == "document_content_probe_node_limit",
            "An unfinished wide probe reports unknown content, not absence.");
        var unknown = new DocumentTextRead(null).WithContentFallback(() => result,
            () => { fallbackCalls++; return new("Not permitted"); });
        Check(unknown.IncompleteReason == "document_content_probe_node_limit" && fallbackCalls == 1,
            "An incomplete content probe does not start native aggregation.");
        var deep = new ProbeNode(false);
        for (var depth = 0; depth < 5; depth++) { deep = new ProbeNode(false, deep); }
        result = Probe(deep);
        Check(result.HasContent is null && result.IncompleteReason == "document_content_probe_depth_limit",
            "A deep generic chain cannot escape the content probe depth bound.");
        result = Probe(emptyShell, _ => throw new InvalidOperationException());
        Check(result.HasContent is null && result.IncompleteReason == "children_read_failed",
            "A native child read failure remains distinct from an opaque document.");
        result = DocumentTextRead.ProbeContent(emptyShell, n => n.Children.FirstOrDefault(), _ => null,
            n => n.IsContent, () => throw new ObservationDeadlineExceededException());
        Check(result.HasContent is null && result.IncompleteReason == "document_text_time_budget",
            "An expired content probe starts no native descendant read.");

        static DocumentContentProbe Probe(ProbeNode root, Func<ProbeNode, ProbeNode?>? readChild = null)
        {
            var next = new Dictionary<ProbeNode, ProbeNode?>(ReferenceEqualityComparer.Instance);
            void Index(ProbeNode parent)
            {
                for (var i = 0; i < parent.Children.Length; i++)
                {
                    next[parent.Children[i]] = parent.Children.ElementAtOrDefault(i + 1);
                    Index(parent.Children[i]);
                }
            }
            Index(root);
            return DocumentTextRead.ProbeContent(root, readChild ?? (n => n.Children.FirstOrDefault()),
                n => next[n], n => n.IsContent, () => { });
        }
    }

    private sealed record ProbeNode(bool IsContent, params ProbeNode[] Children);

    private static DocumentTextContent Content(Node node) => new(node.Text, node.IsTextBearing);
    private sealed record Node(string? Text, params Node[] Children)
    {
        internal bool IsTextBearing { get; init; } = true;
    }
    private static void Check(bool condition, string description)
    {
        if (!condition) { throw new InvalidOperationException(description); }
    }
}
