package core

import (
	"errors"
	"fmt"
	"strconv"
	"strings"
)

type AXWindowRefEntry struct {
	refLifecycleState
	Ref                             string
	AppRef                          string
	AppIdentity                     string
	WindowHandle                    string
	SemanticTarget                  string
	ProviderWindowBusName           string
	ProviderWindowObjectPath        string
	ProviderWindowProcessGeneration string
	ProviderWindowRole              string
	ProviderWindowName              string
	CreatedRevision                 string
	LastSeenRevision                string
	LastUsedTick                    uint64
	// NoAccessibilityTree marks a window that has a native handle but no
	// accessibility tree; see ax_native_window.go.
	NoAccessibilityTree bool
}

type WindowRefRegistry struct {
	Next       uint64 `json:"-"`
	tick       uint64
	limit      int
	entries    map[string]*AXWindowRefEntry
	byIdentity map[string]string
}

func (registry *WindowRefRegistry) clone() *WindowRefRegistry {
	if registry == nil {
		return nil
	}
	clone := &WindowRefRegistry{
		Next:       registry.Next,
		tick:       registry.tick,
		limit:      registry.limit,
		entries:    make(map[string]*AXWindowRefEntry, len(registry.entries)),
		byIdentity: make(map[string]string, len(registry.byIdentity)),
	}
	for ref, entry := range registry.entries {
		if entry == nil {
			clone.entries[ref] = nil
			continue
		}
		entryCopy := *entry
		clone.entries[ref] = &entryCopy
	}
	for identity, ref := range registry.byIdentity {
		clone.byIdentity[identity] = ref
	}
	return clone
}

func NewWindowRefRegistry(limit int) *WindowRefRegistry {
	if limit <= 0 {
		limit = defaultAXWindowRefRegistryLimit
	}
	return &WindowRefRegistry{
		Next:       1,
		limit:      limit,
		entries:    map[string]*AXWindowRefEntry{},
		byIdentity: map[string]string{},
	}
}

func windowRefIdentityKey(appIdentity, windowHandle string) string {
	return strings.Join([]string{
		strings.ToLower(strings.TrimSpace(appIdentity)),
		strings.TrimSpace(windowHandle),
	}, "\x00")
}

func modalWindowRole(role string) bool {
	switch normalizedTreeText(role) {
	case "alert", "alert dialog", "dialog", "file chooser", "color chooser":
		return true
	default:
		return false
	}
}

func windowProviderBindingContinuous(
	entry *AXWindowRefEntry,
	providerBusName,
	providerObjectPath,
	processGeneration,
	role,
	_ string,
) bool {
	if entry == nil {
		return false
	}
	wasModal := modalWindowRole(entry.ProviderWindowRole)
	isModal := modalWindowRole(role)
	if wasModal != isModal {
		return false
	}
	if !isModal {
		// A real top-level native window may rebuild its AX object or change its
		// title while retaining the same native handle and process generation.
		return true
	}
	// Toolkits commonly host transient dialogs inside their owner's native
	// window. In that case the native handle alone cannot distinguish a later
	// dialog instance from the original one, so preserve a modal ref only while
	// its provider owner and role family remain continuous. The accessible name
	// is mutable UI state: settings pages may put the active section in the
	// title without creating a new logical window.
	return strings.TrimSpace(entry.ProviderWindowBusName) ==
		strings.TrimSpace(providerBusName) &&
		strings.TrimSpace(entry.ProviderWindowObjectPath) ==
			strings.TrimSpace(providerObjectPath) &&
		strings.TrimSpace(entry.ProviderWindowProcessGeneration) ==
			strings.TrimSpace(processGeneration) &&
		normalizedTreeText(entry.ProviderWindowRole) ==
			normalizedTreeText(role)
}

func validInternalWindowHandle(windowHandle string) bool {
	const prefix = "w2:"
	windowHandle = strings.TrimSpace(windowHandle)
	if !strings.HasPrefix(windowHandle, prefix) ||
		len(windowHandle) != len(prefix)+32 {
		return false
	}
	for _, char := range windowHandle[len(prefix):] {
		if (char < '0' || char > '9') && (char < 'a' || char > 'f') {
			return false
		}
	}
	return true
}

