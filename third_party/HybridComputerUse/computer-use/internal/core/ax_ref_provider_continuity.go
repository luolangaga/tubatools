package core

import (
	"strings"
)

func (registry *RefRegistry) BindRuntimePaths(
	ref string,
	runtimeID []int,
	aliases [][]int,
) {
	if registry == nil {
		return
	}
	entry := registry.Entries[strings.TrimSpace(ref)]
	if entry == nil {
		return
	}
	entry.RuntimeID = append([]int(nil), runtimeID...)
	entry.RuntimeIDAliases = copyRuntimeIDs(aliases)
}

func (registry *RefRegistry) RefreshProviderBinding(
	ref string,
	record *ElementRecord,
	allowProviderRebind bool,
) bool {
	if registry == nil || record == nil {
		return false
	}
	entry := registry.Entries[strings.TrimSpace(ref)]
	if entry == nil || entry.Lifecycle != AXRefActive ||
		entry.LocatorKind != AXLocatorSemantic {
		return false
	}
	if processInstanceBindingChanged(
		entry, record.ProviderBusName, record.ProviderObjectPath,
		record.ProcessGeneration, record.ProviderIdentityScope,
	) {
		registry.MarkSuperseded(ref, "provider instance continuity lost")
		return false
	}
	if !providerWindowOwnershipContinuous(entry, record.ProviderWindowBusName,
		record.ProviderWindowObjectPath, record.ProviderWindowProcessGeneration) {
		return false
	}
	if !runtimePathMatchesEntry(entry, record.RuntimeID) {
		return false
	}
	if completeProviderIdentityChanged(
		entry,
		record.ProviderBusName,
		record.ProviderObjectPath,
		record.ProcessGeneration,
	) && !allowProviderRebind {
		return false
	}
	role := canonicalRoleDescriptor(record.ControlType, nil).ModelRole
	if !providerSemanticContinuity(
		entry,
		role,
		record.ControlType,
		record.Name,
		entry.SemanticTarget,
		record.AutomationID,
		record.RuntimeID,
	) {
		return false
	}
	entry.ProviderBusName = strings.TrimSpace(record.ProviderBusName)
	entry.ProviderObjectPath = strings.TrimSpace(record.ProviderObjectPath)
	entry.ProcessGeneration = strings.TrimSpace(record.ProcessGeneration)
	entry.ProviderIdentityScope =
		strings.TrimSpace(record.ProviderIdentityScope)
	entry.ProviderWindowBusName = FirstNonEmpty(record.ProviderWindowBusName, entry.ProviderWindowBusName)
	entry.ProviderWindowObjectPath = FirstNonEmpty(record.ProviderWindowObjectPath, entry.ProviderWindowObjectPath)
	entry.ProviderWindowProcessGeneration = FirstNonEmpty(
		record.ProviderWindowProcessGeneration, entry.ProviderWindowProcessGeneration,
	)
	entry.ProviderWindowRole = normalizedTreeText(record.ProviderWindowRole)
	entry.ProviderWindowName = normalizedTreeText(record.ProviderWindowName)
	entry.AutomationID = strings.TrimSpace(record.AutomationID)
	entry.ProviderRole = strings.TrimSpace(record.ControlType)
	entry.Role = role
	entry.Name = record.Name
	entry.RuntimeID = append([]int(nil), record.RuntimeID...)
	entry.RuntimeIDAliases = copyRuntimeIDs([][]int{record.RuntimeID})
	entry.StaleReason = ""
	return true
}

// Retained native objects must never fall back to semantic replacement.
func processInstanceBindingChanged(
	entry *AXRefEntry,
	providerBusName, providerObjectPath, processGeneration, scope string,
) bool {
	if entry == nil {
		return false
	}
	oldScope := strings.TrimSpace(entry.ProviderIdentityScope)
	scope = strings.TrimSpace(scope)
	if oldScope != "process_instance" && scope != "process_instance" {
		return false
	}
	if oldScope != scope {
		return true
	}
	for _, pair := range [][2]string{
		{entry.ProviderBusName, providerBusName},
		{entry.ProviderObjectPath, providerObjectPath},
		{entry.ProcessGeneration, processGeneration},
	} {
		old, current := strings.TrimSpace(pair[0]), strings.TrimSpace(pair[1])
		if old == "" || current == "" || old != current {
			return true
		}
	}
	return false
}

func providerWindowOwnershipContinuous(entry *AXRefEntry, bus, object, generation string) bool {
	if entry == nil {
		return false
	}
	for _, pair := range [][2]string{
		{entry.ProviderWindowBusName, bus},
		{entry.ProviderWindowObjectPath, object},
		{entry.ProviderWindowProcessGeneration, generation},
	} {
		// Legacy providers may omit owner fields on an action readback. Keep
		// the issuing owner, but never accept contradictory owner evidence.
		if strings.TrimSpace(entry.ProviderIdentityScope) != "process_instance" && strings.TrimSpace(pair[1]) == "" {
			continue
		}
		if old := strings.TrimSpace(pair[0]); old != "" && old != strings.TrimSpace(pair[1]) {
			return false
		}
	}
	return true
}

