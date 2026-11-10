package core

import (
	"encoding/json"
	"math"
	"strings"
	"testing"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// zoomGeometryForTest mirrors the Linux provider's zoom_geometry for a
// 1920x1080 desktop delivered as a 1365x768 frame: the region snaps outward
// to native pixels and the image enlarges the crop at most 4x, within the
// model box. Rounding the output size is where a shared scale broke.
func zoomGeometryForTest(region [4]float64, nativeW, nativeH, frameW, frameH int) ProviderGUIZoom {
	perX, perY := float64(nativeW)/float64(frameW), float64(nativeH)/float64(frameH)
	left := math.Max(0, math.Floor(region[0]*perX+1e-9))
	top := math.Max(0, math.Floor(region[1]*perY+1e-9))
	right := math.Min(float64(nativeW), math.Ceil(region[2]*perX-1e-9))
	bottom := math.Min(float64(nativeH), math.Ceil(region[3]*perY-1e-9))
	cropW, cropH := right-left, bottom-top
	enlarge := math.Min(4, math.Min(1366/cropW, 768/cropH))
	return ProviderGUIZoom{
		Region: []float64{left / perX, top / perY, right / perX, bottom / perY},
		Width:  int(math.Max(1, math.Round(cropW*enlarge))),
		Height: int(math.Max(1, math.Round(cropH*enlarge))),
	}
}

// TestZoomMappingErrorStaysWithinTwoFramePixels maps every zoom pixel corner
// and centre back through the printed text and checks it against the frame
// point the provider cropped there, for narrow, wide, tall and edge regions.
func TestZoomMappingErrorStaysWithinTwoFramePixels(t *testing.T) {
	frame := &AppSnapshot{FrameID: "layout-v1-abc12345", Width: 1365, Height: 768}
	regions := map[string][4]float64{
		"minimum side":            {100, 0, 108, 8},
		"narrow column, full h":   {100, 0, 108, 768},
		"narrow row, full width":  {0, 300, 1365, 308},
		"toolbar strip":           {0, 30, 700, 70},
		"dense icon":              {612.3, 61.7, 640.9, 83.2},
		"whole frame":             {0, 0, 1365, 768},
		"bottom right edge":       {1357, 760, 1365, 768},
		"fractional, off-grid":    {333.33, 444.44, 345.67, 455.55},
		"historical 49 px defect": {100, 0, 108.5, 768},
	}
	for name, region := range regions {
		t.Run(name, func(t *testing.T) {
			zoom := zoomGeometryForTest(region, 1920, 1080, frame.Width, frame.Height)
			m := zoomMapping(zoom, frame)
			worst := 0.0
			for _, u := range []float64{0, float64(zoom.Width) / 2, float64(zoom.Width)} {
				for _, v := range []float64{0, float64(zoom.Height) / 2, float64(zoom.Height)} {
					wantX := zoom.Region[0] + u/float64(zoom.Width)*(zoom.Region[2]-zoom.Region[0])
					wantY := zoom.Region[1] + v/float64(zoom.Height)*(zoom.Region[3]-zoom.Region[1])
					gotX, gotY := m.frameFromZoom(u, v)
					worst = math.Max(worst, math.Max(math.Abs(gotX-wantX), math.Abs(gotY-wantY)))
				}
			}
			if worst > guiZoomMaxError {
				t.Fatalf("mapping error %.3f px > %g: %s", worst, guiZoomMaxError, m.text())
			}
			// The snapped region always contains the request.
			if zoom.Region[0] > region[0] || zoom.Region[1] > region[1] ||
				zoom.Region[2] < region[2] || zoom.Region[3] < region[3] {
				t.Fatalf("snapped region %v does not contain %v", zoom.Region, region)
			}
		})
	}
}

func zoomBackend(t *testing.T) *recordingPlatformBackend {
	t.Helper()
	return &recordingPlatformBackend{desktopResponse: &ProviderDesktopResult{
		OK: true,
		GUIResult: ProviderGUIResult{Zoom: &ProviderGUIZoom{
			Region: []float64{99.5313, 0, 108.0625, 768}, Width: 48, Height: 768,
		}},
		Snapshot: &AppSnapshot{
			ScreenshotPNGBase64: testPNGBase64(t, 48, 768), ScreenshotMimeType: "image/png",
			CoordinateSpace: "zoom_image_px", Width: 48, Height: 768,
		},
	}}
}

func TestZoomIsLookOnlyAndNamesTheCurrentFrame(t *testing.T) {
	backend := zoomBackend(t)
	s := NewServiceWithBackend(backend)
	current := &AppSnapshot{FrameID: "layout-v1-0123456789abcdef", Width: 1365, Height: 768}
	s.guiSnapshot = current
	result := s.CallForModel(contract.PublicToolGUI, map[string]any{
		"actions": []any{map[string]any{"type": "zoom", "region": []any{100.0, 0.0, 108.0, 768.0}}},
	}, false)
	if result.IsError {
		t.Fatalf("zoom failed: %s", ResultText(result))
	}
	request := backend.desktopRequests[0]
	if request.FrameID != current.FrameID || request.Actions[0].Type != "zoom" {
		t.Fatalf("zoom did not name the current frame: %#v", request)
	}
	if s.guiSnapshot != current {
		t.Fatal("a zoom image must never become the GUI frame")
	}
	text := ResultText(result)
	for _, want := range []string{
		`"zoom":{"frame":"89abcdef","height":768,"region":[99.5313,0,108.0625,768],"scale_x":5.6264,"scale_y":1,"width":48}`,
		"region [99.5313,0,108.0625,768] of the current 1365x768 GUI frame (frame 89abcdef), shown at 48x768",
		"x = 99.5313 + u/5.6264, y = 0 + v/1",
		"Look only: GUI x/y stay frame pixels",
	} {
		if !strings.Contains(text, want) {
			t.Fatalf("zoom result lacks %q:\n%s", want, text)
		}
	}
	// A click after the zoom is checked against the frame, not the zoom.
	backend.desktopResponse = testDesktopFrame(t, "after", 1365, 768)
	s.CallForModel(contract.PublicToolGUI, map[string]any{
		"actions": []any{map[string]any{"type": "click", "x": 104.0, "y": 300.0}},
	}, false)
	if backend.desktopRequests[1].FrameID != current.FrameID {
		t.Fatalf("click after zoom used frame %q", backend.desktopRequests[1].FrameID)
	}
}

func TestZoomRejectsRegionsBeforeCapture(t *testing.T) {
	for name, region := range map[string][]any{
		"too narrow":   {100.0, 0.0, 107.0, 768.0},
		"too short":    {0.0, 10.0, 400.0, 17.5},
		"outside":      {1300.0, 700.0, 1366.0, 768.0},
		"inverted":     {200.0, 10.0, 100.0, 50.0},
		"three values": {1.0, 2.0, 3.0},
	} {
		t.Run(name, func(t *testing.T) {
			backend := zoomBackend(t)
			s := NewServiceWithBackend(backend)
			s.guiSnapshot = &AppSnapshot{FrameID: "f1", Width: 1365, Height: 768}
			result := s.CallForModel(contract.PublicToolGUI, map[string]any{
				"actions": []any{map[string]any{"type": "zoom", "region": region}},
			}, false)
			if !result.IsError || len(backend.desktopRequests) != 0 ||
				!strings.Contains(ResultText(result), `"code":"invalid_argument"`) {
				t.Fatalf("want invalid_argument before capture, got %d requests: %s",
					len(backend.desktopRequests), ResultText(result))
			}
		})
	}
}

func TestZoomWithoutAFrameReturnsOneAsStale(t *testing.T) {
	backend := &recordingPlatformBackend{desktopResponse: testDesktopFrame(t, "fresh", 1365, 768)}
	s := NewServiceWithBackend(backend)
	result := s.CallForModel(contract.PublicToolGUI, map[string]any{
		"actions": []any{map[string]any{"type": "zoom", "region": []any{0.0, 0.0, 100.0, 100.0}}},
	}, false)
	if !result.IsError || !strings.HasPrefix(ResultText(result), `{"ok":false,"code":"gui_frame_stale"`) ||
		backend.desktopRequests[0].Actions[0].Type != "screenshot" || s.guiSnapshot.FrameID != "fresh" {
		t.Fatalf("zoom without a frame must observe and fail stale: %s", ResultText(result))
	}
}

func TestZoomStaleOverTheRegionDeliversAFreshFrame(t *testing.T) {
	stale := testDesktopFrame(t, "moved", 1365, 768)
	stale.OK = false
	stale.Error = "The windows over the zoom region changed since the frame its coordinates came from."
	stale.Failure = &ProviderFailure{Code: "stale_gui_frame", Phase: "precondition"}
	backend := &recordingPlatformBackend{desktopResponse: stale}
	s := NewServiceWithBackend(backend)
	s.guiSnapshot = &AppSnapshot{FrameID: "old", Width: 1365, Height: 768}
	result := s.CallForModel(contract.PublicToolGUI, map[string]any{
		"actions": []any{map[string]any{"type": "zoom", "region": []any{0.0, 0.0, 100.0, 100.0}}},
	}, false)
	if !strings.HasPrefix(ResultText(result), `{"ok":false,"code":"gui_frame_stale"`) || s.guiSnapshot.FrameID != "moved" {
		t.Fatalf("stale zoom must deliver and register the fresh frame: %s", ResultText(result))
	}
}

func TestZoomRunsOnEveryPlatform(t *testing.T) {
	for _, platform := range []string{"linux", "darwin", "windows"} {
		backend := &platformRecordingBackend{recordingPlatformBackend: zoomBackend(t), platform: platform}
		s := NewServiceWithBackend(backend)
		s.guiSnapshot = &AppSnapshot{FrameID: "f1", Width: 1365, Height: 768}
		result := s.CallForModel(contract.PublicToolGUI, map[string]any{
			"actions": []any{map[string]any{"type": "zoom", "region": []any{100.0, 0.0, 108.0, 768.0}}},
		}, false)
		if result.IsError || len(backend.desktopRequests) != 1 || backend.desktopRequests[0].FrameID != "f1" {
			t.Fatalf("%s: zoom must reach the provider with the frame: %s", platform, ResultText(result))
		}
	}
}

func TestZoomProviderRegionRejectionIsInvalidArgument(t *testing.T) {
	for _, code := range []string{"invalid_gui_action", "invalid_zoom_region"} {
		backend := &recordingPlatformBackend{desktopResponse: &ProviderDesktopResult{
			OK: false, Error: "zoom region sides must be at least 8 frame pixels",
			Failure: &ProviderFailure{Code: code, Phase: "precondition"},
		}}
		s := NewServiceWithBackend(backend)
		s.guiSnapshot = &AppSnapshot{FrameID: "f1", Width: 1365, Height: 768}
		result := s.CallForModel(contract.PublicToolGUI, map[string]any{
			"actions": []any{map[string]any{"type": "zoom", "region": []any{0.0, 0.0, 100.0, 100.0}}},
		}, false)
		if !strings.Contains(ResultText(result), `"code":"invalid_argument","path":"actions[0].region"`) {
			t.Fatalf("%s: %s", code, ResultText(result))
		}
	}
}

type platformRecordingBackend struct {
	*recordingPlatformBackend
	platform string
}

func (b *platformRecordingBackend) HostInfo() HostBackendInfo {
	return HostBackendInfo{Platform: b.platform}
}

func TestGUISchemaWithZoomStaysWithinCodexBudget(t *testing.T) {
	gui := findToolDefinition(t, contract.PublicToolGUI)
	stages := codexCompact(gui.InputSchema)
	if len(stages) != 1 || stages[0].bytes > codexSchemaBudget {
		t.Fatalf("GUI schema triggers client compaction: %+v", stages)
	}
	encoded, _ := json.Marshal(gui.InputSchema)
	t.Logf("GUI schema: raw=%d compacted=%d budget=%d; description=%d bytes",
		len(encoded), stages[0].bytes, codexSchemaBudget, len(gui.Description))
	if !strings.Contains(string(encoded), `"zoom"`) {
		t.Fatal("GUI schema does not publish zoom")
	}
	if len(gui.Description) > 1000 {
		t.Fatalf("GUI description is %d bytes; clients may truncate at 1,000", len(gui.Description))
	}
}
