package core

import (
	"fmt"
	"strings"
	"time"
)

type RuntimeTimeoutError struct {
	Duration time.Duration `json:"-"`
	Phase    string        `json:"-"`
	Platform string        `json:"-"`
	// PreDispatch reports that the deadline passed before the request was
	// written to a provider, so the timeout cannot have emitted input.
	PreDispatch bool `json:"-"`
}

func (e *RuntimeTimeoutError) Error() string {
	return fmt.Sprintf("%s runtime timed out after %s", defaultString(e.Platform, "Linux"), e.Duration)
}

var _ error = (*RuntimeTimeoutError)(nil)

type RuntimeTransportError struct {
	Platform    string                `json:"-"`
	Operation   ProviderWireOperation `json:"-"`
	Phase       string                `json:"-"`
	RequestSent bool                  `json:"-"`
	Cause       error                 `json:"-"`
}

func (e *RuntimeTransportError) Error() string {
	message := fmt.Sprintf("%s provider transport failed during %s",
		defaultString(e.Platform, "Linux"), e.Operation)
	if e.Cause != nil {
		cause := strings.Join(strings.Fields(e.Cause.Error()), " ")
		if len(cause) > 512 {
			cause = cause[:512]
		}
		if cause != "" {
			message += ": " + cause
		}
	}
	return message
}

func (e *RuntimeTransportError) Unwrap() error { return e.Cause }

var _ error = (*RuntimeTransportError)(nil)

func accessibilityOperationCanMutate(request AXServiceRequest) bool {
	return request.WindowAction != nil ||
		(request.Action != nil && request.Action.Intent() != AccessibilityResolveTarget)
}

// countProviderTime adds the time since started to this desktop's provider
// total; records report it as timing_ms.provider.
func (s *Service) countProviderTime(started time.Time) {
	if s.DesktopState != nil {
		s.providerElapsed += time.Since(started)
	}
}

// providerTime reports the provider time this desktop has spent so far.
func (s *Service) providerTime() time.Duration {
	if s.DesktopState == nil {
		return 0
	}
	return s.providerElapsed
}

func (s *Service) platformBackend() PlatformBackend {
	if s.Backend != nil {
		return s.Backend
	}
	return DefaultRuntimeBackend()
}

func (s *Service) callProviderObserveApps(
	request ProviderObserveAppsRequest,
) (*ProviderAccessibilityResult, error) {
	defer s.countProviderTime(time.Now())
	result, err := s.platformBackend().ObserveApps(request)
	result.PrepareForService()
	return &result, err
}

func (s *Service) callProviderObserve(
	request ProviderObserveRequest,
) (*ProviderAccessibilityResult, error) {
	defer s.countProviderTime(time.Now())
	result, err := s.platformBackend().Observe(request)
	result.PrepareForService()
	return &result, err
}

func (s *Service) callProviderAction(
	request ProviderActionRequest,
) (*ProviderAccessibilityResult, error) {
	defer s.countProviderTime(time.Now())
	result, err := s.platformBackend().ResolveAndAct(request)
	result.PrepareForService()
	return &result, err
}

func (s *Service) callProviderDesktop(
	request ProviderDesktopRequest,
) (*ProviderDesktopResult, error) {
	defer s.countProviderTime(time.Now())
	result, err := s.platformBackend().ExecuteBatch(request)
	return &result, err
}

func (s *Service) callProviderWindowAction(
	request ProviderWindowActionRequest,
) (*ProviderWindowResult, error) {
	defer s.countProviderTime(time.Now())
	result, err := s.platformBackend().ActOnWindow(request)
	return &result, err
}

func accessibilityResultFromWindow(
	result ProviderWindowResult,
) ProviderAccessibilityResult {
	accessibility := ProviderAccessibilityResult{
		OK:                        result.OK,
		Text:                      result.Text,
		Error:                     result.Error,
		Notes:                     result.Notes,
		Snapshot:                  result.Snapshot,
		Failure:                   result.Failure,
		RecoveryContext:           result.RecoveryContext,
		Mutation:                  result.Mutation,
		ActionEvidence:            result.ActionEvidence,
		ExternalDesktopTransition: result.ExternalDesktopTransition,
	}
	accessibility.PrepareForService()
	return accessibility
}

