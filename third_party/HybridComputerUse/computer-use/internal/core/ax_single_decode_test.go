package core

import (
	"encoding/json"
	"errors"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// countAXDecodes counts decodeAXRequest calls while run executes.
func countAXDecodes(t *testing.T, run func()) int {
	t.Helper()
	count := 0
	axDecodeHook = func() { count++ }
	defer func() { axDecodeHook = nil }()
	run()
	return count
}

// axSingleDecodeCalls are valid and invalid AX calls. The valid ones reach
// a stub backend that fails every request.
var axSingleDecodeCalls = []string{
	`{"mode":"apps"}`,
	`{"mode":"observe","window_ref":"w1","observe":"tree"}`,
	`{"mode":"observe","app":"a1","root":"focused"}`,
	`{"mode":"find","window_ref":"w1","query":{"role":"button"}}`,
	`{"mode":"find","cursor":"f9"}`,
	`{"mode":"act","window_ref":"w1","actions":[{"type":"keypress","keys":["CTRL","S"]}]}`,
	`{"mode":"act","actions":[{"type":"click","ref":"r1"}],"until":{"type":"exists","ref":"r1","timeout_ms":1}}`,
	`{"mode":"apps","observe":"none"}`,
	`{"mode":"observe","window_ref":"w2","root":"w2"}`,
	`{"mode":"act","actions":[{"type":"close","window_ref":"w2"}]}`,
	`{"mode":"find","window_ref":"w1","query":{"role":"x"},"limit":100}`,
	`{"mode":"observe","app":"x","window_ref":"w1"}`,
	`{"mode":"act","actions":[]}`,
}

// Each AX call is decoded once, and the result equals the earlier path that
// decoded in dispatch and again in dispatchValidated.
func TestAXCallDecodesArgumentsOnce(t *testing.T) {
	for _, raw := range axSingleDecodeCalls {
		current := singleDecodeService(t)
		var got ToolCallResult
		if n := countAXDecodes(t, func() {
			got = current.dispatch(contract.PublicToolAX, decodeTestArguments(t, raw))
		}); n != 1 {
			t.Fatalf("%s decoded %d times", raw, n)
		}
		earlier := singleDecodeService(t)
		want := func() ToolCallResult {
			args := decodeTestArguments(t, raw)
			if _, argErr := DecodeAXRequest(args); argErr != nil {
				requested := 0
				if actions, ok := args["actions"].([]any); ok {
					requested = len(actions)
				}
				return axArgErrorResult(argErr, requested)
			}
			if argErr := contract.ValidateToolArguments(contract.PublicToolAX, args); argErr != nil {
				return argumentErrorResult(contract.PublicToolAX, args, argErr)
			}
			return earlier.dispatchValidated(contract.PublicToolAX, args)
		}()
		gotJSON, _ := json.Marshal(got)
		wantJSON, _ := json.Marshal(want)
		if string(gotJSON) != string(wantJSON) {
			t.Fatalf("%s changed:\n got %s\nwant %s", raw, gotJSON, wantJSON)
		}
	}
}

func singleDecodeService(t *testing.T) *Service {
	service, _, _, _ := sheetBatchService(t)
	service.Backend = &recordingPlatformBackend{err: errors.New("no desktop in this test")}
	return service
}
