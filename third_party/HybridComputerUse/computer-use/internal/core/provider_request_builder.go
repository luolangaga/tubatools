package core

import "github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"

func providerObserveAppsRequestFromOperation(
	AXServiceRequest,
) ProviderObserveAppsRequest {
	return ProviderObserveAppsRequest{ProtocolVersion: ProviderProtocolVersion}
}

func providerObserveRequestFromOperation(
	operation AXServiceRequest,
) ProviderObserveRequest {
	return ProviderObserveRequest{
		ProtocolVersion: ProviderProtocolVersion,
		App:             operation.App,
		AppIdentity:     operation.AppIdentity,
		WindowHandle:    operation.TargetWindowHandle,
		Observation:     providerObservationOptionsFromOperation(operation),
		KnownRefs:       cloneProviderRefs(operation.KnownRefs),
	}
}

func providerActionRequestFromOperation(
	operation AXServiceRequest,
) ProviderActionRequest {
	if operation.Action == nil {
		panic("provider action request requires a typed action command")
	}
	request := ProviderActionRequest{
		ProtocolVersion: ProviderProtocolVersion,
		Intent:          operation.Action.Intent(),
		App:             operation.App,
		AppIdentity:     operation.AppIdentity,
		WindowHandle:    operation.TargetWindowHandle,
		ExpectedWindow:  operation.ExpectedWindow,
		Element:         operation.Element,
		Target:          providerTargetFromOperation(operation.SemanticTarget),
		WindowBounds:    operation.WindowBounds,
		Observation:     providerObservationOptionsFromOperation(operation),
		KnownRefs:       cloneProviderRefs(operation.KnownRefs),
	}
	operation.Action.applyProviderAction(&request)
	return request
}

func providerWindowActionRequestFromOperation(
	operation AXServiceRequest,
) ProviderWindowActionRequest {
	if operation.WindowAction == nil {
		panic("provider window action request requires a typed window action")
	}
	return ProviderWindowActionRequest{
		ProtocolVersion: ProviderProtocolVersion,
		Action:          *operation.WindowAction,
		App:             operation.App,
		AppIdentity:     operation.AppIdentity,
		WindowHandle:    operation.TargetWindowHandle,
		Target:          providerTargetFromOperation(operation.SemanticTarget),
		Observation:     providerObservationOptionsFromOperation(operation),
		KnownRefs:       cloneProviderRefs(operation.KnownRefs),
	}
}

func providerObservationOptionsFromOperation(
	operation AXServiceRequest,
) ProviderObservationOptions {
	var captureSnapshot *bool
	if operation.WindowAction != nil {
		// Window actions require an internal post-action snapshot for native
		// lifecycle verification even when the caller selected observe=none.
		// Observe controls model-visible output, not the evidence required to
		// prove activation or closure.
		capture := true
		captureSnapshot = &capture
	} else if operation.CaptureSnapshot != nil {
		capture := *operation.CaptureSnapshot
		captureSnapshot = &capture
	} else if operation.Action != nil {
		capture := (observation{mode: operation.Observe}).resolvedMode() !=
			contract.ObserveNone
		captureSnapshot = &capture
	}
	maxTreeNodes := operation.MaxTreeNodes
	if operation.LightReadback {
		// One record: the selected window root. The provider still selects
		// the window (a newly opened modal dialog wins), lists every
		// top-level window and settles before reading.
		maxTreeNodes = 1
	}
	// AX images are full-desktop GUI frames that the service captures after
	// the read (attachDesktopFrame), so the provider never captures one.
	noImage := false
	return ProviderObservationOptions{
		TextLimit:         providerTextLimit(operation.TextLimit),
		MaxTreeNodes:      maxTreeNodes,
		MaxTreeDepth:      operation.MaxTreeDepth,
		Prune:             operation.Prune,
		Boxes:             operation.Boxes,
		IncludeScreenshot: &noImage,
		FindWalk:          operation.FindWalk,
		FindQuery:         operation.FindQuery,
		FindResumeAfter:   operation.FindResumeAfter,
		CaptureSnapshot:   captureSnapshot,

		RecheckAfterAction: operation.RecheckAfterAction,
	}
}

func providerTextLimit(value any) *ProviderTextLimit {
	switch typed := value.(type) {
	case nil:
		return nil
	case string:
		if typed == "max" {
			return &ProviderTextLimit{Max: true}
		}
	case int:
		if typed > 0 {
			return &ProviderTextLimit{Value: typed}
		}
	case float64:
		if typed > 0 && typed == float64(int(typed)) {
			return &ProviderTextLimit{Value: int(typed)}
		}
	}
	return nil
}

func cloneProviderRefs(
	refs map[string]elementRef,
) map[string]ProviderElementRef {
	if len(refs) == 0 {
		return nil
	}
	cloned := make(map[string]ProviderElementRef, len(refs))
	for key, ref := range refs {
		cloned[key] = ref
	}
	return cloned
}

func providerTargetFromOperation(
	target *semanticTarget,
) *ProviderSemanticTarget {
	if target == nil {
		return nil
	}
	return &ProviderSemanticTarget{
		EditTarget:                      target.EditTarget,
		Selector:                        target.Selector,
		LocatorKind:                     target.LocatorKind,
		AllowProviderRebind:             target.AllowProviderRebind,
		WindowHandle:                    target.WindowHandle,
		ProviderBusName:                 target.ProviderBusName,
		ProviderObjectPath:              target.ProviderObjectPath,
		ProcessGeneration:               target.ProcessGeneration,
		ProviderWindowBusName:           target.ProviderWindowBusName,
		ProviderWindowObjectPath:        target.ProviderWindowObjectPath,
		ProviderWindowProcessGeneration: target.ProviderWindowProcessGeneration,
		ProviderWindowRole:              target.ProviderWindowRole,
		ProviderWindowName:              target.ProviderWindowName,
		ProviderRoleAliases:             append([]string(nil), target.ProviderRoleAliases...),
		ProviderIdentityScope:           target.ProviderIdentityScope,
		AutomationID:                    target.AutomationID,
		RuntimeID:                       append([]int(nil), target.RuntimeID...),
		RuntimeIDAliases:                append([][]int(nil), target.RuntimeIDAliases...),
		Role:                            target.Role,
		Name:                            target.Name,
		HasName:                         target.HasName,
		Predicates:                      append([]semanticTargetPredicate(nil), target.Predicates...),
		SourceIndex:                     target.SourceIndex,
	}
}
