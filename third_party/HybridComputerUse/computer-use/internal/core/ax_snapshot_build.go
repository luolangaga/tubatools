package core

import (
	"errors"
	"fmt"
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"strings"
)

func buildAXSnapshot(snapshot *AppSnapshot) (*AXSnapshot, error) {
	if snapshot == nil {
		return nil, errors.New("cannot build AXSnapshot from nil snapshot")
	}
	out := newAXSnapshotHeader(snapshot)
	byProviderIdentity := map[string]*AXNode{}
	processIdentityEvidence := map[string]*AXNode{}
	identities := newSemanticIdentityIndex(snapshot)
	for index := range snapshot.Elements {
		record := &snapshot.Elements[index]
		if !recordInObservation(snapshot, record) {
			continue
		}
		id := runtimePathKey(record.RuntimeID)
		if _, exists := out.ByID[id]; exists {
			return nil, fmt.Errorf("duplicate AXSnapshot node id %q", id)
		}
		if _, exists := out.ByIndex[record.Index]; exists {
			return nil, fmt.Errorf("duplicate AXSnapshot provider index %d", record.Index)
		}
		parent := nearestAXSnapshotParent(out, record.RuntimeID)
		providerRole := ontologyProviderRole(record)
		descriptor := canonicalRoleDescriptor(providerRole, parent)
		semanticTarget := indexedSemanticRef(snapshot, identities, *record)
		providerIdentity := completeAXProviderIdentity(
			snapshot.publicWindowRef,
			record.ProcessGeneration,
			record.ProviderBusName,
			record.ProviderObjectPath,
			record.ProviderIdentityScope,
			semanticTarget,
		)
		if err := checkProcessObjectAlias(
			processIdentityEvidence, snapshot, record, descriptor.ModelRole,
		); err != nil {
			return nil, err
		}
		if providerIdentity != "" {
			if canonical := byProviderIdentity[providerIdentity]; canonical != nil {
				if err := validateExactProviderAlias(canonical, record, descriptor.ModelRole); err != nil {
					return nil, err
				}
				out.ByID[id] = canonical
				out.ByIndex[record.Index] = canonical
				mergeExactProviderAlias(canonical, record)
				continue
			}
		}
		locatorKind, semanticTarget := axLocatorForRecord(
			record, descriptor.ModelRole, semanticTarget, providerIdentity,
		)
		node := newAXNodeFromRecord(snapshot, record, axNodeBinding{
			id:               id,
			parent:           parent,
			descriptor:       descriptor,
			providerRole:     providerRole,
			providerIdentity: providerIdentity,
			locatorKind:      locatorKind,
			semanticTarget:   semanticTarget,
		})
		if snapshot.observationRoot != nil && record.Index == snapshot.observationRoot.Index {
			node.ExplicitRoot = true
			if entry := snapshot.observationRootEntry; entry != nil {
				// A cropped subtree cannot replace the locator already resolved
				// against the complete canonical window.
				node.LocatorKind = entry.LocatorKind
			}
		}
		out.ByID[id] = node
		out.ByIndex[record.Index] = node
		if providerIdentity != "" {
			byProviderIdentity[providerIdentity] = node
		}
		if parent == nil {
			out.CanonicalRoots = append(out.CanonicalRoots, node)
		} else {
			parent.Children = append(parent.Children, node)
		}
	}
	for _, annotation := range snapshot.Annotations {
		if parent := out.ByID[runtimePathKey(annotation.ParentRuntimeID)]; parent != nil {
			parent.Annotations = append(parent.Annotations, annotation)
		}
	}
	out.Roots = cloneAXForest(out.CanonicalRoots, nil)
	return out, out.Validate()
}

// newAXSnapshotHeader returns an empty snapshot with the window identity
// and coverage flags of snapshot.
func newAXSnapshotHeader(snapshot *AppSnapshot) *AXSnapshot {
	coverage := providerCoverageSummary(snapshot.Annotations)
	identityCoverageIncomplete :=
		coverage.AcquisitionIncomplete > 0 ||
			coverage.ExpandableHidden > 0 ||
			snapshot.FindCandidatesOnly
	return &AXSnapshot{
		SchemaVersion:              "AXSnapshot",
		AppRef:                     snapshot.publicAppRef,
		AppIdentity:                appIdentityForSnapshot(snapshot),
		WindowHandle:               snapshot.WindowHandle,
		WindowRef:                  snapshot.publicWindowRef,
		Revision:                   snapshot.SnapshotID,
		AcquisitionIncomplete:      coverage.AcquisitionIncomplete > 0,
		IdentityCoverageIncomplete: identityCoverageIncomplete,
		RefInventoryComplete: !identityCoverageIncomplete &&
			snapshot.observationRoot == nil &&
			normalizedAXScope(snapshot.Scope) == contract.AXScopeWindow &&
			isShortAXWindowRef(snapshot.publicWindowRef),
		ByID:    map[string]*AXNode{},
		ByIndex: map[int]*AXNode{},
	}
}

