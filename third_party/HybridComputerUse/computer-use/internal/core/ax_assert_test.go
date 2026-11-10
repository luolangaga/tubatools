package core

import (
	"strings"
	"testing"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

func TestScopedLookupExcludesApplicationChrome(t *testing.T) {
	snapshot := &AppSnapshot{
		Scope: contract.AXScopeContent,
		scopeRoot: &ElementRecord{
			Index: 2, RuntimeID: []int{1}, ControlType: "document web",
		},
		scopedCanonicalCount: 2,
		Elements: []ElementRecord{
			{
				Index: 1, RuntimeID: []int{0},
				ControlType: "push button", Name: "Search",
			},
			{
				Index: 2, RuntimeID: []int{1},
				ControlType: "document web", Name: "Page",
			},
			{
				Index: 3, RuntimeID: []int{1, 0},
				ControlType: "push button", Name: "Search",
			},
		},
	}
	scoped := snapshotForScopedLookup(snapshot)
	if len(scoped.Elements) != 2 {
		t.Fatalf("scoped lookup elements=%d want 2", len(scoped.Elements))
	}
	record, err := lookupElementFor(
		scoped,
		`push button "Search"`,
		"",
		"verify",
	)
	if err != nil || record.Index != 3 {
		t.Fatalf("content-scoped selector did not resolve page control: %#v %v",
			record, err)
	}
}

func TestVerifyReportsWhatItActuallySawNotJustFailure(t *testing.T) {
	// A bare "assertion failed" carries no information; "expected checked,
	// observed [enabled focused]" tells the agent what to do next.
	record := &ElementRecord{Index: 5, ControlType: "toggle button", Name: "Bold", States: "enabled focused"}

	ok, observed := verifyGoal{state: "checked"}.check(record, nil)
	if ok {
		t.Fatal("assertion must fail when the element is not checked")
	}
	if !strings.Contains(observed, "enabled focused") {
		t.Fatalf("failure must report the observed states, got %q", observed)
	}

	if ok, _ := (verifyGoal{state: "!checked"}).check(record, nil); !ok {
		t.Fatal("!checked should pass for an unchecked element")
	}

	// exists:false expresses "wait for the dialog to disappear": it passes only
	// when the element is absent.
	no := false
	if ok, _ := (verifyGoal{exists: &no}).check(nil, nil); !ok {
		t.Fatal("exists:false should pass when the element is absent")
	}
	if ok, _ := (verifyGoal{exists: &no}).check(record, nil); ok {
		t.Fatal("exists:false must not pass while the element is present")
	}

	// With the default timeout, asserting a state on a missing element must
	// return an actionable reason, not an empty failure.
	if ok, observed := (verifyGoal{state: "checked"}).check(nil, nil); ok || observed == "" {
		t.Fatalf("a missing element must fail with a reason, got ok=%v observed=%q", ok, observed)
	}
}

func TestVerifyDoesNotDoubleWrapTheStates(t *testing.T) {
	// Rendered states already carry brackets; wrapping them again yields
	// `[ [enabled]]`.
	record := &ElementRecord{Index: 1, ControlType: "push button", Name: "Search", States: " [has-click-action]"}
	_, observed := verifyGoal{state: "checked"}.check(record, nil)
	if strings.Contains(observed, "[ [") || strings.Contains(observed, "]]") {
		t.Fatalf("states must not be wrapped in brackets twice, got %q", observed)
	}
}

func TestAXAppUnavailableDetectionUsesStructuredFailureCode(t *testing.T) {
	result := TextResult("app is not visible yet", true)
	result.StructuredContent = map[string]any{
		"failure_code":         "ax_app_unavailable",
		"failure_phase":        "precondition",
		"input_events_emitted": false,
	}
	if !isAXAppUnavailableResult(result) {
		t.Fatal("assert_ax must recognize the structured temporary app-unavailable result")
	}
	result.StructuredContent["failure_code"] = "semantic_target_not_found"
	if isAXAppUnavailableResult(result) {
		t.Fatal("only ax_app_unavailable may enter the availability polling path")
	}
}

func TestIncrementalTreeDoesNotInflateSingleHugeLine(t *testing.T) {
	base := func(lines ...string) *AppSnapshot { return &AppSnapshot{TreeLines: lines} }
	body := strings.Repeat("log line abcdefghij ", 400)
	before := base("0 frame \"Terminal\"", "1 terminal \"\": \""+body+"tail\"")
	after := base("0 frame \"Terminal\"", "1 terminal \"\": \""+body+"TAIL-CHANGED\"")

	diff, ok := incrementalTree(before, after)
	if !ok {
		t.Fatal("a content change in one huge node should yield an incremental diff, not resend the full tree")
	}
	total := 0
	for _, line := range diff {
		total += len(line)
	}
	fullSize := 0
	for _, line := range after.TreeLines {
		fullSize += len(line)
	}
	if total >= fullSize {
		t.Fatalf("incremental diff of %d characters must be smaller than the full tree of %d characters", total, fullSize)
	}
	joined := strings.Join(diff, "\n")
	if !strings.Contains(joined, "~1 terminal") {
		t.Fatalf("summary must keep the leading element_index and role, got %q", firstLine(joined))
	}
	if !strings.Contains(joined, "[-tail][+TAIL-CHANGED]") {
		t.Fatalf("summary must write the change as [-old][+new], got %v", diff)
	}
	if strings.Count(joined, body) != 0 {
		t.Fatal("the unchanged body text must not be resent")
	}
}

func TestCompletedNegativeAssertionIsAnObservationNotAProtocolError(t *testing.T) {
	result := assertionNegativeResult(
		"assert_ax predicate_false: current value is unchecked",
		"predicate_false",
		3,
		0,
	)
	if result.IsError {
		t.Fatalf("completed negative assertion became a tool error: %#v", result)
	}
	if result.StructuredContent["assertion_passed"] != false ||
		result.StructuredContent["retry_same_request"] != false ||
		result.StructuredContent["poll_attempted"] != true {
		t.Fatalf("negative assertion evidence is incomplete: %#v",
			result.StructuredContent)
	}
}

func TestAssertAXRefUsesFullLiveTargetIdentity(t *testing.T) {
	service := NewService()
	target := &semanticTarget{
		Selector:            `button "Save"`,
		LocatorKind:         AXLocatorProviderBound,
		WindowHandle:        "private-window",
		ProviderBusName:     ":1.42",
		ProviderObjectPath:  "/org/a11y/save",
		ProcessGeneration:   "99123",
		Role:                "button",
		Name:                "Save",
		HasName:             true,
		ProviderRoleAliases: []string{"button", "push button"},
	}
	exists := true
	result := service.verifyResolvedAXRefWithProbe(
		"editor",
		"r17",
		target,
		verifyGoal{exists: &exists},
		1,
		0,
		func(request ProviderActionRequest) (*ProviderAccessibilityResult, error) {
			if request.Intent != AccessibilityResolveTarget ||
				request.WindowHandle != "private-window" ||
				request.Target.ProviderObjectPath != "/org/a11y/save" ||
				request.Target.LocatorKind != AXLocatorProviderBound {
				t.Fatalf("assert_ax degraded ref identity: %#v", request)
			}
			return &ProviderAccessibilityResult{
				OK: true,
				ResolvedElement: &ElementRecord{
					Index:       7,
					ControlType: "push button",
					Name:        "Save",
				},
			}, nil
		},
	)
	if result.IsError ||
		result.StructuredContent["assertion_status"] != "pass" {
		t.Fatalf("live ref assertion did not pass: %#v", result)
	}
}

func TestAssertAXDoesNotTreatIncompleteAbsenceAsPass(t *testing.T) {
	service := NewService()
	target := &semanticTarget{
		Selector:     `button "Save"`,
		LocatorKind:  AXLocatorSemantic,
		WindowHandle: "private-window",
		Role:         "button",
		Name:         "Save",
		HasName:      true,
	}
	exists := false
	result := service.verifyResolvedAXRefWithProbe(
		"editor",
		"r17",
		target,
		verifyGoal{exists: &exists},
		1,
		0,
		func(ProviderActionRequest) (*ProviderAccessibilityResult, error) {
			return &ProviderAccessibilityResult{
				OK:    false,
				Error: "AX bus read failed",
				Failure: &ProviderFailure{
					Code:          "observation_incomplete",
					FactsComplete: false,
				},
			}, nil
		},
	)
	if !result.IsError ||
		result.StructuredContent["assertion_status"] !=
			"observation_incomplete" {
		t.Fatalf("incomplete absence was treated as predicate truth: %#v", result)
	}
}

func TestAssertAXFallsBackToExactCanonicalSnapshot(t *testing.T) {
	service := NewService()
	target := &semanticTarget{
		Selector:           `checkbox "Automatic quotes"`,
		LocatorKind:        AXLocatorProviderBound,
		WindowHandle:       "private-window",
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/quote",
		ProcessGeneration:  "99123",
		Role:               "checkbox",
		Name:               "Automatic quotes",
		HasName:            true,
	}
	record := &ElementRecord{
		Index:              7,
		ControlType:        "check box",
		Name:               "Automatic quotes",
		States:             "[active]",
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/quote",
		ProcessGeneration:  "99123",
	}
	snapshot := &AppSnapshot{
		WindowHandle: "private-window",
		Elements:     []ElementRecord{*record},
	}
	result := service.verifyResolvedAXRefWithProbes(
		"editor",
		"r17",
		target,
		verifyGoal{state: "!checked"},
		1000,
		0,
		func(ProviderActionRequest) (*ProviderAccessibilityResult, error) {
			return &ProviderAccessibilityResult{
				OK:    false,
				Error: "AX resolver read was incomplete",
				Failure: &ProviderFailure{
					Code:          "observation_incomplete",
					FactsComplete: false,
				},
			}, nil
		},
		func() (*axAssertionSnapshotProof, ToolCallResult) {
			return &axAssertionSnapshotProof{
				snapshot: snapshot,
				notes:    []string{"canonical fallback"},
				record:   record,
			}, ToolCallResult{}
		},
	)
	if result.IsError ||
		result.StructuredContent["assertion_status"] != "pass" ||
		result.StructuredContent["assertion_canonical_fallback"] != true {
		t.Fatalf("exact canonical fallback did not prove the ref state: %#v", result)
	}
	if result.axProofSnapshot != snapshot ||
		len(result.axProofNotes) != 1 {
		t.Fatalf("canonical proof snapshot was not retained: %#v", result)
	}
}

func TestCanonicalAssertionFallbackRequiresTheSameIssuedRefIdentity(t *testing.T) {
	service := NewService()
	showing := true
	record := &ElementRecord{
		Index:              7,
		ControlType:        "check box",
		Name:               "Automatic quotes",
		States:             "[active]",
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/quote",
		ProcessGeneration:  "99123",
		Showing:            &showing,
		Visible:            &showing,
	}
	node := &AXNode{
		ID:                 "0/0",
		Role:               "checkbox",
		SemanticClass:      "control",
		Name:               record.Name,
		ProviderBusName:    record.ProviderBusName,
		ProviderObjectPath: record.ProviderObjectPath,
		ProcessGeneration:  record.ProcessGeneration,
		LocatorKind:        AXLocatorProviderBound,
		SourceRecord:       record,
		Showing:            &showing,
		Visible:            &showing,
	}
	tree := &AXSnapshot{
		AppRef:               "a1",
		AppIdentity:          "editor",
		WindowHandle:         "private-window",
		WindowRef:            "w1",
		Revision:             "c1",
		RefInventoryComplete: true,
		CanonicalRoots:       []*AXNode{node},
		Roots:                []*AXNode{node},
		ByID:                 map[string]*AXNode{"0/0": node},
	}
	service.RefRegistry.AssignSnapshotRefs(tree)
	if node.Ref == "" {
		t.Fatal("fixture did not receive a ref")
	}
	entry, err := service.RefRegistry.Resolve(node.Ref, "")
	if err != nil {
		t.Fatal(err)
	}
	snapshot := &AppSnapshot{
		App:             AppDescriptor{Name: "editor"},
		WindowHandle:    "private-window",
		Elements:        []ElementRecord{*record},
		publicAppRef:    "a1",
		publicWindowRef: "w1",
		AXSnapshot:      tree,
	}
	resolved, err := service.exactRefRecordFromSnapshot(
		snapshot,
		node.Ref,
		entry,
	)
	if err != nil || resolved != record {
		t.Fatalf("exact ref identity was not accepted: record=%#v err=%v", resolved, err)
	}

	tests := []struct {
		name    string
		mutate  func()
		restore func()
	}{
		{
			name:    "different ref",
			mutate:  func() { node.Ref = "r999" },
			restore: func() { node.Ref = entry.Ref },
		},
		{
			name:    "different app ref",
			mutate:  func() { snapshot.publicAppRef = "a999" },
			restore: func() { snapshot.publicAppRef = entry.AppRef },
		},
		{
			name:    "different logical window",
			mutate:  func() { snapshot.publicWindowRef = "w999" },
			restore: func() { snapshot.publicWindowRef = entry.LogicalWindowRef },
		},
		{
			name:    "different native window",
			mutate:  func() { snapshot.WindowHandle = "other-window" },
			restore: func() { snapshot.WindowHandle = entry.WindowHandle },
		},
		{
			name:    "different provider bus",
			mutate:  func() { record.ProviderBusName = ":1.999" },
			restore: func() { record.ProviderBusName = entry.ProviderBusName },
		},
		{
			name: "different provider object",
			mutate: func() {
				record.ProviderObjectPath = "/org/a11y/other"
			},
			restore: func() {
				record.ProviderObjectPath = entry.ProviderObjectPath
			},
		},
		{
			name: "different process generation",
			mutate: func() {
				record.ProcessGeneration = "99999"
			},
			restore: func() {
				record.ProcessGeneration = entry.ProcessGeneration
			},
		},
	}
	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			test.mutate()
			defer test.restore()
			if _, err := service.exactRefRecordFromSnapshot(
				snapshot,
				entry.Ref,
				entry,
			); err == nil {
				t.Fatalf("%s was accepted", test.name)
			}
		})
	}

	t.Run("different app identity", func(t *testing.T) {
		changed := *entry
		changed.AppIdentity = "other-editor"
		if _, err := service.exactRefRecordFromSnapshot(
			snapshot,
			entry.Ref,
			&changed,
		); err == nil {
			t.Fatal("different app identity was accepted")
		}
	})
}