func completeProviderIdentityChanged(
	entry *AXRefEntry,
	providerBusName,
	providerObjectPath,
	processGeneration string,
) bool {
	if entry == nil {
		return false
	}
	oldComplete := strings.TrimSpace(entry.ProviderBusName) != "" &&
		strings.TrimSpace(entry.ProviderObjectPath) != "" &&
		strings.TrimSpace(entry.ProcessGeneration) != ""
	newComplete := strings.TrimSpace(providerBusName) != "" &&
		strings.TrimSpace(providerObjectPath) != "" &&
		strings.TrimSpace(processGeneration) != ""
	if !oldComplete || !newComplete {
		return false
	}
	return entry.ProviderBusName != strings.TrimSpace(providerBusName) ||
		entry.ProviderObjectPath != strings.TrimSpace(providerObjectPath) ||
		entry.ProcessGeneration != strings.TrimSpace(processGeneration)
}

func FirstNonEmpty(values ...string) string {
	for _, value := range values {
		if strings.TrimSpace(value) != "" {
			return strings.TrimSpace(value)
		}
	}
	return ""
}

func providerSemanticContinuity(
	entry *AXRefEntry,
	role CanonicalRole,
	providerRole,
	name,
	semanticTarget,
	automationID string,
	runtimeID []int,
) bool {
	if entry == nil {
		return false
	}
	if entry.Role != "" && role != "" && entry.Role != role {
		return false
	}
	oldRole, newRole := strings.TrimSpace(entry.ProviderRole), strings.TrimSpace(providerRole)
	if oldRole != "" && newRole != "" && !strings.EqualFold(oldRole, newRole) {
		return false
	}
	oldID, newID := strings.TrimSpace(entry.AutomationID), strings.TrimSpace(automationID)
	if oldID != "" || newID != "" {
		if oldID == "" || oldID != newID {
			return false
		}
		return len(runtimeID) == 0 || runtimePathMatchesEntry(entry, runtimeID)
	}
	if comparableTreeText(entry.Name) != comparableTreeText(name) {
		return false
	}
	return strings.TrimSpace(entry.SemanticTarget) ==
		strings.TrimSpace(semanticTarget)
}

func providerBoundProjectionContinuity(entry, current *AXRefEntry, runtimeID []int) bool {
	if entry == nil || current == nil {
		return false
	}
	legacy := func() bool {
		return providerSemanticContinuity(entry, current.Role, current.ProviderRole,
			current.Name, current.SemanticTarget, current.AutomationID, runtimeID)
	}
	if strings.TrimSpace(entry.ProviderIdentityScope) != "process_instance" &&
		strings.TrimSpace(current.ProviderIdentityScope) != "process_instance" {
		return legacy()
	}
	if entry.LocatorKind != AXLocatorProviderBound || current.LocatorKind != AXLocatorProviderBound ||
		processInstanceBindingChanged(entry, current.ProviderBusName, current.ProviderObjectPath,
			current.ProcessGeneration, current.ProviderIdentityScope) ||
		entry.Role != current.Role ||
		!strings.EqualFold(strings.TrimSpace(entry.ProviderRole), strings.TrimSpace(current.ProviderRole)) ||
		comparableTreeText(entry.Name) != comparableTreeText(current.Name) ||
		strings.TrimSpace(entry.AutomationID) != strings.TrimSpace(current.AutomationID) {
		return false
	}
	complete := entry.Role != "" && strings.TrimSpace(entry.ProviderRole) != ""
	for _, pair := range [][2]string{
		{entry.AppRef, current.AppRef},
		{entry.LogicalWindowRef, current.LogicalWindowRef},
		{entry.WindowHandle, current.WindowHandle},
		{entry.ProviderWindowBusName, current.ProviderWindowBusName},
		{entry.ProviderWindowObjectPath, current.ProviderWindowObjectPath},
		{entry.ProviderWindowProcessGeneration, current.ProviderWindowProcessGeneration},
	} {
		old, next := strings.TrimSpace(pair[0]), strings.TrimSpace(pair[1])
		if old != next {
			return false
		}
		complete = complete && old != ""
	}
	if complete {
		// A retained native object may move within its window's projection.
		return true
	}
	return strings.TrimSpace(entry.SemanticTarget) == strings.TrimSpace(current.SemanticTarget) &&
		(len(runtimeID) == 0 || runtimePathMatchesEntry(entry, runtimeID)) && legacy()
}

func runtimePathEqual(first, second []int) bool {
	if len(first) != len(second) {
		return false
	}
	for index := range first {
		if first[index] != second[index] {
			return false
		}
	}
	return true
}

func runtimePathMatchesEntry(entry *AXRefEntry, runtimeID []int) bool {
	if entry == nil {
		return false
	}
	if runtimePathEqual(entry.RuntimeID, runtimeID) {
		return true
	}
	for _, alias := range entry.RuntimeIDAliases {
		if runtimePathEqual(alias, runtimeID) {
			return true
		}
	}
	return false
}
