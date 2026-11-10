package core

import (
	"fmt"
	"strings"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

type AppDescriptor struct {
	Name              string `json:"name"`
	BundleIdentifier  string `json:"bundleIdentifier,omitempty"`
	PID               int    `json:"pid"`
	ProviderBusName   string `json:"providerBusName,omitempty"`
	ProcessGeneration string `json:"processGeneration,omitempty"`
	DisplayText       string `json:"displayText,omitempty"`
	InputCapable      bool   `json:"inputCapable"`
	// Windows lists the app's top-level windows in an apps listing. A
	// provider that does not report them leaves it empty.
	Windows []ProviderAppWindow `json:"windows,omitempty"`
}

// ProviderAppWindow is one top-level window in an apps listing. Handle is
// the strong native window identity; a window without one is listed but
// cannot be addressed.
type ProviderAppWindow struct {
	Title              string `json:"title"`
	Role               string `json:"role"`
	Handle             string `json:"handle"`
	ProviderBusName    string `json:"providerBusName"`
	ProviderObjectPath string `json:"providerObjectPath"`
	ProcessGeneration  string `json:"processGeneration"`
	Active             bool   `json:"active"`
	InputTarget        bool   `json:"inputTarget"`
	// NoAccessibilityTree marks a window whose process exports no
	// accessibility tree; it has a native handle only.
	NoAccessibilityTree bool `json:"noAccessibilityTree,omitempty"`
}

type frame struct {
	X      float64 `json:"x"`
	Y      float64 `json:"y"`
	Width  float64 `json:"width"`
	Height float64 `json:"height"`
}

type windowSelectionCandidate struct {
	Index    int    `json:"index"`
	Role     string `json:"role,omitempty"`
	Title    string `json:"title,omitempty"`
	Active   bool   `json:"active,omitempty"`
	Showing  bool   `json:"showing,omitempty"`
	Visible  bool   `json:"visible,omitempty"`
	Modal    bool   `json:"modal,omitempty"`
	Bounds   *frame `json:"bounds,omitempty"`
	Selected bool   `json:"selected,omitempty"`
}

type windowSelection struct {
	Reason         string                     `json:"reason,omitempty"`
	CandidateCount int                        `json:"candidateCount,omitempty"`
	Candidates     []windowSelectionCandidate `json:"candidates,omitempty"`
}

type ElementRecord struct {
	Index                           int                           `json:"index"`
	RuntimeID                       []int                         `json:"runtimeId,omitempty"`
	RuntimeIDAliases                [][]int                       `json:"runtimeAliases,omitempty"`
	AutomationID                    string                        `json:"automationId,omitempty"`
	ProviderBusName                 string                        `json:"providerBusName,omitempty"`
	ProviderObjectPath              string                        `json:"providerObjectPath,omitempty"`
	ProcessGeneration               string                        `json:"processGeneration,omitempty"`
	ProviderIdentityScope           string                        `json:"providerIdentityScope,omitempty"`
	ProviderWindowBusName           string                        `json:"providerWindowBusName,omitempty"`
	ProviderWindowObjectPath        string                        `json:"providerWindowObjectPath,omitempty"`
	ProviderWindowProcessGeneration string                        `json:"providerWindowProcessGeneration,omitempty"`
	ProviderWindowRole              string                        `json:"providerWindowRole,omitempty"`
	ProviderWindowName              string                        `json:"providerWindowName,omitempty"`
	Showing                         *bool                         `json:"showing,omitempty"`
	Visible                         *bool                         `json:"visible,omitempty"`
	VisibilityEvidence              *AXVisibilityEvidence         `json:"visibilityEvidence,omitempty"`
	NameHash                        string                        `json:"nameHash,omitempty"`
	Name                            string                        `json:"name,omitempty"`
	ControlType                     string                        `json:"controlType,omitempty"`
	LocalizedControlType            string                        `json:"localizedControlType,omitempty"`
	ClassName                       string                        `json:"className,omitempty"`
	Value                           string                        `json:"value,omitempty"`
	NativeWindowHandle              int64                         `json:"nativeWindowHandle,omitempty"`
	Frame                           *frame                        `json:"frame,omitempty"`
	Actions                         []string                      `json:"actions,omitempty"`
	SetValueSupported               *bool                         `json:"setValue,omitempty"`
	EditTarget                      *axRelationTarget             `json:"editTarget,omitempty"`
	States                          string                        `json:"states,omitempty"`
	Description                     string                        `json:"description,omitempty"`
	TextAttributes                  map[string]string             `json:"textAttributes,omitempty"`
	Placeholder                     string                        `json:"placeholder,omitempty"`
	Relations                       map[string][]axRelationTarget `json:"relations,omitempty"`
	RelationReadComplete            bool                          `json:"relationReadComplete,omitempty"`
	NameSource                      string                        `json:"nameSource,omitempty"`
	FindMatchedField                string                        `json:"-"`
	FindMatchedText                 string                        `json:"-"`
}

type axRelationTarget struct {
	Role               string `json:"role,omitempty"`
	Name               string `json:"name,omitempty"`
	ProviderBusName    string `json:"providerBusName,omitempty"`
	ProviderObjectPath string `json:"providerObjectPath,omitempty"`
	ProcessGeneration  string `json:"processGeneration,omitempty"`
}

// axCellSummaryAnnotation is the provider annotation on a table whose
// visible empty cells were left out of the tree. Its Detail says how many
// and how to reach them; it is rendered on the table and is not a coverage
// gap.
const axCellSummaryAnnotation = "cell_summary"

// axNullReferenceReason marks provider-omission annotations for child slots
// that the application answered with a null reference. The renderer shows
// their count on the parent; they remain a coverage gap.
const axNullReferenceReason = "null_reference"

type axProviderAnnotation struct {
	ParentRuntimeID []int  `json:"parentRuntimeId,omitempty"`
	Kind            string `json:"kind"`
	Count           int    `json:"count,omitempty"`
	Reason          string `json:"reason,omitempty"`
	Detail          string `json:"detail,omitempty"`
}

// elementRef is an element's stable ref together with the fields that
// invalidate it. A change of role or name issues a new ref, because the
// element is no longer the same object.
type elementRef struct {
	Index    int    `json:"index"`
	Role     string `json:"role"`
	Name     string `json:"name"`
	NameHash string `json:"nameHash,omitempty"`
}

// busySignal is evidence that the application is still working, computed by
// busy_signal in the Linux provider. Without it, an unfinished asynchronous
// operation and budget-truncated output look identical in the returned text.
type busySignal struct {
	WindowBusy       bool `json:"windowBusy"`
	ProgressElements int  `json:"progressElements"`
}

func (b *busySignal) busy() bool {
	return b != nil && (b.WindowBusy || b.ProgressElements > 0)
}

type AppSnapshot struct {
	App                 AppDescriptor          `json:"app"`
	WindowTitle         string                 `json:"windowTitle,omitempty"`
	WindowHandle        string                 `json:"windowHandle,omitempty"`
	WindowAbsent        bool                   `json:"windowAbsent,omitempty"`
	ClosedWindowHandle  string                 `json:"closedWindowHandle,omitempty"`
	Windows             []TopLevelWindow       `json:"windows,omitempty"`
	WindowSelection     *windowSelection       `json:"windowSelection,omitempty"`
	SnapshotID          string                 `json:"snapshotId,omitempty"`
	SampledAt           string                 `json:"sampledAt,omitempty"`
	Busy                *busySignal            `json:"busy,omitempty"`
	WindowBounds        *frame                 `json:"windowBounds,omitempty"`
	ScreenshotPNGBase64 string                 `json:"screenshotPngBase64,omitempty"`
	ScreenshotMimeType  string                 `json:"screenshotMimeType,omitempty"`
	ScreenshotEncoding  string                 `json:"screenshotEncoding,omitempty"`
	CoordinateSpace     string                 `json:"coordinateSpace,omitempty"`
	FrameID             string                 `json:"frameId,omitempty"`
	DesktopLayout       map[string]any         `json:"desktopLayout,omitempty"`
	Width               int                    `json:"width,omitempty"`
	Height              int                    `json:"height,omitempty"`
	TreeLines           []string               `json:"treeLines,omitempty"`
	FocusedSummary      string                 `json:"focusedSummary,omitempty"`
	SelectedText        string                 `json:"selectedText,omitempty"`
	Elements            []ElementRecord        `json:"elements,omitempty"`
	Refs                map[string]elementRef  `json:"refs,omitempty"`
	Annotations         []axProviderAnnotation `json:"annotations,omitempty"`
	// FindCandidatesOnly marks a Linux find walk whose records cover only
	// nodes that can match the query and their ancestors. Its tree is not
	// a complete identity inventory, so refs minted from it are bound to
	// provider objects instead of selectors.
	FindCandidatesOnly bool `json:"findCandidatesOnly,omitempty"`
	// FindResume is the runtime path of the first node a Linux find walk
	// did not visit because a budget or its deadline ran out.
	FindResume []int `json:"findResume,omitempty"`

	// The fields below are set by the Go service when the snapshot is cached;
	// the provider does not send them. Snapshot age matters on the desktop:
	// unlike DOM changes in a browser, desktop UI changes raise no observable
	// event, so a snapshot can be stale for tens of seconds unnoticed.
	capturedAt time.Time
	sourceApp  string
	// Relation to the previous snapshot. The provider captures each snapshot
	// independently in a one-shot process, so only this side, which holds the
	// history, can tell whether a snapshot matches the previous one.
	comparedToPrevious    bool
	identicalToPrevious   bool
	previousSnapshotID    string
	previousHandle        string
	projection            snapshotProjection
	Scope                 string `json:"-"`
	scopeFallback         string
	scopeRoot             *ElementRecord
	scopedCanonicalCount  int
	observationRoot       *ElementRecord
	observationRootEntry  *AXRefEntry
	observationRootMode   string
	rootedCanonicalCount  int
	contentRoot           *ElementRecord
	contentCanonicalCount int
	contentRevision       string
	AXSnapshot            *AXSnapshot `json:"-"`
	findHiddenRecords     map[int]ElementRecord
	actionExecution       *RuntimeActionEvidence
	deliveryOnly          bool
	// desktopFrame is the full-desktop GUI frame whose image this snapshot
	// carries; see attachDesktopFrame.
	desktopFrame *AppSnapshot
	// lightReadback answers window identity without reading window content.
	// Tree comparisons must not use this root-only observation.
	lightReadback             bool
	publicAppRef              string
	publicWindowRef           string
	externalDesktopTransition bool
	// actedRef is the rN targeted by the action that produced this snapshot.
	actedRef string
	// findResume continues the find walk that read this snapshot; see
	// ax_find_resume.go.
	findResume *axFindResume
}

type TopLevelWindow struct {
	Title                     string `json:"title,omitempty"`
	Role                      string `json:"role,omitempty"`
	Handle                    string `json:"handle,omitempty"`
	ProviderBusName           string `json:"providerBusName,omitempty"`
	ProviderObjectPath        string `json:"providerObjectPath,omitempty"`
	ProviderProcessGeneration string `json:"providerProcessGeneration,omitempty"`
	Selected                  bool   `json:"selected,omitempty"`
	Active                    bool   `json:"active,omitempty"`
	Modal                     bool   `json:"modal,omitempty"`
	Showing                   bool   `json:"showing,omitempty"`
	publicRef                 string
}

type snapshotProjection struct {
	TextLimit    any
	MaxTreeNodes int
	MaxTreeDepth int
	Prune        *bool
	Boxes        *bool
	View         string
	Scope        string
	Root         string
}

func (s *AppSnapshot) renderedText() string {
	if s == nil {
		return ""
	}
	title := s.WindowTitle
	if strings.TrimSpace(title) == "" {
		title = s.App.Name
	}
	windowLine := fmt.Sprintf("window: %q", title)
	if s.WindowAbsent {
		windowLine = "window: null"
	} else if s.publicWindowRef != "" {
		windowLine = fmt.Sprintf(
			"window: %q [ref=%s]", title, s.publicWindowRef,
		)
	} else if s.WindowHandle != "" {
		windowLine = fmt.Sprintf(
			"window: %q [window-action-ref=unavailable; rN actions need no window_ref]", title,
		)
	}
	lines := []string{}
	if s.publicAppRef != "" {
		// The app is named, not offered as an aN: the window ref already
		// identifies it, and models never passed the aN.
		lines = append(lines, fmt.Sprintf("app: %q", s.App.Name))
	}
	lines = append(lines, windowLine)
	if len(s.Windows) > 1 {
		lines = append(lines, "windows:")
		lines = append(lines, renderedWindowInventory(s.Windows)...)
	}
	if normalizedAXScope(s.Scope) == contract.AXScopeContent && s.scopeRoot != nil {
		lines = append(lines, fmt.Sprintf(
			"scope: content root=%s %q",
			canonicalRole(s.scopeRoot),
			s.scopeRoot.Name,
		))
	} else if line := s.scopeFallbackLine(); line != "" {
		lines = append(lines, line)
	}
	if s.observationRoot != nil {
		lines = append(lines, fmt.Sprintf(
			"root: mode=%s %s %q",
			s.observationRootMode,
			canonicalRole(s.observationRoot),
			s.observationRoot.Name,
		))
	}
	lines = append(lines, s.TreeLines...)
	return strings.Join(lines, "\n")
}

// resultWithTreeOverride renders the given lines in place of TreeLines, for
// incremental views.
func (s *AppSnapshot) resultWithTreeOverride(notes, lines []string) ToolCallResult {
	if s == nil {
		return s.resultWithNotes(notes)
	}
	clone := *s
	clone.TreeLines = lines
	return clone.resultWithNotes(notes)
}

// resultWithNotes puts notes on what the action actually did, and whether its
// result was confirmed, before the accessibility tree. A new tree alone is
// easily misread as "the action succeeded", but it is only a snapshot.
func (s *AppSnapshot) resultWithNotes(notes []string) ToolCallResult {
	return s.textWithNotes(s.renderedText(), notes)
}

// notesOnlyResult returns only diagnostic notes, without a tree or screenshot.
//
// Most observation cost is a by-product of actions: re-rendering the full tree
// and attaching a screenshot after every action can cost more than an explicit
// observation, and a caller chaining several known actions does not read the
// intermediate trees.
//
// The tree is not dropped by default, because the post-action tree is the
// evidence of whether the step took effect; the caller must opt in. The notes
// still report the resolved element, visual change evidence and any modal
// dialog; only the complete current state is omitted.
func (s *AppSnapshot) notesOnlyResult(notes []string) ToolCallResult {
	notes = modelFacingAXNotes(notes)
	lines := make([]string, 0, len(notes)+1)
	for _, note := range notes {
		lines = append(lines, "Note: "+note)
	}
	lines = append(lines, "",
		"(observe=\"none\": no final tree or screenshot was returned. Existing rN "+
			"refs remain usable only while their logical "+
			"targets remain valid. Removed or replaced targets become stale; use "+
			"AX mode=observe or find to obtain a fresh ref.)")
	return ToolCallResult{Content: []ContentItem{
		{Type: "text", Text: capInline(strings.Join(lines, "\n"))},
	}}
}

// withoutScreenshot returns a copy of the snapshot without the image, for
// observation modes that return only the tree. It copies rather than clearing
// in place because the cached snapshot remains the before-state for the next
// action.
func (s *AppSnapshot) withoutScreenshot() *AppSnapshot {
	if s == nil {
		return nil
	}
	clone := *s
	clone.ScreenshotPNGBase64 = ""
	clone.ScreenshotMimeType = ""
	clone.ScreenshotEncoding = ""
	clone.desktopFrame = nil
	return &clone
}

func (s *AppSnapshot) screenshotMimeType() string {
	if s != nil && strings.TrimSpace(s.ScreenshotMimeType) != "" {
		return s.ScreenshotMimeType
	}
	return "image/png"
}

// screenshotOnlyResult returns only pixels and evidence notes, without the tree.
//
// Formatting changes and file state often never reach the accessibility tree:
// right-aligning text in LibreOffice Impress or saving with Ctrl+S can leave
// the tree bytes unchanged. The tree is then pure overhead and the image is
// the only evidence.
func (s *AppSnapshot) screenshotOnlyResult(notes []string) ToolCallResult {
	notes = modelFacingAXNotes(notes)
	lines := make([]string, 0, len(notes)+1)
	for _, note := range notes {
		lines = append(lines, "Note: "+note)
	}
	if s == nil || s.ScreenshotPNGBase64 == "" {
		lines = append(lines, "",
			"(observe=\"screenshot\": no full-desktop image came back. On GNOME "+
				"Wayland the capture can return blank. The tree was refreshed "+
				"server-side. Existing rN refs remain usable only while their logical "+
				"targets remain valid; use AX mode=observe or find after a stale ref.)")
		return ToolCallResult{Content: []ContentItem{
			{Type: "text", Text: capInline(strings.Join(lines, "\n"))},
		}}
	}
	lines = append(lines, "",
		"(observe=\"screenshot\": the tree was refreshed server-side but not "+
			"returned. Existing rN refs remain usable only while their logical targets "+
			"remain valid; use AX mode=observe or find after a stale ref.)")
	return ToolCallResult{Content: []ContentItem{
		{Type: "text", Text: capInline(strings.Join(lines, "\n"))},
		{Type: "image", Data: s.ScreenshotPNGBase64, MimeType: s.screenshotMimeType(), frame: s.desktopFrame},
	}}
}

// textWithNotes is the shared output path for trees and other text: notes
// first, then the body, spilled to a file above the threshold and capped
// inline. find results must use the same path, or a query with hundreds of
// matches would reproduce the oversized-output problem.
func (s *AppSnapshot) textWithNotes(text string, notes []string) ToolCallResult {
	notes = modelFacingAXNotes(notes)
	if len(notes) > 0 {
		lines := make([]string, 0, len(notes)+1)
		for _, note := range notes {
			lines = append(lines, "Note: "+note)
		}
		lines = append(lines, "", text)
		text = strings.Join(lines, "\n")
	}
	if spilled := spillToFile("snapshot", text); spilled != "" {
		text = spilled
	}
	text = capInline(text)
	result := ToolCallResult{
		Content: []ContentItem{{Type: "text", Text: text}},
	}
	if s != nil && s.ScreenshotPNGBase64 != "" {
		result.Content = append(result.Content, ContentItem{
			Type:     "image",
			Data:     s.ScreenshotPNGBase64,
			MimeType: s.screenshotMimeType(),
			frame:    s.desktopFrame,
		})
	}
	return result
}