func TestCanonicalAssertionFallbackAcceptsSemanticProviderWrapperChurn(t *testing.T) {
	service := NewService()
	showing := true
	record := &ElementRecord{
		Index:                 7,
		RuntimeID:             []int{0, 4, 7},
		ControlType:           "check box",
		Name:                  "Automatic quotes",
		States:                "[active]",
		ProviderBusName:       ":1.42",
		ProviderObjectPath:    "/org/a11y/quote/old",
		ProcessGeneration:     "99123",
		ProviderIdentityScope: "provider-wrapper-old",
		Showing:               &showing,
		Visible:               &showing,
	}
	node := &AXNode{
		ID:                 "0/4/7",
		Role:               "checkbox",
		SemanticClass:      "control",
		Name:               record.Name,
		RuntimeID:          append([]int(nil), record.RuntimeID...),
		ProviderBusName:    record.ProviderBusName,
		ProviderObjectPath: record.ProviderObjectPath,
		ProcessGeneration:  record.ProcessGeneration,
		SemanticTarget:     `checkbox "Automatic quotes"`,
		LocatorKind:        AXLocatorSemantic,
		SourceRecord:       record,
		Showing:            &showing,
		Visible:            &showing,
	}
	tree := &AXSnapshot{
		AppRef:               "a1",
		AppIdentity:          "editor",
		WindowHandle:         "private-window",
		WindowRef:            "w1",
		Revision:             "c1",
		RefInventoryComplete: true,
		CanonicalRoots:       []*AXNode{node},
		Roots:                []*AXNode{node},
		ByID:                 map[string]*AXNode{"0/4/7": node},
	}
	service.RefRegistry.AssignSnapshotRefs(tree)
	entry, err := service.RefRegistry.Resolve(node.Ref, "")
	if err != nil {
		t.Fatal(err)
	}
	snapshot := &AppSnapshot{
		App:             AppDescriptor{Name: "editor"},
		WindowHandle:    "private-window",
		Elements:        []ElementRecord{*record},
		publicAppRef:    "a1",
		publicWindowRef: "w1",
		AXSnapshot:      tree,
	}

	record.ProviderObjectPath = "/org/a11y/quote/rebuilt"
	record.ProviderIdentityScope = "provider-wrapper-rebuilt"
	node.ProviderObjectPath = record.ProviderObjectPath
	node.ProviderIdentityScope = record.ProviderIdentityScope
	if resolved, err := service.exactRefRecordFromSnapshot(
		snapshot,
		node.Ref,
		entry,
	); err != nil || resolved != record {
		t.Fatalf(
			"semantic ref rejected safe provider-wrapper churn: record=%#v err=%v",
			resolved,
			err,
		)
	}

	record.RuntimeID = []int{0, 9, 9}
	node.RuntimeID = append([]int(nil), record.RuntimeID...)
	if _, err := service.exactRefRecordFromSnapshot(
		snapshot,
		node.Ref,
		entry,
	); err == nil {
		t.Fatal("semantic ref accepted a different runtime lineage")
	}
}

