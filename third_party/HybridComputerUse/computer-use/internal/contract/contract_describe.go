package contract

import (
	"errors"
	"time"
)

// ContractVersion is the version of the code-facing contract: the published
// tool schemas, the records envelope and the error table below. Adding an
// optional field or operation raises the minor number; removing or changing
// one raises the major number.
const ContractVersion = "1.0"

// AXCodeGUIFrameStale is the GUI code for coordinates that refer to a
// screenshot that no longer matches the desktop layout. Nothing was sent.
const AXCodeGUIFrameStale = "gui_frame_stale"

// PublicError describes one public error code for code callers. It is the
// only table: the schema output, the CLI exit codes and the SDK exception
// classes all come from it.
type PublicError struct {
	// ExitCode is the CLI exit status.
	ExitCode int `json:"exit_code"`
	// InputEmitted says whether input may already have been sent when this
	// code is returned: "no", "maybe" (read completed/input_emitted), or
	// "yes".
	InputEmitted string `json:"input_emitted"`
	// Recovery is the next step a caller should take.
	Recovery string `json:"recovery"`
}

var PublicErrorCodes = map[string]PublicError{
	AXCodeInvalidArgument:   {2, "no", "fix the argument named by path; hint is a corrected call"},
	AXCodeWaitTimeout:       {3, "maybe", "observe again; completed counts the steps that ran and the message says what the wait saw"},
	AXCodeStaleRef:          {4, "no", "observe or find again and use the new ref; refs never rebind"},
	AXCodeGUIFrameStale:     {4, "no", "take a new GUI screenshot and plan coordinates from it"},
	AXCodeWindowChanged:     {5, "maybe", "observe before retrying; do not repeat the batch blindly"},
	AXCodeEffectUnknown:     {5, "maybe", "observe before retrying; input may have taken effect"},
	AXCodeAppUnavailable:    {6, "no", "list apps; the application is not running or not accessible"},
	AXCodeAmbiguousWindow:   {6, "no", "use a longer title or the wN from apps"},
	AXCodeWindowNotFound:    {6, "no", "list apps and use a wN or an exact title"},
	AXCodeNoFocusedWindow:   {6, "no", "activate a window first or pass window_ref"},
	AXCodeRuntimeError:      {10, "maybe", "retry reads later; observe before retrying input"},
	GUICodePermissionDenied: {6, "no", GUIPermissionHint},
}

// DescribeContract is the machine-readable contract: what `schema` prints
// and what the SDK and the PTC adapter read at runtime instead of a
// generated copy.
func DescribeContract() map[string]any {
	tools := []map[string]any{}
	for _, tool := range AllToolDefinitions() {
		tools = append(tools, map[string]any{
			"name":         tool.Name,
			"title":        tool.Title,
			"description":  tool.Description,
			"input_schema": tool.InputSchema,
		})
	}
	return map[string]any{
		"contract_version": ContractVersion,
		"version":          ServerVersion,
		"tools":            tools,
		"errors":           PublicErrorCodes,
		"usage_exit_code":  64,
		"records": map[string]any{
			"formats":          []string{FormatText, FormatRecords},
			"records_only_op":  map[string]any{"mode": RecordsOpRead, "refs": "1 to 200 element refs rN"},
			"call_options":     []string{"format", "request_id", "since", "refs"},
			"request_id_ttl_s": int(RequestResultTTL / time.Second),
		},
	}
}

// ServerVersion is the version the server reports. main sets it from the
// release build's -X main.version value before any command runs.
var ServerVersion = "dev"

const (
	RequestResultTTL   = 10 * time.Minute
	MaxRequestResults  = 512
	RequestIDMaxLength = 128
)

var ErrReplayUnavailable = errors.New("original request result is unavailable; input may have run; automatic replay stopped")
