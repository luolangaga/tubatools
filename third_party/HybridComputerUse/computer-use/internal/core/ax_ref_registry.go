package core

import (
	"errors"
	"fmt"
	"strconv"
	"strings"
)

const defaultAXRefRegistryLimit = 4096
const defaultAXWindowRefRegistryLimit = 256
const defaultAXAppRefRegistryLimit = 128

type AXLocatorKind string

const (
	AXLocatorSemantic      AXLocatorKind = "semantic"
	AXLocatorProviderBound AXLocatorKind = "provider_bound"
)

type AXRefLifecycle string

const (
	AXRefActive            AXRefLifecycle = "active"
	AXRefNeedsRevalidation AXRefLifecycle = "needs_revalidation"
	AXRefStale             AXRefLifecycle = "stale"
	AXRefExpired           AXRefLifecycle = "expired"
	AXRefSuperseded        AXRefLifecycle = "superseded"
)

// refLifecycleState is the identity key and lifecycle that app and window
// refs share.
type refLifecycleState struct {
	IdentityKey     string
	Lifecycle       AXRefLifecycle
	LifecycleReason string
}

// setLifecycle records a lifecycle. A ref that is neither active nor awaiting
// revalidation releases its identity key, so the next Acquire of that
// identity issues a new ref.
func (state *refLifecycleState) setLifecycle(
	byIdentity map[string]string,
	lifecycle AXRefLifecycle,
	reason string,
) {
	state.Lifecycle = lifecycle
	state.LifecycleReason = strings.TrimSpace(reason)
	if lifecycle != AXRefActive && lifecycle != AXRefNeedsRevalidation {
		delete(byIdentity, state.IdentityKey)
	}
}

type AXRefEntry struct {
	EditTarget                      *axRelationTarget
	Ref                             string
	IdentityKey                     string
	LocatorKind                     AXLocatorKind
	Lifecycle                       AXRefLifecycle
	StaleReason                     string
	AppRef                          string
	AppIdentity                     string
	LogicalWindowRef                string
	WindowHandle                    string
	ProviderBusName                 string
	ProviderObjectPath              string
	ProcessGeneration               string
	ProviderIdentityScope           string
	ProviderWindowBusName           string
	ProviderWindowObjectPath        string
	ProviderWindowProcessGeneration string
	ProviderWindowRole              string
	ProviderWindowName              string
	AutomationID                    string
	ProviderRole                    string
	RuntimeID                       []int
	RuntimeIDAliases                [][]int
	SemanticTarget                  string
	Role                            CanonicalRole
	Name                            string
	CreatedRevision                 string
	LastSeenRevision                string
	LastUsedTick                    uint64
}

func copyRuntimeIDs(paths [][]int) [][]int {
	out := make([][]int, 0, len(paths))
	seen := map[string]bool{}
	for _, path := range paths {
		if len(path) == 0 {
			continue
		}
		key := fmt.Sprint(path)
		if seen[key] {
			continue
		}
		seen[key] = true
		out = append(out, append([]int(nil), path...))
	}
	return out
}

type RefRegistry struct {
	Next       uint64 `json:"-"`
	tick       uint64
	limit      int
	Entries    map[string]*AXRefEntry `json:"-"`
	byIdentity map[string]string
}

func (registry *RefRegistry) clone() *RefRegistry {
	if registry == nil {
		return nil
	}
	clone := &RefRegistry{
		Next:       registry.Next,
		tick:       registry.tick,
		limit:      registry.limit,
		Entries:    make(map[string]*AXRefEntry, len(registry.Entries)),
		byIdentity: make(map[string]string, len(registry.byIdentity)),
	}
	for ref, entry := range registry.Entries {
		if entry == nil {
			clone.Entries[ref] = nil
			continue
		}
		entryCopy := *entry
		entryCopy.RuntimeID = append([]int(nil), entry.RuntimeID...)
		entryCopy.RuntimeIDAliases = copyRuntimeIDs(entry.RuntimeIDAliases)
		clone.Entries[ref] = &entryCopy
	}
	for identity, ref := range registry.byIdentity {
		clone.byIdentity[identity] = ref
	}
	return clone
}

type axRefLifecycleError struct {
	kind      string
	lifecycle AXRefLifecycle
	message   string
}

func (err *axRefLifecycleError) Error() string { return err.message }

func lifecycleResolveError(kind, ref string, lifecycle AXRefLifecycle, reason, recovery string) error {
	if lifecycle == "" || lifecycle == AXRefActive {
		return nil
	}
	if reason == "" {
		reason = string(lifecycle)
	}
	return &axRefLifecycleError{kind: kind, lifecycle: lifecycle, message: fmt.Sprintf(
		"short AX %s ref %q is %s (%s); %s",
		kind, ref, lifecycle, reason, recovery,
	)}
}

func NewRefRegistry(limit int) *RefRegistry {
	if limit <= 0 {
		limit = defaultAXRefRegistryLimit
	}
	return &RefRegistry{
		Next:       1,
		limit:      limit,
		Entries:    map[string]*AXRefEntry{},
		byIdentity: map[string]string{},
	}
}

