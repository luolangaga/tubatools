package core

import (
	"errors"
	"fmt"
	"strconv"
	"strings"
)

type CanonicalRole string

const (
	RoleGeneric CanonicalRole = "generic"
	RoleText    CanonicalRole = "text"
)

type TriState string

const (
	TriUnknown TriState = "unknown"
	TriFalse   TriState = "false"
	TriTrue    TriState = "true"
)

type CheckedState string

const (
	CheckedUnknown CheckedState = "unknown"
	CheckedFalse   CheckedState = "false"
	CheckedTrue    CheckedState = "true"
	CheckedMixed   CheckedState = "mixed"
)

type ActivationRoute string

const (
	ActivationNone         ActivationRoute = "none"
	ActivationSemantic     ActivationRoute = "semantic_action"
	ActivationLiveGeometry ActivationRoute = "live_geometry_fallback"
	ActivationUnknown      ActivationRoute = "unknown"
)

type AXStates struct {
	Active   TriState     `json:"active,omitempty"`
	Checked  CheckedState `json:"checked,omitempty"`
	Expanded TriState     `json:"expanded,omitempty"`
	Selected TriState     `json:"selected,omitempty"`
	Disabled TriState     `json:"disabled,omitempty"`
	Pressed  TriState     `json:"pressed,omitempty"`
	Invalid  TriState     `json:"invalid,omitempty"`
	Level    *int         `json:"level,omitempty"`
}

type AXCapabilities struct {
	Activate        ActivationRoute `json:"activate,omitempty"`
	SetValue        bool            `json:"setValue,omitempty"`
	EditText        bool            `json:"editText,omitempty"`
	Scroll          bool            `json:"scroll,omitempty"`
	Actions         []string        `json:"actions,omitempty"`
	ProviderActions []string        `json:"providerActions,omitempty"`
}

type AXProperties struct {
	Value       string `json:"value,omitempty"`
	Description string `json:"description,omitempty"`
	Placeholder string `json:"placeholder,omitempty"`
	Omitted     string `json:"omitted,omitempty"`
}

type AXProviderIdentity struct {
	BusName           string
	ObjectPath        string
	ProcessGeneration string
}

type AXNameProvenance struct {
	Source            string
	Contributors      []AXProviderIdentity
	RelationsComplete bool
	Confidence        string
}

type AXNode struct {
	ID                  string                        `json:"id"`
	Role                CanonicalRole                 `json:"role"`
	SemanticClass       string                        `json:"-"`
	ProviderRole        string                        `json:"providerRole,omitempty"`
	Name                string                        `json:"name,omitempty"`
	VisibilityEvidence  *AXVisibilityEvidence         `json:"visibilityEvidence,omitempty"`
	States              AXStates                      `json:"states,omitempty"`
	Capabilities        AXCapabilities                `json:"capabilities,omitempty"`
	Properties          AXProperties                  `json:"properties,omitempty"`
	Ref                 string                        `json:"ref,omitempty"`
	Children            []*AXNode                     `json:"children,omitempty"`
	Relations           map[string][]axRelationTarget `json:"relations,omitempty"`
	Annotations         []axProviderAnnotation        `json:"annotations,omitempty"`
	NameProvenance      AXNameProvenance              `json:"-"`
	SemanticFingerprint string                        `json:"semanticFingerprint"`
	ProviderIdentity    string                        `json:"-"`
	LocatorKind         AXLocatorKind                 `json:"-"`
	AutomationID        string                        `json:"-"`

	ProviderIndex                   int            `json:"-"`
	ProviderIndices                 []int          `json:"-"`
	RuntimeID                       []int          `json:"-"`
	RuntimeIDAliases                [][]int        `json:"-"`
	ProviderBusName                 string         `json:"-"`
	ProviderObjectPath              string         `json:"-"`
	ProcessGeneration               string         `json:"-"`
	ProviderIdentityScope           string         `json:"-"`
	ProviderWindowBusName           string         `json:"-"`
	ProviderWindowObjectPath        string         `json:"-"`
	ProviderWindowProcessGeneration string         `json:"-"`
	ProviderWindowRole              string         `json:"-"`
	ProviderWindowName              string         `json:"-"`
	Showing                         *bool          `json:"-"`
	Visible                         *bool          `json:"-"`
	SemanticTarget                  string         `json:"-"`
	SourceRecord                    *ElementRecord `json:"-"`
	Parent                          *AXNode        `json:"-"`
	Synthetic                       bool           `json:"-"`
	ForceKeep                       bool           `json:"-"`
	// DistinguishesDuplicates marks a node kept only as the context that
	// tells same-named controls below it apart.
	DistinguishesDuplicates   bool   `json:"-"`
	FindMatch                 bool   `json:"-"`
	ExplicitRoot              bool   `json:"-"`
	SemanticIdentityAmbiguous bool   `json:"-"`
	AddressabilityEvaluated   bool   `json:"-"`
	AddressabilityReason      string `json:"-"`
	BudgetOmittedNodes        int    `json:"-"`
	BudgetOmittedTargets      int    `json:"-"`
}