// nearestAXSnapshotParent returns the closest ancestor of runtimeID that is
// already in out. Records arrive in tree order, so ancestors come first.
func nearestAXSnapshotParent(out *AXSnapshot, runtimeID []int) *AXNode {
	for depth := len(runtimeID) - 1; depth >= 0; depth-- {
		if candidate := out.ByID[runtimePathKey(runtimeID[:depth])]; candidate != nil {
			return candidate
		}
	}
	return nil
}

// checkProcessObjectAlias rejects a record whose process object was already
// seen with conflicting identity facts. Process object identity is
// independent of its projected label.
func checkProcessObjectAlias(
	evidence map[string]*AXNode,
	snapshot *AppSnapshot,
	record *ElementRecord,
	role CanonicalRole,
) error {
	identityScope := strings.TrimSpace(record.ProviderIdentityScope)
	if identityScope != "process" && identityScope != "process_instance" {
		return nil
	}
	objectKey := completeAXProviderIdentity(
		snapshot.publicWindowRef, record.ProcessGeneration,
		record.ProviderBusName, record.ProviderObjectPath, identityScope, "",
	)
	if objectKey == "" {
		return nil
	}
	identity := evidence[objectKey]
	if identity == nil {
		identity = &AXNode{Role: role}
		evidence[objectKey] = identity
	} else if err := validateExactProviderAlias(identity, record, role); err != nil {
		return err
	}
	retainProviderAliasIdentity(identity, record)
	return nil
}

// axLocatorForRecord decides how a ref for this record is found again: by
// semantic target, or bound to the exact provider object. It returns the
// semantic target to store, which may be cleared or replaced.
func axLocatorForRecord(
	record *ElementRecord,
	role CanonicalRole,
	semanticTarget, providerIdentity string,
) (AXLocatorKind, string) {
	if providerIdentity != "" && normalizedTreeText(record.Name) == "" {
		switch role {
		case "slider", "spinbutton", "scrollbar":
			// Numeric controls may describe their current value. Keep that
			// observation out of the identity used after changing the value.
			return AXLocatorProviderBound, string(role)
		}
	}
	locatorKind := AXLocatorKind("")
	if semanticTarget != "" {
		locatorKind = AXLocatorSemantic
		if (role == "cell" ||
			role == "gridcell") &&
			spreadsheetRangeValue.MatchString(
				normalizedTreeText(record.Name),
			) {
			// An A1-looking name is not a complete spreadsheet identity.
			// Until document/sheet/table ownership is available, bind the
			// ref to this exact provider object instead of allowing C2 in a
			// different sheet to recover the same public ref.
			if providerIdentity != "" {
				locatorKind = AXLocatorProviderBound
			} else {
				semanticTarget = ""
				locatorKind = ""
			}
		}
	} else if providerIdentity != "" {
		semanticTarget = semanticDiagnosticSelector(
			role,
			record,
		)
		if semanticTarget != "" {
			locatorKind = AXLocatorProviderBound
		}
	}
	return locatorKind, semanticTarget
}

// axNodeBinding is what buildAXSnapshot decided about one record before
// it becomes a node.
type axNodeBinding struct {
	id               string
	parent           *AXNode
	descriptor       RoleDescriptor
	providerRole     string
	providerIdentity string
	locatorKind      AXLocatorKind
	semanticTarget   string
}