func TestAssertAXCanonicalFallbackCompletesStartedSampleAfterDeadline(t *testing.T) {
	service := NewService()
	target := &semanticTarget{
		Selector:           `checkbox "Automatic quotes"`,
		LocatorKind:        AXLocatorProviderBound,
		WindowHandle:       "private-window",
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/quote",
		ProcessGeneration:  "99123",
		Role:               "checkbox",
		Name:               "Automatic quotes",
		HasName:            true,
	}
	record := &ElementRecord{
		Index:              7,
		ControlType:        "check box",
		Name:               "Automatic quotes",
		States:             "[active]",
		ProviderBusName:    ":1.42",
		ProviderObjectPath: "/org/a11y/quote",
		ProcessGeneration:  "99123",
	}
	snapshot := &AppSnapshot{
		WindowHandle: "private-window",
		Elements:     []ElementRecord{*record},
	}
	fallbackCalls := 0
	result := service.verifyResolvedAXRefWithProbes(
		"editor",
		"r17",
		target,
		verifyGoal{state: "!checked"},
		1,
		0,
		func(ProviderActionRequest) (*ProviderAccessibilityResult, error) {
			time.Sleep(2 * time.Millisecond)
			return &ProviderAccessibilityResult{
				OK:    false,
				Error: "AX resolver read was incomplete",
				Failure: &ProviderFailure{
					Code:          "observation_incomplete",
					FactsComplete: false,
				},
			}, nil
		},
		func() (*axAssertionSnapshotProof, ToolCallResult) {
			fallbackCalls++
			return &axAssertionSnapshotProof{
				snapshot: snapshot,
				record:   record,
			}, ToolCallResult{}
		},
	)
	if result.IsError ||
		result.StructuredContent["assertion_status"] != "pass" ||
		result.StructuredContent["assertion_canonical_fallback"] != true {
		t.Fatalf("terminal canonical sample did not complete: %#v", result)
	}
	if fallbackCalls != 1 {
		t.Fatalf("fallback calls = %d, want 1", fallbackCalls)
	}
}

