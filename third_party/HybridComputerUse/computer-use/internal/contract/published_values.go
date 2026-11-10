package contract

import (
	_ "image/jpeg"
	_ "image/png"
)

const MaxAXFindPageSize = 50

const (
	TreeViewWorking = "working"
	TreeViewFull    = "full"
	AXScopeWindow   = "window"
	AXScopeContent  = "content"
)

var AXScopeValues = []string{AXScopeWindow, AXScopeContent}

const (
	DefaultSettleMS = 800
	MaxSettleMS     = 3000
)

// Public AX error codes. Internal failure codes stay in telemetry; the model
// sees only these.
const (
	AXCodeInvalidArgument  = "invalid_argument"
	AXCodeStaleRef         = "stale_ref"
	AXCodeWindowChanged    = "window_changed"
	AXCodeWaitTimeout      = "wait_timeout"
	AXCodeEffectUnknown    = "effect_unknown"
	AXCodeAppUnavailable   = "app_unavailable"
	AXCodeRuntimeError     = "runtime_error"
	AXCodeAmbiguousWindow  = "ambiguous_window"
	AXCodeWindowNotFound   = "window_not_found"
	AXCodeNoFocusedWindow  = "no_focused_window"
	AXModeApps             = "apps"
	AXDefaultFindPageSize  = 20
	AXDefaultWaitTimeout   = 5000
	AXPublicErrorCodeKey   = "public_error_code"
	AXPublicErrorPathKey   = "public_error_path"
	AXPublicErrorHintKey   = "public_error_hint"
	AXPublicErrorDetailKey = "public_error_message"
)

var AXActionTypes = []string{
	"click", "type", "keypress", "set_value", "select", "scroll", "activate", "close", "wait",
}

// AXFindFilters are the query fields shared by find and a wait step.
var AXFindFilters = []string{"role", "name_equals", "name_contains", "text_equals", "text_contains", "state"}

const (
	AXModeObserve = "observe"
	AXModeFind    = "find"
	AXModeAct     = "act"
	// AXModeAssert names the internal predicate check that a wait step
	// runs. It is not a public mode.
	AXModeAssert = "assert_ax"
)

const GUICodePermissionDenied = "permission_denied"

const GUIPermissionHint = "Ask the user to grant the denied system permission to the current Computer Use Provider " +
	"(on macOS: Screen Recording or Accessibility in System Settings > Privacy & Security), " +
	"restart the provider, then take a fresh GUI screenshot before retrying input."

const (
	FormatText    = "text"
	FormatRecords = "records"
)

// RecordsOpRead is the records-only AX mode {"mode":"read","refs":[...]}.
// It has no MCP form: the published schema and the model contract do not
// list it, and the text format rejects it.
const RecordsOpRead = "read"
