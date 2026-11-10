package core

import (
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"sort"
	"strconv"
	"strings"
)

func appIdentityForSnapshot(snapshot *AppSnapshot) string {
	if snapshot == nil {
		return ""
	}
	identity := strings.TrimSpace(snapshot.App.BundleIdentifier)
	if identity == "" {
		identity = strings.TrimSpace(snapshot.App.Name)
	}
	return strings.ToLower(identity)
}

func semanticFingerprint(
	window string,
	role CanonicalRole,
	name string,
	semanticTarget string,
	relations map[string][]axRelationTarget,
) string {
	parts := []string{
		strings.TrimSpace(window),
		string(role),
		comparableTreeText(name),
		strings.TrimSpace(semanticTarget),
	}
	relationParts := []string{}
	for kind, targets := range relations {
		for _, target := range targets {
			relationParts = append(relationParts,
				kind+":"+strings.ToLower(strings.TrimSpace(target.Role))+":"+
					comparableTreeText(target.Name))
		}
	}
	sort.Strings(relationParts)
	parts = append(parts, relationParts...)
	sum := sha256.Sum256([]byte(strings.Join(parts, "\x00")))
	return "f1:" + hex.EncodeToString(sum[:12])
}

func completeAXProviderIdentity(
	logicalWindowRef,
	processGeneration,
	providerBusName,
	providerObjectPath,
	providerIdentityScope,
	semanticTarget string,
) string {
	if strings.TrimSpace(logicalWindowRef) == "" ||
		strings.TrimSpace(processGeneration) == "" ||
		strings.TrimSpace(providerBusName) == "" ||
		strings.TrimSpace(providerObjectPath) == "" {
		return ""
	}
	parts := []string{
		strings.TrimSpace(logicalWindowRef),
		strings.TrimSpace(processGeneration),
		strings.TrimSpace(providerBusName),
		strings.TrimSpace(providerObjectPath),
	}
	if strings.TrimSpace(providerIdentityScope) != "" {
		parts = append(
			parts,
			strings.TrimSpace(providerIdentityScope),
			strings.TrimSpace(semanticTarget),
		)
	}
	return strings.Join(parts, "\x00")
}

func semanticDiagnosticSelector(
	role CanonicalRole,
	record *ElementRecord,
) string {
	if record == nil {
		return ""
	}
	name := normalizedTreeText(record.Name)
	if name != "" {
		return selectorHead(string(role), name)
	}
	if description := normalizedTreeText(record.Description); description != "" {
		return fmt.Sprintf(
			"%s [desc=%s]",
			strings.TrimSpace(string(role)),
			strconv.Quote(description),
		)
	}
	if placeholder := normalizedTreeText(record.Placeholder); placeholder != "" {
		return fmt.Sprintf(
			"%s [placeholder=%s]",
			strings.TrimSpace(string(role)),
			strconv.Quote(placeholder),
		)
	}
	return ""
}

type axProviderIdentityConflict struct {
	reason string
}

func (err *axProviderIdentityConflict) Error() string {
	return "provider_identity_conflict: contradictory " + err.reason + " in accessibility records"
}

func conflictingNonemptyIdentity(first, second string) bool {
	first, second = strings.TrimSpace(first), strings.TrimSpace(second)
	return first != "" && second != "" && first != second
}

func validateExactProviderAlias(node *AXNode, record *ElementRecord, role CanonicalRole) error {
	if node.Role != role {
		return &axProviderIdentityConflict{reason: "roles"}
	}
	if conflictingNonemptyIdentity(node.AutomationID, record.AutomationID) {
		return &axProviderIdentityConflict{reason: "control identifiers"}
	}
	for _, pair := range [][2]string{
		{node.ProviderWindowBusName, record.ProviderWindowBusName},
		{node.ProviderWindowObjectPath, record.ProviderWindowObjectPath},
		{node.ProviderWindowProcessGeneration, record.ProviderWindowProcessGeneration},
	} {
		if conflictingNonemptyIdentity(pair[0], pair[1]) {
			return &axProviderIdentityConflict{reason: "window ownership"}
		}
	}
	return nil
}

func retainProviderAliasIdentity(node *AXNode, record *ElementRecord) {
	node.AutomationID = FirstNonEmpty(node.AutomationID, record.AutomationID)
	node.ProviderWindowBusName = FirstNonEmpty(node.ProviderWindowBusName, record.ProviderWindowBusName)
	node.ProviderWindowObjectPath = FirstNonEmpty(node.ProviderWindowObjectPath, record.ProviderWindowObjectPath)
	node.ProviderWindowProcessGeneration = FirstNonEmpty(
		node.ProviderWindowProcessGeneration, record.ProviderWindowProcessGeneration,
	)
}

func mergeExactProviderAlias(node *AXNode, record *ElementRecord) {
	if node == nil || record == nil {
		return
	}
	node.ProviderIndices = append(node.ProviderIndices, record.Index)
	node.RuntimeIDAliases = append(
		node.RuntimeIDAliases,
		append([]int(nil), record.RuntimeID...),
	)
	aliasStates := typedAXStates(record)
	node.States.Active = mergeTriState(node.States.Active, aliasStates.Active)
	node.States.Checked = mergeCheckedState(node.States.Checked, aliasStates.Checked)
	node.States.Expanded = mergeTriState(node.States.Expanded, aliasStates.Expanded)
	node.States.Selected = mergeTriState(node.States.Selected, aliasStates.Selected)
	node.States.Disabled = mergeTriState(node.States.Disabled, aliasStates.Disabled)
	node.States.Pressed = mergeTriState(node.States.Pressed, aliasStates.Pressed)
	node.States.Invalid = mergeTriState(node.States.Invalid, aliasStates.Invalid)
	if node.States.Level != nil && aliasStates.Level != nil &&
		*node.States.Level != *aliasStates.Level {
		node.States.Level = nil
	} else if node.States.Level == nil {
		node.States.Level = aliasStates.Level
	}
	aliasCapabilities := typedAXCapabilities(record, node.Role)
	if node.Capabilities.Activate == ActivationNone {
		node.Capabilities.Activate = aliasCapabilities.Activate
	} else if aliasCapabilities.Activate != ActivationNone &&
		node.Capabilities.Activate != aliasCapabilities.Activate {
		node.Capabilities.Activate = ActivationUnknown
	}
	node.Capabilities.SetValue = node.Capabilities.SetValue || aliasCapabilities.SetValue
	node.Capabilities.EditText = node.Capabilities.EditText || aliasCapabilities.EditText
	node.Capabilities.Scroll = node.Capabilities.Scroll || aliasCapabilities.Scroll
	node.Showing = mergeBoolEvidence(node.Showing, record.Showing)
	node.Visible = mergeBoolEvidence(node.Visible, record.Visible)
	node.VisibilityEvidence = mergeVisibilityEvidence(node.VisibilityEvidence, record.VisibilityEvidence)
	if node.VisibilityEvidence != nil && node.VisibilityEvidence.State == "unknown" {
		node.Showing, node.Visible = nil, nil
	}
	// Keep later owner evidence so a third alias cannot hide a conflict.
	retainProviderAliasIdentity(node, record)
}