// forEachOwnedRef visits every ref whose owner app or window ref is ownerRef.
func forEachOwnedRef[E any](
	entries map[string]*E,
	ownerRef string,
	ownerOf func(*E) string,
	visit func(ref string),
) {
	ownerRef = strings.TrimSpace(ownerRef)
	for ref, entry := range entries {
		if entry != nil && ownerOf(entry) == ownerRef {
			visit(ref)
		}
	}
}

func (registry *RefRegistry) MarkStale(ref, reason string) {
	registry.MarkLifecycle(ref, AXRefStale, reason)
}

func (registry *RefRegistry) MarkNeedsRevalidation(ref, reason string) {
	registry.MarkLifecycle(ref, AXRefNeedsRevalidation, reason)
}

func (registry *RefRegistry) MarkSuperseded(ref, reason string) {
	registry.MarkLifecycle(ref, AXRefSuperseded, reason)
}

func (registry *RefRegistry) MarkLifecycle(
	ref string,
	lifecycle AXRefLifecycle,
	reason string,
) {
	if registry == nil {
		return
	}
	entry := registry.Entries[strings.TrimSpace(ref)]
	if entry == nil || entry.Lifecycle == AXRefExpired {
		return
	}
	entry.Lifecycle = lifecycle
	entry.StaleReason = strings.TrimSpace(reason)
	if lifecycle != AXRefActive && lifecycle != AXRefNeedsRevalidation &&
		registry.byIdentity[entry.IdentityKey] == entry.Ref {
		delete(registry.byIdentity, entry.IdentityKey)
	}
}

func (registry *RefRegistry) MarkByApp(
	appRef string,
	lifecycle AXRefLifecycle,
	reason string,
) {
	if registry == nil {
		return
	}
	forEachOwnedRef(registry.Entries, appRef,
		func(entry *AXRefEntry) string { return entry.AppRef },
		func(ref string) { registry.MarkLifecycle(ref, lifecycle, reason) })
}

func (registry *RefRegistry) MarkByWindow(
	windowRef string,
	lifecycle AXRefLifecycle,
	reason string,
) {
	if registry == nil {
		return
	}
	forEachOwnedRef(registry.Entries, windowRef,
		func(entry *AXRefEntry) string { return entry.LogicalWindowRef },
		func(ref string) { registry.MarkLifecycle(ref, lifecycle, reason) })
}

func (registry *RefRegistry) evict() {
	for registry != nil && len(registry.Entries) > registry.limit {
		oldestRef := ""
		var oldestTick uint64
		for ref, entry := range registry.Entries {
			if oldestRef == "" || entry.LastUsedTick < oldestTick {
				oldestRef = ref
				oldestTick = entry.LastUsedTick
			}
		}
		entry := registry.Entries[oldestRef]
		if entry == nil {
			delete(registry.Entries, oldestRef)
			continue
		}
		delete(registry.Entries, oldestRef)
		if registry.byIdentity[entry.IdentityKey] == entry.Ref {
			delete(registry.byIdentity, entry.IdentityKey)
		}
		entry.Lifecycle = AXRefExpired
		entry.StaleReason = "session registry capacity eviction"
	}
}

func parseMonotonicAXRef(ref string) (uint64, bool) {
	ref = strings.TrimSpace(ref)
	if len(ref) < 2 || ref[0] != 'r' {
		return 0, false
	}
	value, err := strconv.ParseUint(ref[1:], 10, 64)
	return value, err == nil && value > 0
}

func (registry *RefRegistry) Resolve(
	ref,
	requestedApp string,
) (*AXRefEntry, error) {
	id, ok := parseMonotonicAXRef(ref)
	if !ok {
		return nil, fmt.Errorf("invalid short AX ref %q", ref)
	}
	if registry == nil {
		return nil, errors.New("AX ref registry is unavailable")
	}
	entry := registry.Entries[strings.TrimSpace(ref)]
	if entry == nil {
		if id < registry.Next {
			return nil, &axRefLifecycleError{kind: "element", lifecycle: AXRefExpired, message: fmt.Sprintf(
				"short AX ref %q expired from this session registry; observe or find the target again",
				ref,
			)}
		}
		return nil, fmt.Errorf(
			"short AX ref %q was never issued in this session",
			ref,
		)
	}
	if entry.Lifecycle != AXRefActive {
		reason := "target identity is no longer current"
		switch entry.Lifecycle {
		case AXRefNeedsRevalidation:
			reason = "target identity requires a fresh observation"
		case AXRefSuperseded:
			reason = "target identity was replaced"
		case AXRefExpired:
			reason = "target reference expired"
		}
		return nil, &axRefLifecycleError{kind: "element", lifecycle: entry.Lifecycle, message: fmt.Sprintf(
			"short AX ref %q is %s (%s); observe or find the target again",
			ref, entry.Lifecycle, reason,
		)}
	}
	if requested := looseKey(requestedApp); requested != "" {
		registered := looseKey(entry.AppIdentity)
		if registered != "" &&
			!strings.Contains(registered, requested) &&
			!strings.Contains(requested, registered) {
			return nil, fmt.Errorf(
				"short AX ref %q belongs to app %q, not %q",
				ref, entry.AppIdentity, requestedApp,
			)
		}
	}
	registry.tick++
	entry.LastUsedTick = registry.tick
	copy := *entry
	return &copy, nil
}
