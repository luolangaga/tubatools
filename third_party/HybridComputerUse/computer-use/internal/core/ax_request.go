package core

import (
	"errors"
	"fmt"
	"strings"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

type axReadContext struct {
	appToken     string
	appRef       string
	windowRef    string
	windowHandle string
}

// parseAXMode returns the public mode of a call, or an error when it is
// missing or unknown. Only exact values are accepted.
func parseAXMode(args map[string]any) (string, error) {
	mode, _ := args["mode"].(string)
	switch mode {
	case contract.AXModeApps, contract.AXModeObserve, contract.AXModeFind, contract.AXModeAct:
		return mode, nil
	}
	return "", errors.New("mode must be apps, observe, find, or act")
}

// resolveAXReadIdentity binds a read to one app or window. app accepts an
// application name, app id, PID, or an aN ref from apps; window_ref names
// one window. A target rN supplies the identity when neither is given.
func (s *Service) resolveAXReadIdentity(
	app, windowRef, target string,
) (*axReadContext, error) {
	context := &axReadContext{}
	if isShortAXRef(target) {
		entry, err := s.resolveShortAXRefOwned(target)
		if err != nil {
			return nil, err
		}
		context.appRef = entry.AppRef
		context.windowRef = entry.LogicalWindowRef
		context.windowHandle = entry.WindowHandle
		if windowRef != "" && entry.LogicalWindowRef != windowRef {
			return nil, contract.NewArgumentRuleError(contract.ArgumentRuleRefOwnsIdentity, fmt.Sprintf(
				"%s belongs to window %s, not %s; omit window_ref or use %s",
				target, entry.LogicalWindowRef, windowRef, entry.LogicalWindowRef,
			))
		}
	}
	if windowRef != "" {
		entry, err := s.resolveShortAXWindowRefOwned(windowRef)
		if err != nil {
			return nil, err
		}
		if entry.NoAccessibilityTree {
			return nil, &axNoTreeWindowError{ref: entry.Ref}
		}
		context.appRef = entry.AppRef
		context.windowRef = entry.Ref
		context.windowHandle = entry.WindowHandle
		context.appToken = entry.AppRef
		if context.appToken == "" {
			context.appToken = entry.AppIdentity
		}
		return context, nil
	}
	if isShortAXAppRef(app) {
		if _, err := s.resolveShortAXAppRef(app); err != nil {
			return nil, err
		}
		if context.appRef != "" && context.appRef != app {
			return nil, contract.NewArgumentRuleError(contract.ArgumentRuleRefOwnsIdentity,
				fmt.Sprintf("%s belongs to app %s, not %s", target, context.appRef, app))
		}
		context.appRef = app
		context.appToken = app
		return context, nil
	}
	if context.appRef != "" {
		if app != "" {
			return nil, contract.NewArgumentRuleError(contract.ArgumentRuleRefOwnsIdentity,
				target+" already identifies its app; omit app")
		}
		context.appToken = context.appRef
		return context, nil
	}
	if app == "" {
		return nil, errors.New("app or window_ref is required")
	}
	context.appToken = app
	return context, nil
}

func WithAXModeEvidence(
	result ToolCallResult,
	mode string,
) ToolCallResult {
	if result.StructuredContent == nil {
		result.StructuredContent = map[string]any{}
	}
	result.StructuredContent["ax_mode"] = mode
	return result
}

func (s *Service) handleAXRequest(args map[string]any) ToolCallResult {
	request, argErr := DecodeAXRequest(args)
	if argErr != nil {
		requested := 0
		if actions, ok := args["actions"].([]any); ok {
			requested = len(actions)
		}
		return axArgErrorResult(argErr, requested)
	}
	return s.axDecodedRequest(request)
}

// axDecodedRequest runs an AX call whose arguments are already decoded.
// dispatch decodes each call once and passes the request here.
func (s *Service) axDecodedRequest(request *axRequest) ToolCallResult {
	obs := observation{mode: request.observe}
	switch request.mode {
	case contract.AXModeApps:
		return WithAXModeEvidence(
			s.listApps().withEvidenceScope("app_inventory"),
			contract.AXModeApps,
		)
	case contract.AXModeObserve:
		return s.axObserve(request, obs)
	case contract.AXModeFind:
		return s.axFind(request)
	default:
		return s.axAct(request, obs)
	}
}

func (s *Service) axObserve(
	request *axRequest,
	obs observation,
) ToolCallResult {
	var root map[string]any
	switch {
	case request.root == "focused":
		root = map[string]any{"focused": true}
	case request.root != "":
		root = map[string]any{"ref": request.root}
	}
	var includeScreenshot *bool
	if obs.resolvedMode() == contract.ObserveScreenshot ||
		obs.resolvedMode() == contract.ObserveFull {
		enabled := true
		includeScreenshot = &enabled
	}
	readContext, identityErr := s.resolveAXReadIdentity(request.app, request.windowRef, "")
	if identityErr != nil {
		return axIdentityErrorResult(identityErr)
	}
	if obs.mode == contract.ObserveDiff && root == nil {
		return WithAXModeEvidence(
			s.axDiffObservation(readContext, request.scope).
				withEvidenceScope("independent_ui_read"),
			contract.AXModeObserve,
		)
	}
	// observe "full" always returns the whole tree, even when an earlier
	// result already delivered the identical state.
	forceFull := obs.resolvedMode() == contract.ObserveFull
	result := s.getAppState(
		readContext.appToken,
		nil, nil, nil, nil,
		includeScreenshot,
		&forceFull,
		contract.TreeViewWorking,
		request.scope,
		root,
		readContext.windowHandle,
	).withEvidenceScope("independent_ui_read")
	return WithAXModeEvidence(result, contract.AXModeObserve)
}

// axDiffObservation reads the window and renders it as a diff against the
// last tree delivered for it, falling back to the working tree.
func (s *Service) axDiffObservation(
	readContext *axReadContext,
	scope string,
) ToolCallResult {
	noScreenshot := false
	request := AXServiceRequest{
		Operation:          axServiceObserve,
		App:                readContext.appToken,
		IncludeScreenshot:  &noScreenshot,
		TargetWindowHandle: readContext.windowHandle,
		SourceRef:          readContext.windowRef,
		Scope:              normalizedAXScope(scope),
		TreeView:           contract.TreeViewWorking,
	}
	snapshot, notes, result := s.refreshSnapshot(readContext.appToken, request)
	if result.IsError {
		return result
	}
	if s.deliveredSnapshotFor(snapshot) == nil {
		// Nothing was delivered for this window yet, so there is no diff
		// baseline: return the tree.
		return s.getAppStateResult(snapshot, notes, false)
	}
	return s.renderAXBatchFinalObservation(snapshot, notes, observation{mode: contract.ObserveDiff})
}

func (s *Service) axFind(request *axRequest) ToolCallResult {
	if request.cursor != "" {
		return WithAXModeEvidence(
			s.continueFind(request.cursor, request.limit),
			contract.AXModeFind,
		)
	}
	readContext, identityErr := s.resolveAXReadIdentity(request.app, request.windowRef, "")
	if identityErr != nil {
		return axIdentityErrorResult(identityErr)
	}
	query := request.query
	name := OptionalString(query, "name_contains")
	_, nameExact := query["name_equals"]
	if nameExact {
		name = OptionalString(query, "name_equals")
	}
	text := OptionalString(query, "text_contains")
	_, textExact := query["text_equals"]
	if textExact {
		text = OptionalString(query, "text_equals")
	}
	result := s.find(
		readContext.appToken,
		OptionalString(query, "role"),
		name,
		text,
		OptionalString(query, "state"),
		request.limit,
		nameExact,
		textExact,
		request.scope,
		readContext.windowHandle,
	).withEvidenceScope("independent_ui_read")
	return WithAXModeEvidence(result, contract.AXModeFind)
}

func (s *Service) axAct(
	request *axRequest,
	obs observation,
) ToolCallResult {
	result := s.axBatchDecoded(
		request.actions,
		obs,
		request.windowRef,
	).withEvidenceScope("delivery_or_local_ui")
	return WithAXModeEvidence(result, contract.AXModeAct)
}

// axIdentityErrorResult reports a read identity that could not be bound.
func axIdentityErrorResult(err error) ToolCallResult {
	if noTree, ok := asNoTreeWindowError(err); ok {
		return axNoTreeWindowResult(noTree, "window_ref", 0)
	}
	var lifecycle *axRefLifecycleError
	if errors.As(err, &lifecycle) {
		return axStaleRefResult("", err)
	}
	if strings.Contains(err.Error(), "never issued") ||
		strings.Contains(err.Error(), "invalid short AX") ||
		strings.Contains(err.Error(), "expired") {
		return axStaleRefResult("", err)
	}
	argErr := axInvalid("app", err.Error(), `{"mode":"apps"}`)
	var rule *contract.ArgumentRuleError
	if errors.As(err, &rule) {
		argErr.rule = rule.Code
	}
	return axArgErrorResult(argErr, 0)
}

// axStaleRefResult reports a ref that no longer resolves. Nothing was sent.
func axStaleRefResult(ref string, err error) ToolCallResult {
	message := err.Error()
	if ref != "" && !strings.Contains(message, ref) {
		message = ref + ": " + message
	}
	result := TextResult(message, true)
	result.StructuredContent = map[string]any{
		contract.AXPublicErrorCodeKey:   contract.AXCodeStaleRef,
		contract.AXPublicErrorDetailKey: message,
		contract.AXPublicErrorHintKey:   `{"mode":"observe","window_ref":"wN"}`,
		"failure_code":                  "stale_target",
		"input_events_emitted":          false,
		"action_attempted":              false,
		"preflight_only":                true,
		"evidence_scope":                "error_only",
		"observation_required":          true,
	}
	return result
}
