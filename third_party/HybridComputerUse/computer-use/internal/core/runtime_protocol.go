package core

// AXServiceRequest is common-service orchestration state. It never crosses the
// provider boundary; operation-specific request builders translate it into
// typed provider requests.
type AXServiceRequest struct {
	Operation          axServiceOperation   `json:"-"`
	App                string               `json:"app,omitempty"`
	AppIdentity        *ProviderAppIdentity `json:"appIdentity,omitempty"`
	TargetWindowHandle string               `json:"targetWindowHandle,omitempty"`
	ExpectedWindow     *ProviderWindowExpectation
	Element            *ElementRecord         `json:"element,omitempty"`
	SemanticTarget     *semanticTarget        `json:"semanticTarget,omitempty"`
	SourceRef          string                 `json:"sourceRef,omitempty"`
	Actionability      *ActionabilityEvidence `json:"-"`
	Action             axActionCommand        `json:"-"`
	WindowAction       *WindowOperation       `json:"-"`
	WindowBounds       *frame                 `json:"windowBounds,omitempty"`
	TextLimit          any                    `json:"text_limit,omitempty"`
	MaxTreeNodes       int                    `json:"max_tree_nodes,omitempty"`
	MaxTreeDepth       int                    `json:"max_tree_depth,omitempty"`
	Prune              *bool                  `json:"prune,omitempty"`
	Boxes              *bool                  `json:"boxes,omitempty"`
	IncludeScreenshot  *bool                  `json:"includeScreenshot,omitempty"`
	Observe            string                 `json:"observe,omitempty"`
	CaptureSnapshot    *bool                  `json:"-"`
	// LightReadback limits the post-action read to the window root. It is
	// not part of the projection, so later reads keep their own limits.
	LightReadback bool `json:"-"`
	// FindWalk is request-local, like LightReadback: it is not part of the
	// projection, so reads after a find use their own limits.
	FindWalk        bool                  `json:"-"`
	FindQuery       *ProviderFindQuery    `json:"-"`
	FindResumeAfter []int                 `json:"-"`
	TreeView        string                `json:"-"`
	Scope           string                `json:"-"`
	ObservationRoot map[string]any        `json:"-"`
	KnownRefs       map[string]elementRef `json:"knownRefs,omitempty"`

	// RecheckAfterAction is request-local, like FindWalk; see
	// ProviderObservationOptions.RecheckAfterAction.
	RecheckAfterAction bool `json:"-"`
}

type axServiceOperation string

const (
	axServiceObserveApps axServiceOperation = "observe_apps"
	axServiceObserve     axServiceOperation = "observe"
)

type axActionCommand interface {
	Intent() AccessibilityOperation
	applyProviderAction(*ProviderActionRequest)
	appendTraceFields(map[string]any)
}

type axResolveTargetCommand struct{}

func (axResolveTargetCommand) Intent() AccessibilityOperation {
	return AccessibilityResolveTarget
}

func (axResolveTargetCommand) applyProviderAction(*ProviderActionRequest) {}

func (axResolveTargetCommand) appendTraceFields(map[string]any) {}

type AXClickCommand struct {
	X           *float64
	Y           *float64
	ClickCount  int
	MouseButton string
	ClickMethod string
}

func (AXClickCommand) Intent() AccessibilityOperation {
	return AccessibilityClick
}

func (command AXClickCommand) applyProviderAction(
	request *ProviderActionRequest,
) {
	request.Click = &ProviderClickInput{
		X:           command.X,
		Y:           command.Y,
		ClickCount:  command.ClickCount,
		MouseButton: command.MouseButton,
		ClickMethod: command.ClickMethod,
	}
}

func (command AXClickCommand) appendTraceFields(fields map[string]any) {
	fields["x"] = command.X
	fields["y"] = command.Y
	fields["click_count"] = command.ClickCount
	fields["mouse_button"] = command.MouseButton
	fields["click_method"] = command.ClickMethod
}

type axScrollCommand struct {
	Direction    string
	Pages        float64
	WindowCentre bool
}

func (axScrollCommand) Intent() AccessibilityOperation {
	return AccessibilityScroll
}

func (command axScrollCommand) applyProviderAction(
	request *ProviderActionRequest,
) {
	request.Scroll = &ProviderScrollInput{
		Direction:    command.Direction,
		Pages:        command.Pages,
		WindowCentre: command.WindowCentre,
	}
}

func (command axScrollCommand) appendTraceFields(fields map[string]any) {
	fields["direction"] = command.Direction
	fields["pages"] = command.Pages
	fields["window_centre"] = command.WindowCentre
}

type axTypeCommand struct {
	Text string
}

func (axTypeCommand) Intent() AccessibilityOperation {
	return AccessibilityType
}

func (command axTypeCommand) applyProviderAction(
	request *ProviderActionRequest,
) {
	request.Type = &ProviderTypeInput{Text: command.Text}
}

func (command axTypeCommand) appendTraceFields(fields map[string]any) {
	fields["text"] = command.Text
}

type axKeypressCommand struct {
	Key string
}

func (axKeypressCommand) Intent() AccessibilityOperation {
	return AccessibilityKeypress
}

func (command axKeypressCommand) applyProviderAction(
	request *ProviderActionRequest,
) {
	request.Keypress = &ProviderKeypressInput{Key: command.Key}
}

func (command axKeypressCommand) appendTraceFields(fields map[string]any) {
	fields["key"] = command.Key
}

type axSetValueCommand struct {
	Value                string
	RequireSemanticWrite bool
}