func TestAssertAXTreatsCompleteStaleRefAsAbsent(t *testing.T) {
	service := NewService()
	target := &semanticTarget{
		Selector:     `button "Use Excel Format"`,
		LocatorKind:  AXLocatorProviderBound,
		WindowHandle: "private-dialog",
		Role:         "button",
		Name:         "Use Excel Format",
		HasName:      true,
	}
	exists := false
	result := service.verifyResolvedAXRefWithProbe(
		"editor",
		"r17",
		target,
		verifyGoal{exists: &exists},
		1,
		0,
		func(ProviderActionRequest) (*ProviderAccessibilityResult, error) {
			return &ProviderAccessibilityResult{
				OK:    false,
				Error: "stale_element_ref: provider object disappeared",
				Failure: &ProviderFailure{
					Code:          "stale_element_ref",
					Subject:       "element",
					FactsComplete: true,
				},
			}, nil
		},
	)
	if result.IsError ||
		result.StructuredContent["assertion_status"] != "pass" {
		t.Fatalf("complete stale ref did not prove exact ref absence: %#v", result)
	}
}

func TestAssertAXTreatsCompleteAppDisappearanceAsAbsent(t *testing.T) {
	service := NewService()
	target := &semanticTarget{
		Selector:     `button "Close"`,
		LocatorKind:  AXLocatorSemantic,
		WindowHandle: "private-window",
		Role:         "button",
		Name:         "Close",
		HasName:      true,
	}
	exists := false
	result := service.verifyResolvedAXRefWithProbe(
		"editor",
		"r21",
		target,
		verifyGoal{exists: &exists},
		1,
		0,
		func(ProviderActionRequest) (*ProviderAccessibilityResult, error) {
			return &ProviderAccessibilityResult{
				OK:    false,
				Error: "stale_app_ref: process generation no longer exists",
				Failure: &ProviderFailure{
					Code:          "stale_target",
					Subject:       "app",
					FactsComplete: true,
				},
			}, nil
		},
	)
	if result.IsError ||
		result.StructuredContent["assertion_status"] != "pass" {
		t.Fatalf("complete app disappearance did not prove ref absence: %#v", result)
	}
}

