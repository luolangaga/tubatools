package core

import (
	"errors"
	"fmt"
	"strconv"
	"strings"
)

type AXAppRefEntry struct {
	refLifecycleState
	Ref               string
	DisplayName       string
	ProviderBusName   string
	ProcessGeneration string
	CreatedRevision   string
	LastSeenRevision  string
	LastUsedTick      uint64
}

type AppRefRegistry struct {
	Next       uint64 `json:"-"`
	tick       uint64
	limit      int
	entries    map[string]*AXAppRefEntry
	byIdentity map[string]string
}

func (registry *AppRefRegistry) clone() *AppRefRegistry {
	if registry == nil {
		return nil
	}
	clone := &AppRefRegistry{
		Next:       registry.Next,
		tick:       registry.tick,
		limit:      registry.limit,
		entries:    make(map[string]*AXAppRefEntry, len(registry.entries)),
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

func NewAppRefRegistry(limit int) *AppRefRegistry {
	if limit <= 0 {
		limit = defaultAXAppRefRegistryLimit
	}
	return &AppRefRegistry{
		Next:       1,
		limit:      limit,
		entries:    map[string]*AXAppRefEntry{},
		byIdentity: map[string]string{},
	}
}

func appRefIdentityKey(providerBusName, processGeneration string) string {
	return strings.Join([]string{
		strings.TrimSpace(providerBusName),
		strings.TrimSpace(processGeneration),
	}, "\x00")
}

func (registry *AppRefRegistry) Acquire(
	displayName,
	providerBusName,
	processGeneration,
	revision string,
) string {
	if registry == nil {
		return ""
	}
	key := appRefIdentityKey(providerBusName, processGeneration)
	if strings.Trim(key, "\x00") == "" ||
		strings.TrimSpace(providerBusName) == "" ||
		strings.TrimSpace(processGeneration) == "" {
		return ""
	}
	registry.tick++
	if ref := registry.byIdentity[key]; ref != "" {
		if entry := registry.entries[ref]; entry != nil {
			if entry.Lifecycle != AXRefActive &&
				entry.Lifecycle != AXRefNeedsRevalidation {
				delete(registry.byIdentity, key)
			} else {
				entry.DisplayName = strings.TrimSpace(displayName)
				entry.LastSeenRevision = revision
				entry.LastUsedTick = registry.tick
				entry.Lifecycle = AXRefActive
				entry.LifecycleReason = ""
				return ref
			}
		}
		delete(registry.byIdentity, key)
	}
	ref := "a" + strconv.FormatUint(registry.Next, 10)
	registry.Next++
	registry.entries[ref] = &AXAppRefEntry{
		refLifecycleState: refLifecycleState{IdentityKey: key, Lifecycle: AXRefActive},
		Ref:               ref,
		DisplayName:       strings.TrimSpace(displayName),
		ProviderBusName:   strings.TrimSpace(providerBusName),
		ProcessGeneration: strings.TrimSpace(processGeneration),
		CreatedRevision:   revision,
		LastSeenRevision:  revision,
		LastUsedTick:      registry.tick,
	}
	registry.byIdentity[key] = ref
	registry.evict()
	return ref
}

func (registry *AppRefRegistry) evict() {
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
		delete(registry.entries, oldestRef)
		if entry != nil {
			delete(registry.byIdentity, entry.IdentityKey)
		}
	}
}

func parseMonotonicAXAppRef(ref string) (uint64, bool) {
	if len(ref) < 2 || ref[0] != 'a' {
		return 0, false
	}
	value, err := strconv.ParseUint(ref[1:], 10, 64)
	return value, err == nil && value > 0
}

func (registry *AppRefRegistry) Resolve(ref string) (*AXAppRefEntry, error) {
	id, ok := parseMonotonicAXAppRef(ref)
	if !ok {
		return nil, fmt.Errorf("invalid short AX app ref %q", ref)
	}
	if registry == nil {
		return nil, errors.New("AX app ref registry is unavailable")
	}
	entry := registry.entries[strings.TrimSpace(ref)]
	if entry == nil {
		if id < registry.Next {
			return nil, &axRefLifecycleError{kind: "app", lifecycle: AXRefExpired, message: fmt.Sprintf(
				"short AX app ref %q expired from this session registry; observe applications again",
				ref,
			)}
		}
		return nil, fmt.Errorf(
			"short AX app ref %q was never issued in this session",
			ref,
		)
	}
	if err := lifecycleResolveError(
		"app", ref, entry.Lifecycle, entry.LifecycleReason,
		"observe applications again",
	); err != nil {
		return nil, err
	}
	registry.tick++
	entry.LastUsedTick = registry.tick
	copy := *entry
	return &copy, nil
}

// ProviderIdentity returns the exact process identity an issued aN named,
// whatever its lifecycle. It is for read-only absence checks of that same
// process; it never revives the ref or binds input.
func (registry *AppRefRegistry) ProviderIdentity(ref string) *ProviderAppIdentity {
	if registry == nil {
		return nil
	}
	entry := registry.entries[strings.TrimSpace(ref)]
	if entry == nil || entry.ProviderBusName == "" || entry.ProcessGeneration == "" {
		return nil
	}
	return &ProviderAppIdentity{
		ProviderBusName:   entry.ProviderBusName,
		ProcessGeneration: entry.ProcessGeneration,
	}
}

func (registry *AppRefRegistry) MarkLifecycle(
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