type DistillationStageReport struct {
	Name                 string `json:"name"`
	InputNodes           int    `json:"inputNodes"`
	OutputNodes          int    `json:"outputNodes"`
	RemovedNodes         int    `json:"removedNodes"`
	UnwrappedNodes       int    `json:"unwrappedNodes"`
	SuppressedProperties int    `json:"suppressedProperties"`
	BudgetApplied        bool   `json:"budgetApplied"`
	BudgetCharsBefore    int    `json:"budgetCharsBefore"`
	BudgetCharsAfter     int    `json:"budgetCharsAfter"`
	BudgetOmittedNodes   int    `json:"budgetOmittedNodes"`
	BudgetOmittedTargets int    `json:"budgetOmittedTargets"`
	BudgetExhausted      bool   `json:"budgetExhausted"`
	TargetCount          int    `json:"targetCount"`
	TargetRecall         int    `json:"targetRecall"`
	AmbiguityBefore      int    `json:"ambiguityBefore"`
	AmbiguityAfter       int    `json:"ambiguityAfter"`
	InvariantFailure     string `json:"invariantFailure,omitempty"`
}

type AXSnapshot struct {
	SchemaVersion              string                    `json:"schemaVersion"`
	AppRef                     string                    `json:"appRef,omitempty"`
	AppIdentity                string                    `json:"appIdentity"`
	WindowHandle               string                    `json:"windowHandle"`
	WindowRef                  string                    `json:"-"`
	Revision                   string                    `json:"revision"`
	Roots                      []*AXNode                 `json:"roots"`
	Distillation               []DistillationStageReport `json:"distillation,omitempty"`
	AcquisitionIncomplete      bool                      `json:"-"`
	IdentityCoverageIncomplete bool                      `json:"-"`
	RefInventoryComplete       bool                      `json:"-"`
	// ActedRef is the rN the action that produced this snapshot targeted.
	// Projection keeps that node's full text.
	ActedRef string `json:"-"`

	CanonicalRoots []*AXNode          `json:"-"`
	ByID           map[string]*AXNode `json:"-"`
	ByIndex        map[int]*AXNode    `json:"-"`
}

func runtimePathKey(path []int) string {
	if len(path) == 0 {
		return "/"
	}
	parts := make([]string, len(path))
	for index, value := range path {
		parts[index] = strconv.Itoa(value)
	}
	return strings.Join(parts, ".")
}

func recordInObservation(snapshot *AppSnapshot, record *ElementRecord) bool {
	if snapshot == nil || record == nil {
		return false
	}
	if snapshot.observationRoot != nil {
		return runtimePathContains(
			snapshot.observationRoot.RuntimeID,
			record.RuntimeID,
		)
	}
	return recordInAXScope(snapshot, record)
}

func (snapshot *AXSnapshot) Validate() error {
	if snapshot == nil {
		return errors.New("nil AXSnapshot")
	}
	if snapshot.SchemaVersion != "AXSnapshot" {
		return fmt.Errorf("unexpected AX schema version %q", snapshot.SchemaVersion)
	}
	seen := map[string]bool{}
	var visit func([]*AXNode, *AXNode) error
	visit = func(nodes []*AXNode, parent *AXNode) error {
		for _, node := range nodes {
			if node == nil || node.ID == "" || node.Role == "" {
				return errors.New("AXSnapshot contains an incomplete node")
			}
			if seen[node.ID] && !node.Synthetic {
				return fmt.Errorf("duplicate AXSnapshot node id %q", node.ID)
			}
			seen[node.ID] = true
			if node.Parent != parent {
				return fmt.Errorf("AXSnapshot parent mismatch at %q", node.ID)
			}
			if err := visit(node.Children, node); err != nil {
				return err
			}
		}
		return nil
	}
	return visit(snapshot.Roots, nil)
}
