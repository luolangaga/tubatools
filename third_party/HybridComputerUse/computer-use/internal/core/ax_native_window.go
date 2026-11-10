package core

import (
	"errors"
	"fmt"
	"strconv"
	"strings"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// A window can lack an accessibility tree even when another window of its
// process exports one. The apps listing gives it a wN so that
// activate, close and window waits find it by title; reads and element
// actions say that it has no tree and point to GUI.

const axNoTreeWindowMark = "(no accessibility tree: use GUI)"

// axNoTreeWindowError is a read or action aimed at such a window.
type axNoTreeWindowError struct {
	ref string
}

func (err *axNoTreeWindowError) Error() string {
	return err.ref + " has no accessibility tree. " +
		"Read and operate it with GUI; AX can only activate, " +
		"close or wait for this window."
}

func axNoTreeWindowResult(err *axNoTreeWindowError, path string, requested int) ToolCallResult {
	return axArgErrorResult(&axArgError{
		Code:    contract.AXCodeAppUnavailable,
		path:    path,
		Message: err.Error(),
		Hint:    GUIScreenshotCall,
	}, requested)
}

// noTreeWindowEntry returns the registry entry of ref when it names a
// window without an accessibility tree.
func (s *Service) noTreeWindowEntry(ref string) *AXWindowRefEntry {
	if !isShortAXWindowRef(ref) {
		return nil
	}
	entry, err := s.WindowRefRegistry.Resolve(ref, "")
	if err != nil || entry == nil || !entry.NoAccessibilityTree {
		return nil
	}
	return entry
}

// axNoTreeBatchWindow returns the window without a tree that a batch names
// through window_ref or an activate or close target, or nil.
func (s *Service) axNoTreeBatchWindow(actions []axBatchAction, windowRef string) *AXWindowRefEntry {
	if entry := s.noTreeWindowEntry(windowRef); entry != nil {
		return entry
	}
	for _, action := range actions {
		if entry := s.noTreeWindowEntry(action.targetRef); entry != nil {
			return entry
		}
	}
	return nil
}

// axNoTreeWindowBatch runs a batch on a window without an accessibility
// tree. Only activate, close and window waits can work there; anything
// else is rejected before the first action, so no input is sent.
func (s *Service) axNoTreeWindowBatch(
	actions []axBatchAction,
	window *AXWindowRefEntry,
	notes []string,
	requested int,
) ToolCallResult {
	for index, action := range actions {
		switch action.actionType {
		case "activate", "close":
			if action.targetRef == "" || s.noTreeWindowEntry(action.targetRef) != nil {
				continue
			}
			return axArgErrorResult(axInvalid(fmt.Sprintf("actions[%d]", index),
				"one call cannot act on a window without an accessibility tree "+
					"and on other windows; split it into separate calls",
				`{"mode":"act","actions":[{"type":"activate","ref":"`+window.Ref+`"}]}`), requested)
		case "wait":
			if action.wait != nil && action.wait.window != "" {
				continue
			}
		}
		return axNoTreeWindowResult(&axNoTreeWindowError{ref: window.Ref},
			fmt.Sprintf("actions[%d]", index), requested)
	}
	lines := append([]string{}, notes...)
	emitted := false
	closed := map[string]bool{}
	for index, action := range actions {
		if action.actionType == "wait" {
			result := s.runAXWait(&AXBatchContext{WindowRef: window.Ref}, action.wait)
			if !axPostconditionPassed(result) {
				return axNoTreeBatchStop(result, index, requested, emitted, lines)
			}
			lines = append(lines, firstLine(ResultText(result)))
			continue
		}
		target := window
		if action.targetRef != "" {
			target = s.noTreeWindowEntry(action.targetRef)
		}
		result := s.axNoTreeWindowCommand(action.actionType, target)
		if result.IsError {
			return axNoTreeBatchStop(result, index, requested, emitted, lines)
		}
		emitted = true
		verb := "activated"
		if action.actionType == "close" {
			verb = "closed"
			closed[target.Ref] = true
		}
		lines = append(lines, verb+" "+target.Ref)
	}
	if !closed[window.Ref] {
		lines = append(lines, window.Ref+" "+axNoTreeWindowMark)
	}
	result := TextResult(strings.Join(lines, "\n"), false)
	result.StructuredContent = map[string]any{
		"input_events_emitted":       emitted,
		"final_observation_returned": false,
		"final_observation_kind":     "none",
		"observation_required":       false,
		"evidence_scope":             "delivery_or_local_ui",
		"a11y_batch_result": map[string]any{
			"actions_requested":                    requested,
			"actions_completed_without_call_error": len(actions),
		},
	}
	return result
}

// axNoTreeWindowCommand asks the provider to activate or close the native
// window and to confirm the effect with the window manager.
func (s *Service) axNoTreeWindowCommand(action string, window *AXWindowRefEntry) ToolCallResult {
	if window == nil {
		return TextResult("the window is no longer listed; call AX apps", true)
	}
	operation := WindowOperation(action)
	request := ProviderWindowActionRequest{
		ProtocolVersion: ProviderProtocolVersion,
		Action:          operation,
		App:             window.AppIdentity,
		WindowHandle:    window.WindowHandle,
		Target: &ProviderSemanticTarget{
			Selector:     "window " + strconv.Quote(window.ProviderWindowName),
			HasName:      window.ProviderWindowName != "",
			Name:         window.ProviderWindowName,
			Role:         "window",
			WindowHandle: window.WindowHandle,
		},
	}
	response, err := s.callProviderWindowAction(request)
	if err == nil && response != nil && response.OK {
		return TextResult(action, false)
	}
	message := action + " failed"
	if err != nil {
		message = err.Error()
	} else if response != nil && strings.TrimSpace(response.Error) != "" {
		message = strings.TrimSpace(response.Error)
	}
	result := TextResult(message, true)
	result.StructuredContent = map[string]any{
		"failure_code":         "effect_unknown",
		"input_events_emitted": true,
		"mutation_unknown":     true,
	}
	if response != nil && response.Failure != nil {
		result.StructuredContent["failure_code"] = response.Failure.Code
		result.StructuredContent["failure_phase"] = response.Failure.Phase
		emitted := response.Mutation != nil && response.Mutation.InputEventsEmitted
		result.StructuredContent["input_events_emitted"] = emitted
		result.StructuredContent["mutation_unknown"] = emitted
	}
	return result
}

func axNoTreeBatchStop(
	result ToolCallResult,
	index, requested int,
	emitted bool,
	lines []string,
) ToolCallResult {
	if result.StructuredContent == nil {
		result.StructuredContent = map[string]any{}
	}
	if emitted {
		result.StructuredContent["input_events_emitted"] = true
	}
	result.StructuredContent["a11y_batch_result"] = map[string]any{
		"actions_requested":                    requested,
		"actions_completed_without_call_error": index,
		"failed_action_index":                  index,
	}
	if len(lines) > 0 {
		result.Content = append(
			[]ContentItem{{Type: "text", Text: strings.Join(lines, "\n")}},
			result.Content...,
		)
	}
	return result
}

// asNoTreeWindowError reports whether err is a read of a window without a tree.
func asNoTreeWindowError(err error) (*axNoTreeWindowError, bool) {
	var noTree *axNoTreeWindowError
	ok := errors.As(err, &noTree)
	return noTree, ok
}
