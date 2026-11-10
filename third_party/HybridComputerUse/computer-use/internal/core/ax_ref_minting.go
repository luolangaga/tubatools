package core

import (
	"strconv"
	"strings"
)

func refIdentityKey(
	locatorKind AXLocatorKind,
	appIdentity,
	logicalWindowRef,
	windowHandle,
	semanticTarget string,
	role CanonicalRole,
	name,
	providerBusName,
	providerObjectPath,
	processGeneration,
	providerWindowBusName,
	providerWindowObjectPath,
	providerWindowProcessGeneration,
	providerIdentityScope string,
) string {
	if locatorKind == AXLocatorProviderBound &&
		strings.TrimSpace(providerBusName) != "" &&
		strings.TrimSpace(providerObjectPath) != "" &&
		strings.TrimSpace(processGeneration) != "" {
		windowIdentity := strings.TrimSpace(logicalWindowRef)
		if windowIdentity == "" {
			windowIdentity = strings.TrimSpace(windowHandle)
		}
		parts := []string{
			string(locatorKind),
			strings.ToLower(strings.TrimSpace(appIdentity)),
			windowIdentity,
			strings.TrimSpace(processGeneration),
			strings.TrimSpace(providerBusName),
			strings.TrimSpace(providerObjectPath),
			strings.TrimSpace(providerIdentityScope),
		}
		if strings.TrimSpace(providerIdentityScope) == "table_owner" {
			parts = append(parts, strings.TrimSpace(semanticTarget))
		}
		return strings.Join(parts, "\x00")
	}
	return strings.Join([]string{
		string(locatorKind),
		strings.ToLower(strings.TrimSpace(appIdentity)),
		FirstNonEmpty(strings.TrimSpace(logicalWindowRef), strings.TrimSpace(windowHandle)),
		strings.TrimSpace(semanticTarget),
	}, "\x00")
}

// refProviderIdentity is the optional provider identity that AcquireTyped
// receives as positional strings. Positions 8 to 12 are trimmed when read;
// positions 0 to 7 keep their raw value, which the continuity checks see.
type refProviderIdentity struct {
	busName, objectPath, processGeneration                   string
	windowBusName, windowObjectPath, windowProcessGeneration string
	windowRole, windowName                                   string
	appRef, logicalWindowRef, scope                          string
	automationID, providerRole                               string
}

func parseRefProviderIdentity(values []string) refProviderIdentity {
	at := func(index int) string {
		if len(values) > index {
			return values[index]
		}
		return ""
	}
	return refProviderIdentity{
		busName:                 at(0),
		objectPath:              at(1),
		processGeneration:       at(2),
		windowBusName:           at(3),
		windowObjectPath:        at(4),
		windowProcessGeneration: at(5),
		windowRole:              at(6),
		windowName:              at(7),
		appRef:                  strings.TrimSpace(at(8)),
		logicalWindowRef:        strings.TrimSpace(at(9)),
		scope:                   strings.TrimSpace(at(10)),
		automationID:            strings.TrimSpace(at(11)),
		providerRole:            strings.TrimSpace(at(12)),
	}
}

// complete reports whether the identity names one live provider object.
func (identity refProviderIdentity) complete() bool {
	return strings.TrimSpace(identity.busName) != "" &&
		strings.TrimSpace(identity.objectPath) != "" &&
		strings.TrimSpace(identity.processGeneration) != ""
}

// refAcquisition is one AcquireTyped request after its arguments are read.
type refAcquisition struct {
	locatorKind    AXLocatorKind
	appIdentity    string
	windowHandle   string
	semanticTarget string
	role           CanonicalRole
	name           string
	revision       string
	provider       refProviderIdentity
}