func TestAssertAXDoesNotTreatAmbiguityAsAbsence(t *testing.T) {
	service := NewService()
	target := &semanticTarget{
		Selector:     `button "OK"`,
		LocatorKind:  AXLocatorSemantic,
		WindowHandle: "private-window",
		Role:         "button",
		Name:         "OK",
		HasName:      true,
	}
	exists := false
	result := service.verifyResolvedAXRefWithProbe(
		"editor",
		"r22",
		target,
		verifyGoal{exists: &exists},
		1,
		0,
		func(ProviderActionRequest) (*ProviderAccessibilityResult, error) {
			return &ProviderAccessibilityResult{
				OK:    false,
				Error: "semantic target is ambiguous",
				Failure: &ProviderFailure{
					Code:          "semantic_target_ambiguous",
					Subject:       "element",
					FactsComplete: true,
				},
			}, nil
		},
	)
	if !result.IsError ||
		result.StructuredContent["assertion_status"] != "locator_ambiguous" {
		t.Fatalf("ambiguity was treated as absence: %#v", result)
	}
}

func TestValueEqualsAssertionUsesExactTrimmedValue(t *testing.T) {
	goal := verifyGoal{valueEquals: "5"}
	if ok, _ := goal.check(&ElementRecord{Value: " 5 "}, nil); !ok {
		t.Fatal("trimmed exact value did not match")
	}
	if ok, _ := goal.check(&ElementRecord{Value: "15"}, nil); ok {
		t.Fatal("substring incorrectly satisfied value_equals")
	}
}
