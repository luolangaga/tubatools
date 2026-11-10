package core

import "github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"

// A query or text wait observes the whole application that owns the batch
// window: every visible top-level window, including a dialog or popup menu
// that the action opened as a window of its own. A ref wait keeps reading
// that one element. Reads never re-bind an input target: they only sample
// state, and the batch window stays the target of input.

// untilApplicationRead is one poll of a query or text wait.
type untilApplicationRead struct {
	// primary is the bound window, or the application's main window once the
	// bound window is gone. It is an empty snapshot when the application has
	// exited.
	primary *AppSnapshot
	// lookup is primary with its scope applied.
	lookup *AppSnapshot
	// windowRef names the one other window that is modal or has focus, or the
	// window that replaced a closed bound window. It goes into the hint of an
	// until that did not hold.
	windowRef string
	request   AXServiceRequest
}

// readUntilApplication reads the bound window. The read also returns the
// application's window inventory, which readOtherWindows uses when the
// bound window does not answer the predicate.
func (s *Service) readUntilApplication(
	request AXServiceRequest,
) (*untilApplicationRead, ToolCallResult) {
	app := request.App
	_ = s.bindPredicateAppIdentity(&request)
	boundHandle := request.TargetWindowHandle
	primary, _, result := s.readProjectedSnapshot(app, request)
	if result.IsError && boundHandle != "" && predicateWindowGone(result) {
		// The bound window closed. The application's other windows still
		// answer the predicate. If they cannot be read, the closed-window
		// result stands.
		fallback := request
		fallback.TargetWindowHandle = ""
		if snapshot, _, fallbackResult := s.readProjectedSnapshot(app, fallback); !fallbackResult.IsError {
			primary, result, request = snapshot, fallbackResult, fallback
		} else if predicateAppGone(fallbackResult) {
			result = fallbackResult
		}
	}
	if result.IsError {
		if predicateAppGone(result) {
			// A complete inventory no longer has the process: it has no
			// window and no element.
			empty := &AppSnapshot{}
			return &untilApplicationRead{
				primary: empty, lookup: empty, request: request,
			}, ToolCallResult{}
		}
		return nil, result
	}
	read := &untilApplicationRead{
		primary: primary,
		lookup:  snapshotForScopedLookup(primary),
		request: request,
	}
	if boundHandle != "" && primary.WindowHandle != boundHandle {
		read.windowRef = primary.publicWindowRef
	}
	if ref := soleFocusedOtherWindowRef(primary); ref != "" {
		read.windowRef = ref
	}
	return read, ToolCallResult{}
}

// otherWindows lists the application's visible top-level windows other than
// the one already read.
func (read *untilApplicationRead) otherWindows() []TopLevelWindow {
	return otherVisibleWindows(read.primary)
}

func otherVisibleWindows(snapshot *AppSnapshot) []TopLevelWindow {
	windows := []TopLevelWindow{}
	if snapshot == nil {
		return windows
	}
	for _, window := range snapshot.Windows {
		if window.Selected || window.Handle == snapshot.WindowHandle ||
			!(window.Showing || window.Modal || window.Active) {
			continue
		}
		windows = append(windows, window)
	}
	return windows
}

// soleFocusedOtherWindowRef returns the wN of the one other window of the
// snapshot's application that is modal or has focus, such as a dialog the
// action opened, or "" when there is none or more than one.
func soleFocusedOtherWindowRef(snapshot *AppSnapshot) string {
	focused := []string{}
	for _, window := range otherVisibleWindows(snapshot) {
		if (window.Modal || window.Active) && window.publicRef != "" {
			focused = append(focused, window.publicRef)
		}
	}
	if len(focused) != 1 {
		return ""
	}
	return focused[0]
}

// readOtherWindows reads every other visible window of the application, one
// provider observe per window, and returns their records as one lookup
// snapshot. complete is false when a window could not be read; then the lack
// of a match does not prove absence.
func (s *Service) readOtherWindows(
	read *untilApplicationRead,
) (*AppSnapshot, bool) {
	others := &AppSnapshot{App: read.primary.App, capturedAt: read.primary.capturedAt,
		Windows: read.primary.Windows}
	complete := true
	for _, window := range read.otherWindows() {
		elements, ok := s.readUntilWindow(read.request, window.Handle)
		if !ok {
			complete = false
			continue
		}
		others.Elements = append(others.Elements, elements...)
	}
	return others, complete
}

// readUntilWindow reads one more window of the application. It goes to the
// provider directly: readSnapshot would pass the bound window's known refs
// and then read this window a second time without them.
func (s *Service) readUntilWindow(
	request AXServiceRequest,
	handle string,
) ([]ElementRecord, bool) {
	if !validInternalWindowHandle(handle) {
		return nil, false
	}
	request.TargetWindowHandle = handle
	request.Scope = contract.AXScopeWindow
	request.KnownRefs = nil
	request.SourceRef = ""
	if previous := s.currentSnapshot(request.App); previous != nil {
		applyProjection(&request, previous.projection)
	}
	response, err := s.callProviderAccessibilityOperation(request)
	if err != nil || response == nil {
		return nil, false
	}
	response.PrepareForService()
	if !response.OK {
		// A window that closed after the inventory holds nothing.
		return nil, response.FailureFactsComplete &&
			predicateWindowGoneCode(response.FailureCode, response.FailureSubject)
	}
	if response.Snapshot == nil {
		return nil, false
	}
	snapshot, result := s.prepareProviderSnapshot(request, response)
	if result.IsError || snapshot == nil {
		return nil, false
	}
	return snapshot.Elements, true
}

// bindPredicateAppIdentity binds an aN for a read. When the aN was retired,
// for example because the action closed the application's last window, the
// read names the same exact process so the provider can report whether that
// process still exists. The ref itself stays retired.
func (s *Service) bindPredicateAppIdentity(request *AXServiceRequest) error {
	err := s.bindRuntimeAppIdentity(request)
	if err == nil {
		return nil
	}
	if identity := s.AppRefRegistry.ProviderIdentity(request.App); identity != nil {
		request.AppIdentity = identity
		return nil
	}
	return err
}

func predicateWindowGone(result ToolCallResult) bool {
	code, _ := result.StructuredContent["failure_code"].(string)
	subject, _ := result.StructuredContent["failure_subject"].(string)
	return predicateWindowGoneCode(code, subject)
}

func predicateWindowGoneCode(code, subject string) bool {
	return code == "stale_window_ref" || code == "stale_window" ||
		(code == "stale_target" && subject == "window")
}

func predicateAppGone(result ToolCallResult) bool {
	code, _ := result.StructuredContent["failure_code"].(string)
	subject, _ := result.StructuredContent["failure_subject"].(string)
	complete, _ := result.StructuredContent["failure_facts_complete"].(bool)
	return complete && subject == "app" &&
		(code == "stale_app_ref" || code == "stale_target")
}