func (registry *RefRegistry) AcquireTyped(
	locatorKind AXLocatorKind,
	appIdentity,
	windowHandle,
	semanticTarget string,
	role CanonicalRole,
	name,
	revision string,
	providerIdentity ...string,
) string {
	if registry == nil || strings.TrimSpace(windowHandle) == "" {
		return ""
	}
	if locatorKind == "" {
		locatorKind = AXLocatorSemantic
	}
	request := refAcquisition{
		locatorKind:    locatorKind,
		appIdentity:    appIdentity,
		windowHandle:   windowHandle,
		semanticTarget: semanticTarget,
		role:           role,
		name:           name,
		revision:       revision,
		provider:       parseRefProviderIdentity(providerIdentity),
	}
	provider := request.provider
	providerComplete := provider.complete()
	if locatorKind == AXLocatorProviderBound && !providerComplete {
		return ""
	}
	if locatorKind != AXLocatorProviderBound &&
		strings.TrimSpace(semanticTarget) == "" {
		return ""
	}
	registry.tick++
	ownerIdentity := appIdentity
	if provider.appRef != "" {
		ownerIdentity = provider.appRef
	}
	key := refIdentityKey(
		locatorKind, ownerIdentity, provider.logicalWindowRef, windowHandle,
		semanticTarget, role, name,
		provider.busName, provider.objectPath, provider.processGeneration,
		provider.windowBusName, provider.windowObjectPath,
		provider.windowProcessGeneration, provider.scope,
	)
	if ref, reused := registry.reuseIdentityRef(key, request); reused {
		return ref
	}
	if provider.scope == "process_instance" && !providerComplete {
		return ""
	}
	return registry.mintRef(key, request)
}

// reuseIdentityRef returns the ref already issued for key when its entry is
// still live and the provider object and window are continuous. Otherwise it
// retires the entry, drops key from the identity index and reports false.
func (registry *RefRegistry) reuseIdentityRef(key string, request refAcquisition) (string, bool) {
	ref := registry.byIdentity[key]
	if ref == "" {
		return "", false
	}
	provider := request.provider
	if entry := registry.Entries[ref]; entry != nil {
		if entry.Lifecycle != AXRefActive &&
			entry.Lifecycle != AXRefNeedsRevalidation {
			delete(registry.byIdentity, key)
		} else {
			if processInstanceBindingChanged(entry, provider.busName, provider.objectPath,
				provider.processGeneration, provider.scope) ||
				!providerWindowOwnershipContinuous(entry, provider.windowBusName,
					provider.windowObjectPath, provider.windowProcessGeneration) ||
				entry.WindowHandle != strings.TrimSpace(request.windowHandle) {
				registry.MarkSuperseded(ref, "provider instance or window continuity lost")
			} else if request.locatorKind == AXLocatorProviderBound &&
				provider.complete() &&
				!providerBoundProjectionContinuity(entry, &AXRefEntry{
					LocatorKind: request.locatorKind, AppRef: provider.appRef,
					LogicalWindowRef: provider.logicalWindowRef, WindowHandle: request.windowHandle,
					ProviderBusName: provider.busName, ProviderObjectPath: provider.objectPath,
					ProcessGeneration: provider.processGeneration, ProviderIdentityScope: provider.scope,
					ProviderWindowBusName:           provider.windowBusName,
					ProviderWindowObjectPath:        provider.windowObjectPath,
					ProviderWindowProcessGeneration: provider.windowProcessGeneration,
					Role:                            request.role, ProviderRole: provider.providerRole, Name: request.name,
					AutomationID: provider.automationID, SemanticTarget: request.semanticTarget,
				}, nil) {
				entry.Lifecycle = AXRefSuperseded
				entry.StaleReason = "provider identity now exposes a different semantic object"
				delete(registry.byIdentity, key)
			} else {
				registry.refreshReusedEntry(entry, request)
				return ref, true
			}
		}
	}
	delete(registry.byIdentity, key)
	return "", false
}