func newAXNodeFromRecord(snapshot *AppSnapshot, record *ElementRecord, binding axNodeBinding) *AXNode {
	node := &AXNode{
		ID:            binding.id,
		Role:          binding.descriptor.ModelRole,
		SemanticClass: binding.descriptor.SemanticClass,
		ProviderRole:  binding.providerRole,
		Name:          normalizedTreeText(record.Name),
		States:        typedAXStates(record),
		Properties: AXProperties{
			Value:       normalizedTreeText(record.Value),
			Description: normalizedTreeText(record.Description),
			Placeholder: normalizedTreeText(record.Placeholder),
		},
		Relations: record.Relations,
		NameProvenance: AXNameProvenance{
			Source:            strings.TrimSpace(record.NameSource),
			Contributors:      nameContributorIdentities(record),
			RelationsComplete: record.RelationReadComplete,
			Confidence:        nameProvenanceConfidence(record),
		},
		ProviderIndex:   record.Index,
		ProviderIndices: []int{record.Index},
		RuntimeID:       append([]int(nil), record.RuntimeID...),
		RuntimeIDAliases: [][]int{
			append([]int(nil), record.RuntimeID...),
		},
		ProviderBusName:                 record.ProviderBusName,
		ProviderObjectPath:              record.ProviderObjectPath,
		ProcessGeneration:               record.ProcessGeneration,
		ProviderIdentityScope:           record.ProviderIdentityScope,
		ProviderWindowBusName:           record.ProviderWindowBusName,
		ProviderWindowObjectPath:        record.ProviderWindowObjectPath,
		ProviderWindowProcessGeneration: record.ProviderWindowProcessGeneration,
		ProviderWindowRole:              record.ProviderWindowRole,
		ProviderWindowName:              record.ProviderWindowName,
		ProviderIdentity:                binding.providerIdentity,
		LocatorKind:                     binding.locatorKind,
		AutomationID:                    strings.TrimSpace(record.AutomationID),
		Showing:                         record.Showing,
		Visible:                         record.Visible,
		VisibilityEvidence:              cloneVisibilityEvidence(record.VisibilityEvidence),
		SemanticTarget:                  binding.semanticTarget,
		SourceRecord:                    record,
		Parent:                          binding.parent,
	}
	if node.VisibilityEvidence != nil && node.VisibilityEvidence.State == "unknown" {
		node.Showing, node.Visible = nil, nil
	}
	node.Capabilities = typedAXCapabilities(record, node.Role)
	node.SemanticFingerprint = semanticFingerprint(
		snapshot.WindowHandle,
		node.Role,
		node.Name,
		binding.semanticTarget,
		node.Relations,
	)
	return node
}

func nameContributorIdentities(record *ElementRecord) []AXProviderIdentity {
	if record == nil {
		return nil
	}
	targets := record.Relations["labelled-by"]
	identities := make([]AXProviderIdentity, 0, len(targets))
	seen := map[string]bool{}
	for _, target := range targets {
		key := providerObjectIdentityKey(
			target.ProviderBusName,
			target.ProviderObjectPath,
			target.ProcessGeneration,
		)
		if key == "" || seen[key] {
			continue
		}
		seen[key] = true
		identities = append(identities, AXProviderIdentity{
			BusName:           strings.TrimSpace(target.ProviderBusName),
			ObjectPath:        strings.TrimSpace(target.ProviderObjectPath),
			ProcessGeneration: strings.TrimSpace(target.ProcessGeneration),
		})
	}
	return identities
}

func nameProvenanceConfidence(record *ElementRecord) string {
	if record == nil {
		return "unknown"
	}
	switch strings.TrimSpace(record.NameSource) {
	case "provider":
		return "provider_declared"
	case "labelled_by":
		if record.RelationReadComplete &&
			len(nameContributorIdentities(record)) > 0 {
			return "exact"
		}
		return "inferred"
	case "synthetic_text_run":
		return "inferred"
	default:
		return "unknown"
	}
}

func cloneAXForest(nodes []*AXNode, parent *AXNode) []*AXNode {
	out := make([]*AXNode, 0, len(nodes))
	for _, node := range nodes {
		clone := *node
		clone.VisibilityEvidence = cloneVisibilityEvidence(node.VisibilityEvidence)
		clone.Parent = parent
		clone.Children = cloneAXForest(node.Children, &clone)
		clone.RuntimeID = append([]int(nil), node.RuntimeID...)
		clone.ProviderIndices = append([]int(nil), node.ProviderIndices...)
		clone.RuntimeIDAliases = make([][]int, 0, len(node.RuntimeIDAliases))
		for _, alias := range node.RuntimeIDAliases {
			clone.RuntimeIDAliases = append(
				clone.RuntimeIDAliases,
				append([]int(nil), alias...),
			)
		}
		clone.Capabilities.Actions = append([]string(nil), node.Capabilities.Actions...)
		clone.Capabilities.ProviderActions = append(
			[]string(nil), node.Capabilities.ProviderActions...,
		)
		clone.Annotations = append([]axProviderAnnotation(nil), node.Annotations...)
		out = append(out, &clone)
	}
	return out
}