func (registry *WindowRefRegistry) Acquire(
	appIdentity,
	windowHandle,
	semanticTarget,
	revision string,
	providerIdentity ...string,
) string {
	appIdentity = strings.ToLower(strings.TrimSpace(appIdentity))
	windowHandle = strings.TrimSpace(windowHandle)
	semanticTarget = strings.TrimSpace(semanticTarget)
	if registry == nil || appIdentity == "" ||
		!validInternalWindowHandle(windowHandle) ||
		semanticTarget == "" {
		return ""
	}
	registry.tick++
	providerWindowBusName := ""
	providerWindowObjectPath := ""
	providerWindowProcessGeneration := ""
	providerWindowRole := ""
	providerWindowName := ""
	if len(providerIdentity) > 0 {
		providerWindowBusName = strings.TrimSpace(providerIdentity[0])
	}
	if len(providerIdentity) > 1 {
		providerWindowObjectPath = strings.TrimSpace(providerIdentity[1])
	}
	if len(providerIdentity) > 2 {
		providerWindowProcessGeneration = strings.TrimSpace(providerIdentity[2])
	}
	if len(providerIdentity) > 3 {
		providerWindowRole = normalizedTreeText(providerIdentity[3])
	}
	if len(providerIdentity) > 4 {
		providerWindowName = normalizedTreeText(providerIdentity[4])
	}
	appRef := ""
	if len(providerIdentity) > 5 {
		appRef = strings.TrimSpace(providerIdentity[5])
	}
	ownerIdentity := appIdentity
	if appRef != "" {
		ownerIdentity = appRef
	}
	key := windowRefIdentityKey(ownerIdentity, windowHandle)
	if ref := registry.byIdentity[key]; ref != "" {
		if entry := registry.entries[ref]; entry != nil {
			if entry.Lifecycle != AXRefActive &&
				entry.Lifecycle != AXRefNeedsRevalidation {
				delete(registry.byIdentity, key)
			} else if !windowProviderBindingContinuous(
				entry,
				providerWindowBusName,
				providerWindowObjectPath,
				providerWindowProcessGeneration,
				providerWindowRole,
				providerWindowName,
			) {
				entry.Lifecycle = AXRefStale
				entry.LifecycleReason = "logical_window_replaced"
				delete(registry.byIdentity, key)
			} else {
				if !modalWindowRole(providerWindowRole) {
					providerWindowBusName = FirstNonEmpty(providerWindowBusName, entry.ProviderWindowBusName)
					providerWindowObjectPath = FirstNonEmpty(providerWindowObjectPath, entry.ProviderWindowObjectPath)
					providerWindowProcessGeneration = FirstNonEmpty(
						providerWindowProcessGeneration, entry.ProviderWindowProcessGeneration,
					)
					providerWindowRole = FirstNonEmpty(providerWindowRole, entry.ProviderWindowRole)
					providerWindowName = FirstNonEmpty(providerWindowName, entry.ProviderWindowName)
				}
				entry.AppRef = appRef
				entry.SemanticTarget = semanticTarget
				entry.ProviderWindowBusName = providerWindowBusName
				entry.ProviderWindowObjectPath = providerWindowObjectPath
				entry.ProviderWindowProcessGeneration = providerWindowProcessGeneration
				entry.ProviderWindowRole = providerWindowRole
				entry.ProviderWindowName = providerWindowName
				entry.LastSeenRevision = revision
				entry.LastUsedTick = registry.tick
				entry.Lifecycle = AXRefActive
				entry.LifecycleReason = ""
				return ref
			}
		}
		delete(registry.byIdentity, key)
	}
	ref := "w" + strconv.FormatUint(registry.Next, 10)
	registry.Next++
	entry := &AXWindowRefEntry{
		refLifecycleState:               refLifecycleState{IdentityKey: key, Lifecycle: AXRefActive},
		Ref:                             ref,
		AppRef:                          strings.TrimSpace(appRef),
		AppIdentity:                     appIdentity,
		WindowHandle:                    windowHandle,
		SemanticTarget:                  semanticTarget,
		ProviderWindowBusName:           providerWindowBusName,
		ProviderWindowObjectPath:        providerWindowObjectPath,
		ProviderWindowProcessGeneration: providerWindowProcessGeneration,
		ProviderWindowRole:              providerWindowRole,
		ProviderWindowName:              providerWindowName,
		CreatedRevision:                 revision,
		LastSeenRevision:                revision,
		LastUsedTick:                    registry.tick,
	}
	registry.entries[ref] = entry
	registry.byIdentity[key] = ref
	registry.evict()
	return ref
}