// refreshReusedEntry records the current observation on a reused entry. A
// semantic ref keeps its old provider object when the provider identity
// changed; a provider-bound ref takes the current role, name and target.
func (registry *RefRegistry) refreshReusedEntry(entry *AXRefEntry, request refAcquisition) {
	provider := request.provider
	entry.AppRef = provider.appRef
	entry.LogicalWindowRef = provider.logicalWindowRef
	providerChanged := request.locatorKind == AXLocatorSemantic &&
		completeProviderIdentityChanged(
			entry,
			provider.busName,
			provider.objectPath,
			provider.processGeneration,
		)
	if !providerChanged {
		entry.ProviderBusName = strings.TrimSpace(provider.busName)
		entry.ProviderObjectPath = strings.TrimSpace(provider.objectPath)
		entry.ProcessGeneration = strings.TrimSpace(provider.processGeneration)
		entry.ProviderIdentityScope = provider.scope
	}
	entry.LastSeenRevision = request.revision
	entry.LastUsedTick = registry.tick
	entry.ProviderWindowRole = normalizedTreeText(provider.windowRole)
	entry.ProviderWindowName = normalizedTreeText(provider.windowName)
	if request.locatorKind == AXLocatorProviderBound {
		entry.AutomationID = provider.automationID
		entry.ProviderRole = provider.providerRole
		entry.Role = request.role
		entry.Name = request.name
		entry.SemanticTarget = strings.TrimSpace(request.semanticTarget)
	}
	entry.Lifecycle = AXRefActive
	entry.StaleReason = ""
}

// mintRef issues the next rN for key and indexes its entry.
func (registry *RefRegistry) mintRef(key string, request refAcquisition) string {
	provider := request.provider
	ref := "r" + strconv.FormatUint(registry.Next, 10)
	registry.Next++
	entry := &AXRefEntry{
		Ref:                             ref,
		IdentityKey:                     key,
		LocatorKind:                     request.locatorKind,
		Lifecycle:                       AXRefActive,
		AppRef:                          strings.TrimSpace(provider.appRef),
		AppIdentity:                     strings.ToLower(strings.TrimSpace(request.appIdentity)),
		LogicalWindowRef:                provider.logicalWindowRef,
		WindowHandle:                    strings.TrimSpace(request.windowHandle),
		ProviderBusName:                 strings.TrimSpace(provider.busName),
		ProviderObjectPath:              strings.TrimSpace(provider.objectPath),
		ProcessGeneration:               strings.TrimSpace(provider.processGeneration),
		ProviderIdentityScope:           provider.scope,
		ProviderWindowBusName:           strings.TrimSpace(provider.windowBusName),
		ProviderWindowObjectPath:        strings.TrimSpace(provider.windowObjectPath),
		ProviderWindowProcessGeneration: strings.TrimSpace(provider.windowProcessGeneration),
		ProviderWindowRole:              normalizedTreeText(provider.windowRole),
		ProviderWindowName:              normalizedTreeText(provider.windowName),
		SemanticTarget:                  strings.TrimSpace(request.semanticTarget),
		AutomationID:                    provider.automationID,
		ProviderRole:                    provider.providerRole,
		Role:                            request.role,
		Name:                            request.name,
		CreatedRevision:                 request.revision,
		LastSeenRevision:                request.revision,
		LastUsedTick:                    registry.tick,
	}
	registry.Entries[ref] = entry
	registry.byIdentity[key] = ref
	registry.evict()
	return ref
}