func (s *Service) callProviderAccessibilityOperation(
	operation AXServiceRequest,
) (*ProviderAccessibilityResult, error) {
	domains := 0
	if operation.Operation != "" {
		domains++
	}
	if operation.Action != nil {
		domains++
	}
	if operation.WindowAction != nil {
		domains++
	}
	if domains != 1 {
		return nil, fmt.Errorf(
			"accessibility request must select exactly one typed operation domain, got %d",
			domains,
		)
	}
	if operation.WindowAction != nil {
		result, err := s.callProviderWindowAction(
			providerWindowActionRequestFromOperation(operation),
		)
		accessibility := accessibilityResultFromWindow(*result)
		return &accessibility, err
	}
	if operation.Action != nil {
		return s.callProviderAction(
			providerActionRequestFromOperation(operation),
		)
	}
	switch operation.Operation {
	case axServiceObserveApps:
		return s.callProviderObserveApps(
			providerObserveAppsRequestFromOperation(operation),
		)
	case axServiceObserve:
		return s.callProviderObserve(
			providerObserveRequestFromOperation(operation),
		)
	default:
		return nil, fmt.Errorf(
			"unsupported accessibility service operation %q",
			operation.Operation,
		)
	}
}

func (s *Service) runtimeTimeoutDiagnostic(snapshot *AppSnapshot) string {
	if snapshot == nil || snapshot.App.PID <= 0 {
		return FormatRuntimeTimeoutDiagnostic(0, false, false, false, false)
	}
	backend := s.platformBackend()
	pid := snapshot.App.PID
	processState := backend.ProcessState(pid)
	if processState == "missing" {
		return FormatRuntimeTimeoutDiagnostic(pid, true, false, true, false)
	}
	if processState == "zombie" || processState == "exited" {
		return FormatZombieRuntimeDiagnostic(pid)
	}
	windowKnown, windowAlive := backend.WindowForPID(pid)
	return FormatRuntimeTimeoutDiagnostic(
		pid, true, true, windowKnown, windowAlive,
	)
}

func FormatZombieRuntimeDiagnostic(pid int) string {
	return fmt.Sprintf(
		"a11y_responsive=false cached_pid_present=zombie "+
			"window_for_cached_pid=false cached_pid=%d. The cached process "+
			"generation is defunct and cannot become ready; do not keep retrying "+
			"the same PID. Re-observe application identity before any relaunch.",
		pid)
}

func FormatRuntimeTimeoutDiagnostic(
	pid int,
	hasProcessIdentity bool,
	processAlive bool,
	windowKnown bool,
	windowAlive bool,
) string {
	if !hasProcessIdentity {
		return "a11y_responsive=false cached_pid_present=unknown " +
			"window_for_cached_pid=unknown. " +
			"The accessibility bridge did not answer, and this MCP session has no " +
			"cached process identity for an independent liveness check."
	}
	if !processAlive {
		return fmt.Sprintf(
			"a11y_responsive=false cached_pid_present=false "+
				"window_for_cached_pid=false cached_pid=%d. The cached PID no longer "+
				"exists; the application may have exited or restarted.",
			pid)
	}
	if !windowKnown {
		return fmt.Sprintf(
			"a11y_responsive=false cached_pid_present=true "+
				"window_for_cached_pid=unknown cached_pid=%d. The cached PID exists "+
				"but the accessibility bridge did not answer; "+
				"the bounded native window probe was unavailable.",
			pid)
	}
	if windowAlive {
		return fmt.Sprintf(
			"a11y_responsive=false cached_pid_present=true "+
				"window_for_cached_pid=true cached_pid=%d. The cached PID and a managed "+
				"native window exist, but the accessibility "+
				"bridge did not answer. Inspect the GUI instead of retrying the same "+
				"A11Y call indefinitely.",
			pid)
	}
	return fmt.Sprintf(
		"a11y_responsive=false cached_pid_present=true "+
			"window_for_cached_pid=false cached_pid=%d. The cached PID exists without "+
			"a managed native window; the application may be rebuilding its UI. A bounded "+
			"retry can be appropriate.",
		pid)
}
