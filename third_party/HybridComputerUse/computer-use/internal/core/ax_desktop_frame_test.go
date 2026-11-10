package core

import (
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// desktopFrameBackend returns a window tree whose provider image (if any
// were requested) would be window-sized, and a desktop frame of another
// size, so a test can tell which one reached the model.
func desktopFrameBackend(t *testing.T) *recordingPlatformBackend {
	t.Helper()
	return &recordingPlatformBackend{
		accessibilityResponse: &ProviderAccessibilityResult{OK: true, Snapshot: &AppSnapshot{
			App:                 AppDescriptor{Name: "Editor", PID: 7},
			WindowTitle:         "Editor",
			WindowHandle:        "window-7",
			TreeLines:           []string{`ax:`, ` - window "Editor"`},
			ScreenshotPNGBase64: testPNGBase64(t, 40, 30),
			ScreenshotMimeType:  "image/png",
		}},
		desktopResponse: testDesktopFrame(t, "ax-frame", 136, 77),
	}
}

func onlyImage(t *testing.T, result ToolCallResult) ContentItem {
	t.Helper()
	var images []ContentItem
	for _, item := range result.Content {
		if item.Type == "image" {
			images = append(images, item)
		}
	}
	if len(images) != 1 {
		t.Fatalf("want one image, got %d: %#v", len(images), result.Content)
	}
	return images[0]
}

func TestAXImagesAreTheFullDesktopGUIFrame(t *testing.T) {
	for _, observe := range []string{contract.ObserveFull, contract.ObserveScreenshot} {
		t.Run(observe, func(t *testing.T) {
			backend := desktopFrameBackend(t)
			s := NewServiceWithBackend(backend)
			result := s.CallForModel(contract.PublicToolAX, map[string]any{
				"mode": "observe", "app": "Editor", "observe": observe,
			}, false)
			if result.IsError {
				t.Fatalf("observe failed: %s", ResultText(result))
			}
			image := onlyImage(t, result)
			if image.Data != backend.desktopResponse.Snapshot.ScreenshotPNGBase64 {
				t.Fatal("AX returned the provider window image, not the desktop frame")
			}
			if len(backend.observeRequests) != 1 ||
				backend.observeRequests[0].Observation.IncludeScreenshot == nil ||
				*backend.observeRequests[0].Observation.IncludeScreenshot {
				t.Fatalf("the accessibility read must not capture a window image: %#v",
					backend.observeRequests)
			}
			if len(backend.desktopRequests) != 1 ||
				len(backend.desktopRequests[0].Actions) != 1 ||
				backend.desktopRequests[0].Actions[0].Type != "screenshot" {
				t.Fatalf("want one GUI screenshot capture, got %#v", backend.desktopRequests)
			}
			if !strings.Contains(ResultText(result), "image: full desktop 136x77, the current GUI frame") {
				t.Fatalf("result does not state the image size and frame: %s", ResultText(result))
			}
			if s.guiSnapshot == nil || s.guiSnapshot.FrameID != "ax-frame" {
				t.Fatalf("delivered AX image did not become the GUI frame: %#v", s.guiSnapshot)
			}
		})
	}
}

func TestGUIClickAfterAXFullUsesItsFrameWithoutAnotherScreenshot(t *testing.T) {
	backend := desktopFrameBackend(t)
	s := NewServiceWithBackend(backend)
	s.CallForModel(contract.PublicToolAX, map[string]any{
		"mode": "observe", "app": "Editor", "observe": "full",
	}, false)
	backend.desktopResponse = testDesktopFrame(t, "after-click", 136, 77)
	result := s.CallForModel(contract.PublicToolGUI, map[string]any{
		"actions": []any{map[string]any{"type": "click", "x": 60.0, "y": 40.0}},
	}, false)
	if result.IsError {
		t.Fatalf("click after AX full was rejected: %s", ResultText(result))
	}
	if len(backend.desktopRequests) != 2 {
		t.Fatalf("want the AX capture and the click batch only, got %d desktop requests",
			len(backend.desktopRequests))
	}
	click := backend.desktopRequests[1]
	if click.FrameID != "ax-frame" || len(click.Actions) != 1 || click.Actions[0].Type != "click" {
		t.Fatalf("click was not checked against the AX frame: %#v", click)
	}
}

func TestGUIClickAfterAXFullIsStaleWhenTheWindowUnderThePointChanged(t *testing.T) {
	backend := desktopFrameBackend(t)
	s := NewServiceWithBackend(backend)
	s.CallForModel(contract.PublicToolAX, map[string]any{
		"mode": "observe", "app": "Editor", "observe": "full",
	}, false)
	// The provider's point rule rejects the batch before input and attaches
	// the fresh desktop.
	stale := testDesktopFrame(t, "moved", 136, 77)
	stale.OK = false
	stale.Error = "The desktop layout changed since the screenshot used for coordinates."
	stale.Failure = &ProviderFailure{Code: "stale_gui_frame", Phase: "precondition"}
	backend.desktopResponse = stale
	result := s.CallForModel(contract.PublicToolGUI, map[string]any{
		"actions": []any{map[string]any{"type": "click", "x": 60.0, "y": 40.0}},
	}, false)
	if backend.desktopRequests[1].FrameID != "ax-frame" {
		t.Fatalf("click did not name the AX frame: %#v", backend.desktopRequests[1])
	}
	if !result.IsError || !strings.HasPrefix(ResultText(result), `{"ok":false,"code":"gui_frame_stale"`) {
		t.Fatalf("want gui_frame_stale, got %s", ResultText(result))
	}
	if s.guiSnapshot == nil || s.guiSnapshot.FrameID != "moved" {
		t.Fatal("the fresh desktop attached to the stale error must become the GUI frame")
	}
}

func TestAXReadsWithoutAnImageLeaveTheGUIFrame(t *testing.T) {
	backend := desktopFrameBackend(t)
	s := NewServiceWithBackend(backend)
	previous := &AppSnapshot{FrameID: "gui-frame"}
	s.guiSnapshot = previous
	for _, observe := range []string{contract.ObserveTree, contract.ObserveDiff} {
		result := s.CallForModel(contract.PublicToolAX, map[string]any{
			"mode": "observe", "app": "Editor", "observe": observe,
		}, false)
		for _, item := range result.Content {
			if item.Type == "image" {
				t.Fatalf("%s returned an image", observe)
			}
		}
	}
	if len(backend.desktopRequests) != 0 || s.guiSnapshot != previous {
		t.Fatalf("an AX read without an image captured the desktop or moved the GUI frame")
	}
}

func TestAXDesktopCaptureFailureReturnsTheTreeWithoutAFrame(t *testing.T) {
	backend := desktopFrameBackend(t)
	backend.desktopResponse = &ProviderDesktopResult{OK: false, Error: "capture unavailable"}
	s := NewServiceWithBackend(backend)
	result := s.CallForModel(contract.PublicToolAX, map[string]any{
		"mode": "observe", "app": "Editor", "observe": "full",
	}, false)
	text := ResultText(result)
	if result.IsError || !strings.Contains(text, `window: "Editor"`) ||
		!strings.Contains(text, "full-desktop capture failed: capture unavailable") {
		t.Fatalf("capture failure must keep the tree and say why: %q", text)
	}
	for _, item := range result.Content {
		if item.Type == "image" {
			t.Fatal("the provider window image leaked after a desktop capture failure")
		}
	}
	if s.guiSnapshot != nil {
		t.Fatal("a failed capture must not become the GUI frame")
	}
}

func TestAXFullWithoutTheGUIChannelStillReturnsTheDesktopFrame(t *testing.T) {
	t.Setenv("COMPUTER_USE_CHANNELS", "a11y,keyboard")
	for _, tool := range contract.ToolDefinitions() {
		if tool.Name == contract.PublicToolGUI {
			t.Fatal("GUI is listed although its channel is disabled")
		}
	}
	backend := desktopFrameBackend(t)
	s := NewServiceWithBackend(backend)
	result := s.CallForModel(contract.PublicToolAX, map[string]any{
		"mode": "observe", "app": "Editor", "observe": "full",
	}, false)
	if onlyImage(t, result).Data != backend.desktopResponse.Snapshot.ScreenshotPNGBase64 {
		t.Fatal("AX full did not return the desktop frame")
	}
	text := ResultText(result)
	if !strings.Contains(text, "image: full desktop 136x77.") ||
		strings.Contains(text, "GUI") {
		t.Fatalf("the image line must not mention GUI when GUI is disabled: %s", text)
	}
}

func TestAXErrorImagesBecomeTheGUIFrameOnlyWhenDelivered(t *testing.T) {
	frame := testDesktopFrame(t, "recovery-frame", 136, 77).Snapshot
	failed := func() ToolCallResult {
		return ToolCallResult{IsError: true, Content: []ContentItem{
			{Type: "text", Text: "effect unknown; current state follows"},
			{Type: "image", Data: frame.ScreenshotPNGBase64, MimeType: "image/png", frame: frame},
		}}
	}
	previous := &AppSnapshot{FrameID: "gui-frame"}

	// observe full: the caller asked for the image, so the fresh desktop
	// attached to the error is delivered and becomes the frame.
	s := NewService()
	s.guiSnapshot = previous
	delivered := s.deliverAXImages(contract.PublicToolAX,
		map[string]any{"mode": "act", "observe": "full"}, failed())
	onlyImage(t, delivered)
	if s.guiSnapshot != frame || !strings.Contains(ResultText(delivered), "image: full desktop 136x77") {
		t.Fatalf("a delivered error image must become the GUI frame: %#v", s.guiSnapshot)
	}

	// observe diff: the image is withheld, so the frame does not move.
	s = NewService()
	s.guiSnapshot = previous
	withheld := s.deliverAXImages(contract.PublicToolAX,
		map[string]any{"mode": "act", "observe": "diff"}, failed())
	for _, item := range withheld.Content {
		if item.Type == "image" {
			t.Fatal("an image the caller did not request was delivered")
		}
	}
	if s.guiSnapshot != previous || strings.Contains(ResultText(withheld), "image: full desktop") {
		t.Fatal("a withheld image must not move the GUI frame or be described")
	}
}