func (registry *RefRegistry) AcquireNode(
	snapshot *AXSnapshot,
	node *AXNode,
	locatorKind AXLocatorKind,
) string {
	if registry == nil || snapshot == nil || node == nil {
		return ""
	}
	if locatorKind == AXLocatorSemantic &&
		snapshot.IdentityCoverageIncomplete {
		if !axNodeProviderIdentityComplete(node) {
			return ""
		}
		locatorKind = AXLocatorProviderBound
	}
	if locatorKind == AXLocatorProviderBound ||
		locatorKind == AXLocatorSemantic && !snapshot.IdentityCoverageIncomplete {
		key := refIdentityKey(
			locatorKind,
			FirstNonEmpty(snapshot.AppRef, snapshot.AppIdentity),
			snapshot.WindowRef,
			snapshot.WindowHandle,
			node.SemanticTarget,
			node.Role,
			node.Name,
			node.ProviderBusName,
			node.ProviderObjectPath,
			node.ProcessGeneration,
			node.ProviderWindowBusName,
			node.ProviderWindowObjectPath,
			node.ProviderWindowProcessGeneration,
			node.ProviderIdentityScope,
		)
		if existingRef := registry.byIdentity[key]; existingRef != "" {
			entry := registry.Entries[existingRef]
			if entry != nil && node.SourceRecord != nil && !sameComboEditTarget(entry.EditTarget, node.SourceRecord.EditTarget) {
				registry.MarkSuperseded(existingRef, "composite control editing target changed")
			}
			providerChanged := completeProviderIdentityChanged(
				entry,
				node.ProviderBusName,
				node.ProviderObjectPath,
				node.ProcessGeneration,
			)
			boundDiscontinuity := locatorKind == AXLocatorProviderBound &&
				!providerBoundProjectionContinuity(entry, &AXRefEntry{
					LocatorKind: locatorKind, AppRef: snapshot.AppRef,
					LogicalWindowRef: snapshot.WindowRef, WindowHandle: snapshot.WindowHandle,
					ProviderBusName: node.ProviderBusName, ProviderObjectPath: node.ProviderObjectPath,
					ProcessGeneration: node.ProcessGeneration, ProviderIdentityScope: node.ProviderIdentityScope,
					ProviderWindowBusName:           node.ProviderWindowBusName,
					ProviderWindowObjectPath:        node.ProviderWindowObjectPath,
					ProviderWindowProcessGeneration: node.ProviderWindowProcessGeneration,
					Role:                            node.Role, ProviderRole: node.ProviderRole, Name: node.Name,
					AutomationID: node.AutomationID, SemanticTarget: node.SemanticTarget,
				}, node.RuntimeID)
			if processInstanceBindingChanged(entry, node.ProviderBusName, node.ProviderObjectPath,
				node.ProcessGeneration, node.ProviderIdentityScope) || boundDiscontinuity ||
				providerChanged && !runtimePathMatchesEntry(entry, node.RuntimeID) {
				// A complete fresh snapshot must never advertise an rN that the
				// live resolver will immediately reject. A different provider
				// identity outside the recorded runtime lineage is a replacement,
				// not proven continuity: retire the old ref and issue a new one.
				registry.MarkSuperseded(
					existingRef,
					"complete snapshot observed replacement element identity",
				)
			}
		}
	}
	// Display normalization must not alter the label bound to a native object.
	name := node.Name
	if node.SourceRecord != nil {
		name = node.SourceRecord.Name
	}
	ref := registry.AcquireTyped(
		locatorKind,
		snapshot.AppIdentity,
		snapshot.WindowHandle,
		node.SemanticTarget,
		node.Role,
		name,
		snapshot.Revision,
		node.ProviderBusName,
		node.ProviderObjectPath,
		node.ProcessGeneration,
		node.ProviderWindowBusName,
		node.ProviderWindowObjectPath,
		node.ProviderWindowProcessGeneration,
		node.ProviderWindowRole,
		node.ProviderWindowName,
		snapshot.AppRef,
		snapshot.WindowRef,
		node.ProviderIdentityScope,
		node.AutomationID,
		node.ProviderRole,
	)
	if ref != "" {
		entry := registry.Entries[ref]
		if locatorKind == AXLocatorSemantic &&
			completeProviderIdentityChanged(
				entry,
				node.ProviderBusName,
				node.ProviderObjectPath,
				node.ProcessGeneration,
			) {
			// A fresh snapshot may observe a rebuilt provider wrapper before
			// the action resolver has proved object continuity. Preserve the
			// original runtime-path lineage until that live, window-scoped
			// check succeeds. Otherwise a later same-name peer could replace
			// the evidence needed to reject an unsafe rebind.
			return ref
		}
		if node.SourceRecord != nil {
			entry.EditTarget = node.SourceRecord.EditTarget
		}
		registry.BindRuntimePaths(ref, node.RuntimeID, node.RuntimeIDAliases)
	}
	return ref
}

func sameComboEditTarget(left, right *axRelationTarget) bool {
	if left == nil || right == nil {
		return left == right
	}
	return *left == *right
}