func (registry *WindowRefRegistry) evict() {
	for registry != nil && len(registry.entries) > registry.limit {
		oldestRef := ""
		var oldestTick uint64
		for ref, entry := range registry.entries {
			if oldestRef == "" || entry.LastUsedTick < oldestTick {
				oldestRef = ref
				oldestTick = entry.LastUsedTick
			}
		}
		entry := registry.entries[oldestRef]
		if entry == nil {
			delete(registry.entries, oldestRef)
			continue
		}
		delete(registry.entries, oldestRef)
		delete(registry.byIdentity, entry.IdentityKey)
	}
}

func parseMonotonicAXWindowRef(ref string) (uint64, bool) {
	if len(ref) < 2 || ref[0] != 'w' {
		return 0, false
	}
	value, err := strconv.ParseUint(ref[1:], 10, 64)
	return value, err == nil && value > 0
}

func (registry *WindowRefRegistry) Resolve(
	ref,
	requestedApp string,
) (*AXWindowRefEntry, error) {
	id, ok := parseMonotonicAXWindowRef(ref)
	if !ok {
		return nil, fmt.Errorf("invalid short AX window ref %q", ref)
	}
	if registry == nil {
		return nil, errors.New("AX window ref registry is unavailable")
	}
	entry := registry.entries[strings.TrimSpace(ref)]
	if entry == nil {
		if id < registry.Next {
			return nil, &axRefLifecycleError{kind: "window", lifecycle: AXRefExpired, message: fmt.Sprintf(
				"short AX window ref %q expired from this session registry; observe the window again",
				ref,
			)}
		}
		return nil, fmt.Errorf(
			"short AX window ref %q was never issued in this session",
			ref,
		)
	}
	if err := lifecycleResolveError(
		"window", ref, entry.Lifecycle, entry.LifecycleReason,
		"observe the window again",
	); err != nil {
		return nil, err
	}
	if requested := looseKey(requestedApp); requested != "" {
		registered := looseKey(entry.AppIdentity)
		if registered != "" &&
			!strings.Contains(registered, requested) &&
			!strings.Contains(requested, registered) {
			return nil, fmt.Errorf(
				"short AX window ref %q belongs to app %q, not %q",
				ref, entry.AppIdentity, requestedApp,
			)
		}
	}
	registry.tick++
	entry.LastUsedTick = registry.tick
	copy := *entry
	return &copy, nil
}

func (registry *WindowRefRegistry) MarkLifecycle(
	ref string,
	lifecycle AXRefLifecycle,
	reason string,
) {
	if registry == nil {
		return
	}
	if entry := registry.entries[strings.TrimSpace(ref)]; entry != nil {
		entry.setLifecycle(registry.byIdentity, lifecycle, reason)
	}
}

func (registry *WindowRefRegistry) MarkByApp(
	appRef string,
	lifecycle AXRefLifecycle,
	reason string,
) {
	if registry == nil {
		return
	}
	forEachOwnedRef(registry.entries, appRef,
		func(entry *AXWindowRefEntry) string { return entry.AppRef },
		func(ref string) { registry.MarkLifecycle(ref, lifecycle, reason) })
}

// markNoAccessibilityTree records whether the window of ref has no
// accessibility tree. The apps listing sets it on every window it issues.
func (registry *WindowRefRegistry) markNoAccessibilityTree(ref string, none bool) {
	if registry == nil {
		return
	}
	if entry := registry.entries[ref]; entry != nil {
		entry.NoAccessibilityTree = none
	}
}
