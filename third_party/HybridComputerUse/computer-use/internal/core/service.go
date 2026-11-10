package core

import (
	"encoding/base64"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

type failedObservationEntry struct {
	continuityNote string
	query          string
	windowHandle   string
	aliases        map[string]bool
}

type observationLedger struct {
	lastComplete map[string]*AppSnapshot
	lastFullTree map[string]bool
}

// DesktopState is shared by every caller of one desktop: the provider, the
// latest canonical snapshot per app (the address space that refs resolve
// in), and the aN/wN/rN registries. A ref names the same element for every
// caller, so the registries cannot be per caller. The GUI layout lease is
// desktop state too, but the provider holds it, not this struct.
type DesktopState struct {
	Backend           PlatformBackend `json:"-"`
	snapshots         map[string]*AppSnapshot
	RefRegistry       *RefRegistry       `json:"-"`
	WindowRefRegistry *WindowRefRegistry `json:"-"`
	AppRefRegistry    *AppRefRegistry    `json:"-"`
	// accessibilityEpoch counts invalidations caused by GUI input. A
	// session that saw an older epoch drops its own caches before its next
	// call, because input from another caller made them stale as well.
	accessibilityEpoch uint64
	// providerElapsed is the total time spent in provider calls.
	providerElapsed time.Duration
	// shared holds what concurrent daemon sessions coordinate on. It is a
	// pointer so that probe copies of the desktop share it.
	Shared *DesktopShared `json:"-"`
}

// DesktopShared is the coordination state of one desktop.
type DesktopShared struct {
	// mu serializes calls from concurrent sessions of a daemon. It is the
	// input lock: one call, reads included, runs at a time, because the
	// core state is not safe for concurrent use and the provider already
	// serializes its own calls.
	Mu sync.Mutex `json:"-"`
	// requestResults holds the responses of recent calls that carried a
	// request_id; see replayOnce.
	RequestMu      sync.Mutex               `json:"-"`
	RequestResults map[string]StoredRequest `json:"-"`
}

// sessionState belongs to one caller: what that caller was shown (diff
// baselines and delivered trees), its find cursors, the observation it
// still owes after a stale-ref failure, and the GUI frame its coordinates
// refer to. Two callers on one desktop must not move each other's
// baselines or cursors.
type sessionState struct {
	guiSnapshot               *AppSnapshot
	lastDiagnostics           map[string]map[string]string
	observations              observationLedger
	ExposedHiddenRefs         map[string]map[int]ElementRecord `json:"-"`
	FindCursorRegistry        *FindCursorRegistry              `json:"-"`
	pendingFailedObservations []failedObservationEntry
	seenAccessibilityEpoch    uint64
	// lastInventory is the latest apps listing, for its records form.
	lastInventory *axWindowInventory
	// recordSnapshots are recent records observations, for since.
	recordSnapshots    []recordSnapshotEntry
	nextRecordSnapshot uint64
}

// Service is one session's handle on a desktop. The embedded transport
// state belongs to the MCP adapter; core code never reads it.
type Service struct {
	*DesktopState
	*sessionState
	mcpTransportState
}

func NewService() *Service {
	return NewServiceWithBackend(DefaultRuntimeBackend())
}

func NewServiceWithBackend(backend PlatformBackend) *Service {
	return NewSessionService(NewDesktopState(backend))
}

func NewDesktopState(backend PlatformBackend) *DesktopState {
	if backend == nil {
		backend = DefaultRuntimeBackend()
	}
	return &DesktopState{
		Backend:           backend,
		snapshots:         map[string]*AppSnapshot{},
		RefRegistry:       NewRefRegistry(defaultAXRefRegistryLimit),
		WindowRefRegistry: NewWindowRefRegistry(defaultAXWindowRefRegistryLimit),
		AppRefRegistry:    NewAppRefRegistry(defaultAXAppRefRegistryLimit),
		Shared:            &DesktopShared{RequestResults: map[string]StoredRequest{}},
	}
}

// NewSessionService returns a new session on an existing desktop.
func NewSessionService(desktop *DesktopState) *Service {
	return &Service{
		DesktopState: desktop,
		sessionState: &sessionState{
			lastDiagnostics: map[string]map[string]string{},
			observations: observationLedger{
				lastComplete: map[string]*AppSnapshot{},
				lastFullTree: map[string]bool{},
			},
			ExposedHiddenRefs:         map[string]map[int]ElementRecord{},
			FindCursorRegistry:        NewFindCursorRegistry(defaultAXFindCursorLimit),
			pendingFailedObservations: []failedObservationEntry{},
			seenAccessibilityEpoch:    desktop.accessibilityEpoch,
		},
		mcpTransportState: mcpTransportState{ProtocolVersion: MCPProtocolLegacy},
	}
}

// isolatedProbe returns a copy whose registries can be changed without
// touching live state. The struct copies keep the semantics of the former
// shallow copy of a flat service: maps stay shared, field assignments do not.
func (s *Service) isolatedProbe() *Service {
	probe := *s
	desktop := *s.DesktopState
	desktop.RefRegistry = s.RefRegistry.clone()
	desktop.WindowRefRegistry = s.WindowRefRegistry.clone()
	desktop.AppRefRegistry = s.AppRefRegistry.clone()
	session := *s.sessionState
	probe.DesktopState = &desktop
	probe.sessionState = &session
	return &probe
}

// syncSessionWithDesktop drops this session's caches when another session's
// GUI input invalidated the desktop since this session's last call.
func (s *Service) syncSessionWithDesktop() {
	if s.seenAccessibilityEpoch == s.accessibilityEpoch {
		return
	}
	s.ExposedHiddenRefs = map[string]map[int]ElementRecord{}
	s.FindCursorRegistry.Reset()
	s.seenAccessibilityEpoch = s.accessibilityEpoch
}

// errorContextEnabled reports whether failures write an error-context file.
// It defaults to on, unlike file output mode, for the opposite reason: file
// output changes the shape of every successful call, while error context is
// written only on failure, when nothing else allows a post-mortem because the
// response holds one error line and the UI has already moved on.
func errorContextEnabled() bool {
	return !strings.EqualFold(
		strings.TrimSpace(os.Getenv("COMPUTER_USE_ERROR_CONTEXT")), "off")
}

// writeErrorContext writes the UI state at the time of a failure to disk. It
// returns a note describing the files and whether fresh UI was sampled.
//
// A one-line error cannot be diagnosed afterwards, so each failure saves a
// snapshot. On the
// desktop the UI changes a moment later, and no tree from the failure would
// survive otherwise.
//
// The state is re-captured rather than taken from the cache: failures often
// happen because the UI no longer matches the snapshot, so the cached copy
// would discard the very evidence being sought. The cost is paid only on the
// failure path.
//
// Capturing can itself fail (the app exited, or the accessibility bus
// disconnected). It then attempts no recovery and returns an empty note: a
// diagnostic facility must not add a new error when diagnosis fails.
func (s *Service) writeErrorContext(app, tool, message string) (string, bool) {
	if !errorContextEnabled() || strings.TrimSpace(app) == "" {
		return "", false
	}
	noRefs, noImage := false, false
	response, err := s.callProviderObserve(ProviderObserveRequest{
		ProtocolVersion: ProviderProtocolVersion,
		App:             app,
		Observation: ProviderObservationOptions{
			Boxes:             &noRefs,
			IncludeScreenshot: &noImage,
		},
	})
	if err != nil || response == nil || !response.OK || response.Snapshot == nil {
		return "", false
	}
	dir := filepath.Join(outputDir(), "error-context")
	if err := os.MkdirAll(dir, 0o700); err != nil {
		return "", true
	}
	if err := os.Chmod(dir, 0o700); err != nil {
		return "", true
	}
	stamp := time.Now().UnixNano()
	snapshot := response.Snapshot
	var body strings.Builder
	fmt.Fprintf(&body, "# error context\n\ntool: %s\napp: %s\ncaptured: %s\n\n",
		tool, app, time.Now().Format(time.RFC3339Nano))
	fmt.Fprintf(&body, "## error\n\n%s\n\n", message)
	fmt.Fprintf(&body, "## window\n\n%s\n\n", snapshot.WindowTitle)
	fmt.Fprintf(&body, "## tree at failure time\n\n%s\n", snapshot.renderedText())
	textPath := filepath.Join(dir, fmt.Sprintf("error-context-%d.md", stamp))
	if err := os.WriteFile(textPath, []byte(body.String()), 0o600); err != nil {
		return "", true
	}
	parts := []string{textPath}
	// The file holds the full desktop, like every AX image. It is not
	// delivered as an image, so it does not become the GUI frame.
	if frame, frameErr := s.captureDesktopFrame(); frameErr == nil {
		if raw, decodeErr := base64.StdEncoding.DecodeString(frame.ScreenshotPNGBase64); decodeErr == nil {
			extension := ".png"
			if frame.screenshotMimeType() == "image/jpeg" {
				extension = ".jpg"
			}
			shotPath := filepath.Join(dir, fmt.Sprintf("error-context-%d%s", stamp, extension))
			if os.WriteFile(shotPath, raw, 0o600) == nil {
				parts = append(parts, shotPath)
			}
		}
	}
	return fmt.Sprintf(
		"\n\n[error context written to %s \u2014 the tree and screenshot as they were "+
			"AFTER this failure, re-captured rather than taken from the cached "+
			"snapshot, because the failure usually means the UI is no longer what "+
			"the snapshot said. Set COMPUTER_USE_ERROR_CONTEXT=off to disable.]",
		strings.Join(parts, " and ")), true
}

func withErrorContextEvidence(
	result ToolCallResult,
	contextWritten bool,
) ToolCallResult {
	if result.StructuredContent == nil {
		result.StructuredContent = map[string]any{}
	}
	result.StructuredContent["error_context_captured"] = contextWritten
	result.StructuredContent["error_context_fresh_ui_sampled"] = true
	result.StructuredContent["cause_unattributed"] = true
	result.StructuredContent["error_context_fresh_state_cause"] = "unattributed"
	if contextWritten {
		result.StructuredContent["evidence_scope"] =
			"error_with_external_fresh_ui_context"
	} else {
		result.StructuredContent["evidence_scope"] =
			"error_with_unpersisted_fresh_ui_sample"
	}
	return result
}

func (s *Service) callTool(
	name string,
	args map[string]any,
) (result ToolCallResult) {
	defer func() {
		result = s.deliverAXImages(name, args, result)
	}()

	s.syncSessionWithDesktop()
	result = s.dispatch(name, args)
	if name != contract.PublicToolGUI {
		result = result.withDefaultEvidenceScope(
			toolEvidenceScope(name, args),
		)
	}
	// Capture error context in one place: errors are returned from many call
	// sites, and adding the capture to each would miss some.
	if result.IsError && name != contract.PublicToolGUI {
		// Preparation can remain queued after the failure. Error reporting must
		// not issue a hidden read or replace the explicit stop with recovery.
		if result.StructuredContent["focus_preparation"] != nil ||
			result.StructuredContent["pointer_preparation"] != nil {
			result.StructuredContent["evidence_scope"] = "error_only"
			result.StructuredContent["preflight_only"] = false
			result.StructuredContent["error_context_captured"] = false
			result.StructuredContent["error_context_fresh_ui_sampled"] = false
			return result
		}
		if result.StructuredContent["find_delivery_budget_exhausted"] == true {
			// A sampled/cached read that cannot fit is not a preflight rejection
			// and must not trigger a second, hidden UI read.
			result.StructuredContent["evidence_scope"] = result.StructuredContent["find_read_scope"]
			result.StructuredContent["preflight_only"] = false
			result.StructuredContent["input_events_emitted"] = false
			result.StructuredContent["error_context_captured"] = false
			result.StructuredContent["error_context_fresh_ui_sampled"] = false
			return result
		}
		app := requiredString(args, "app")
		recoveryObservationAttempted := result.StructuredContent != nil &&
			result.StructuredContent["recovery_observation_attempted"] == true
		if recoveryObservationAttempted {
			return result
		}
		// A no-input stale-ref failure intentionally returns no fresh UI state.
		// Capturing error context here would perform the hidden read that the
		// error contract explicitly forbids and reintroduce causal ambiguity.
		observationRequired := result.StructuredContent != nil &&
			result.StructuredContent["observation_required"] == true
		if observationRequired || s.hasPendingFailedObservation(app) {
			if result.StructuredContent == nil {
				result.StructuredContent = map[string]any{}
			}
			result.StructuredContent["observation_required"] = true
			result.StructuredContent["evidence_scope"] = "error_only"
			if !observationRequired && len(result.Content) > 0 {
				result.Content[0].Text = strings.TrimSpace(result.Content[0].Text) +
					"\n\nAn explicit AX mode=observe or mode=find call is still " +
					"required after the earlier stale-reference failure. That " +
					"failed action emitted no input and did not cause a UI change. " +
					"This error did not perform a recovery read."
			}
			return result
		}
		if result.StructuredContent != nil &&
			result.StructuredContent["assertion_sampled_live_ui"] == true {
			result.StructuredContent["evidence_scope"] =
				"independent_ui_read"
			result.StructuredContent["error_context_captured"] = false
			result.StructuredContent["error_context_fresh_ui_sampled"] = true
			result.StructuredContent["preflight_only"] = false
			result.StructuredContent["cause_unattributed"] = true
			result.StructuredContent["error_context_fresh_state_cause"] =
				"assertion_poll"
			return result
		}
		if result.StructuredContent != nil &&
			result.StructuredContent["poll_attempted"] == true {
			result.StructuredContent["evidence_scope"] = "runtime_poll"
			result.StructuredContent["error_context_captured"] = false
			result.StructuredContent["error_context_fresh_ui_sampled"] = false
			result.StructuredContent["preflight_only"] = false
			return result
		}
		if result.StructuredContent != nil &&
			result.StructuredContent["evidence_scope"] == "error_only" {
			result.StructuredContent["error_context_captured"] = false
			result.StructuredContent["error_context_fresh_ui_sampled"] = false
			if _, known := result.StructuredContent["preflight_only"]; !known {
				result.StructuredContent["preflight_only"] = true
			}
			return result
		}
		message := ""
		for _, item := range result.Content {
			if item.Type == "text" {
				message += item.Text
			}
		}
		// The accessibility bridge itself timed out. Re-observing the app for
		// error context would hit the same bridge and double the delay.
		if strings.Contains(message, "runtime_timeout=true") {
			return result
		}
		note, sampled := s.writeErrorContext(app, name, message)
		if note != "" {
			result.Content = append(result.Content, ContentItem{Type: "text", Text: note})
		}
		if sampled {
			result = withErrorContextEvidence(result, note != "")
			if note == "" {
				result.Content = append(result.Content, ContentItem{
					Type: "text",
					Text: "[error context fresh UI sampled after the failure, " +
						"but the context file could not be written. The sample " +
						"is not included here and its cause is unattributed.]",
				})
			}
		}
	}
	return result
}

func toolEvidenceScope(name string, args map[string]any) string {
	switch name {
	case contract.PublicToolGUI:
		return "full_desktop_observation"
	case contract.PublicToolAX:
		mode, _ := parseAXMode(args)
		switch mode {
		case contract.AXModeApps:
			return "app_inventory"
		case contract.AXModeObserve, contract.AXModeFind:
			return "independent_ui_read"
		default:
			return "delivery_or_local_ui"
		}
	default:
		return "delivery_or_local_ui"
	}
}

func (s *Service) dispatch(name string, args map[string]any) ToolCallResult {
	axCall, rejected, ok := s.rejectInvalidCall(name, args)
	if ok {
		return rejected
	}
	if axCall != nil {
		// Run the request decoded above; dispatchValidated would decode it
		// a second time.
		return s.axDecodedRequest(axCall)
	}
	return s.dispatchValidated(name, args)
}

// rejectInvalidCall returns the error result for a call that must not run:
// a disabled channel, AX arguments the decoder rejects, or arguments the
// published schema rejects. Nothing is sent to the provider for them. For an
// AX call that passes, it returns the decoded request so that callers do not
// decode it again.
func (s *Service) rejectInvalidCall(name string, args map[string]any) (*axRequest, ToolCallResult, bool) {
	if !contract.ToolIsEnabled(name) {
		// The tool is already absent from tools/list, but a direct call must
		// still be rejected, and the error must say that the channel is
		// disabled rather than that the tool does not exist. An ambiguous
		// error makes the agent suspect a misspelled name.
		return nil, TextResult(fmt.Sprintf(
			"tool %q belongs to the %s channel, which is disabled by COMPUTER_USE_CHANNELS. Enabled channels: %s.",
			name, contract.ToolChannel[name], strings.Join(contract.SortedChannels(), ", ")), true), true
	}
	// AX decodes its arguments first: the flat AX schema cannot say which
	// fields belong to which mode or action type, and the decoder's errors
	// name the field and show a corrected call.
	var axCall *axRequest
	if name == contract.PublicToolAX {
		request, argErr := DecodeAXRequest(args)
		if argErr != nil {
			requested := 0
			if actions, ok := args["actions"].([]any); ok {
				requested = len(actions)
			}
			return nil, axArgErrorResult(argErr, requested), true
		}
		axCall = request
	}
	// The published input schema is the static contract. Nothing below runs,
	// and no input is emitted, for arguments it rejects.
	if argErr := contract.ValidateToolArguments(name, args); argErr != nil {
		return nil, argumentErrorResult(name, args, argErr), true
	}
	return axCall, ToolCallResult{}, false
}

// dispatchValidated runs a call whose arguments already passed the published
// schema. The AX services still check what they read, so for AX this path
// alone must accept and reject the same arguments as the schema; the
// consistency test calls it directly to prove that. GUI relies on the schema
// gate for its shape rules and keeps only its cross-field rules here.
func (s *Service) dispatchValidated(name string, args map[string]any) ToolCallResult {
	switch name {
	case contract.PublicToolGUI:
		return withGUIRequested(s.dispatchGUI(args), args["actions"])
	case contract.PublicToolAX:
		return s.handleAXRequest(args)
	default:
		return TextResult(fmt.Sprintf("unsupportedTool(%q)", name), true)
	}
}

// dispatchGUI runs a GUI call whose arguments passed the published schema.
// The schema owns every shape rule (accepted fields, a non-empty batch, a
// screenshot alone and without settle_ms, the settle_ms range); this path
// only converts the values.
func (s *Service) dispatchGUI(args map[string]any) ToolCallResult {
	settleMS, err := optionalSettleMS(args)
	if err != nil {
		return guiError(contract.AXCodeInvalidArgument, "settle_ms", err.Error(), guiArgumentHint(args, "settle_ms"))
	}
	return s.gui(args["actions"], settleMS)
}