func (axSetValueCommand) Intent() AccessibilityOperation {
	return AccessibilitySetValue
}

func (command axSetValueCommand) applyProviderAction(
	request *ProviderActionRequest,
) {
	value := command.Value
	request.SetValue = &ProviderSetValueInput{Value: &value, RequireSemanticWrite: command.RequireSemanticWrite}
}

func (command axSetValueCommand) appendTraceFields(fields map[string]any) {
	fields["value"] = command.Value
}

// axSelectCommand selects text, a cell range or an item. To is the range
// end (to_ref) as a semantic target; Text names a substring of the target.
type axSelectCommand struct {
	Text string
	To   *semanticTarget
}

func (axSelectCommand) Intent() AccessibilityOperation {
	return AccessibilitySelect
}

func (command axSelectCommand) applyProviderAction(
	request *ProviderActionRequest,
) {
	request.Select = &ProviderSelectInput{
		Text:     command.Text,
		ToTarget: providerTargetFromOperation(command.To),
	}
}

func (command axSelectCommand) appendTraceFields(fields map[string]any) {
	if command.Text != "" {
		fields["text"] = command.Text
	}
	if command.To != nil {
		fields["to_selector"] = command.To.Selector
	}
}

func (request AXServiceRequest) operationName() string {
	if request.Action != nil {
		return string(request.Action.Intent())
	}
	if request.WindowAction != nil {
		return string(*request.WindowAction)
	}
	return string(request.Operation)
}

type semanticTargetPredicate struct {
	Kind  string `json:"kind"`
	Value string `json:"value"`
}

type semanticTarget struct {
	EditTarget                      *axRelationTarget         `json:"editTarget,omitempty"`
	Selector                        string                    `json:"selector"`
	LocatorKind                     AXLocatorKind             `json:"locatorKind,omitempty"`
	AllowProviderRebind             bool                      `json:"allowProviderRebind,omitempty"`
	WindowHandle                    string                    `json:"windowHandle,omitempty"`
	ProviderBusName                 string                    `json:"providerBusName,omitempty"`
	ProviderObjectPath              string                    `json:"providerObjectPath,omitempty"`
	ProcessGeneration               string                    `json:"processGeneration,omitempty"`
	ProviderWindowBusName           string                    `json:"providerWindowBusName,omitempty"`
	ProviderWindowObjectPath        string                    `json:"providerWindowObjectPath,omitempty"`
	ProviderWindowProcessGeneration string                    `json:"providerWindowProcessGeneration,omitempty"`
	ProviderWindowRole              string                    `json:"providerWindowRole,omitempty"`
	ProviderWindowName              string                    `json:"providerWindowName,omitempty"`
	ProviderRoleAliases             []string                  `json:"providerRoleAliases,omitempty"`
	ProviderIdentityScope           string                    `json:"providerIdentityScope,omitempty"`
	AutomationID                    string                    `json:"automationId,omitempty"`
	RuntimeID                       []int                     `json:"runtimeId,omitempty"`
	RuntimeIDAliases                [][]int                   `json:"runtimeAliases,omitempty"`
	SourceIndex                     *int                      `json:"sourceIndex,omitempty"`
	Role                            string                    `json:"role,omitempty"`
	Name                            string                    `json:"name,omitempty"`
	HasName                         bool                      `json:"hasName"`
	Predicates                      []semanticTargetPredicate `json:"predicates,omitempty"`
}

type RuntimeActionEvidence struct {
	*RuntimeTextDeliveryEvidence
	DiagnosticTimingMS  map[string]float64 `json:"diagnostic_timing_ms,omitempty"`
	Route               string             `json:"route"`
	EmissionRoute       string             `json:"emission_route,omitempty"`
	GeometrySampled     bool               `json:"geometry_sampled"`
	GeometryStable      *bool              `json:"geometry_stable,omitempty"`
	WindowFocusVerified *bool              `json:"window_focus_verified,omitempty"`
	TargetFocusVerified *bool              `json:"target_focus_verified,omitempty"`
	PointOwnerVerified  *bool              `json:"point_owner_verified,omitempty"`
	Enabled             *bool              `json:"enabled,omitempty"`
	Editable            *bool              `json:"editable,omitempty"`
	FocusVerified       *bool              `json:"focus_verified,omitempty"`
	EditableVerified    *bool              `json:"editable_verified,omitempty"`
	ReadbackSampled     bool               `json:"readback_sampled"`
	ReadbackVerified    *bool              `json:"readback_verified,omitempty"`
	InputEmitted        bool               `json:"input_emitted"`
}

// RuntimeTextDeliveryEvidence records message completion, not accepted text.
// UTF-16 and message indices are zero-based; a nil unknown index means no
// message was left awaiting a completion result.
// SourceUTF16Count is the source span of the current message, not a count
// of accepted characters; a string insertion can cover the entire span.
// A nil count means that the provider did not report the span.
type RuntimeTextDeliveryEvidence struct {
	SourceUTF16Index    int    `json:"source_utf16_index"`
	SourceUTF16Count    *int   `json:"source_utf16_count,omitempty"`
	SourceUTF16Length   int    `json:"source_utf16_length"`
	AttemptedMessages   int    `json:"attempted_messages"`
	ReturnedMessages    int    `json:"returned_messages"`
	UnknownMessageIndex *int   `json:"unknown_message_index"`
	Message             uint32 `json:"message"`
	SequencePhase       string `json:"sequence_phase"`
	EffectVerified      bool   `json:"effect_verified"`
}
