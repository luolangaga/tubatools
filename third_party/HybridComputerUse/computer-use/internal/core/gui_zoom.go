package core

import (
	"fmt"
	"math"
	"strconv"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// guiZoomMaxError is the largest distance, in frame pixels, between a frame
// point and the point its stated mapping gives for the matching zoom pixel.
// The provider maps per axis with exact float ratios; the result prints the
// region and scales with enough digits that rounding stays far below this.
const guiZoomMaxError = 2.0

// guiZoom returns an enlarged region of the current GUI frame. It is look
// only: coordinates remain pixels of that frame, the zoom image never
// becomes a frame, and the next coordinate action is checked against the
// frame by the provider's point rule. The provider also rejects the zoom
// when the windows over the region changed since that frame.
func (s *Service) guiZoom(action ProviderDesktopAction) ToolCallResult {
	frame := s.guiSnapshot
	if frame == nil || frame.FrameID == "" {
		// The region means nothing without its frame; the fresh screenshot
		// becomes that frame.
		result := s.gui([]any{map[string]any{"type": "screenshot"}}, 0)
		if !result.IsError {
			result.IsError = true
			SetGUIPublicError(&result, contract.AXCodeGUIFrameStale, "",
				"zoom regions are pixels of the current GUI frame, and there is none. "+
					"Nothing was zoomed; use the attached fresh screenshot.",
				GUIScreenshotCall)
		}
		return result
	}
	if path, message := zoomRegionProblem(action.Region, frame); message != "" {
		return guiError(contract.AXCodeInvalidArgument, path, message, zoomRegionHint(frame))
	}
	response, err := s.callProviderDesktop(ProviderDesktopRequest{
		ProtocolVersion: ProviderProtocolVersion,
		Actions:         []ProviderDesktopAction{action},
		FrameID:         frame.FrameID,
	})
	if err != nil {
		if result, rejected := runtimeRequestRejectionResult(err); rejected {
			SetGUIPublicError(&result, contract.AXCodeInvalidArgument, "", err.Error(), GUIScreenshotCall)
			return result
		}
		return guiError(contract.AXCodeRuntimeError, "", err.Error(), GUIScreenshotCall)
	}
	if !response.OK {
		// A stale frame comes back with a fresh full-desktop screenshot,
		// which guiResponse delivers and registers.
		if response.Snapshot != nil && response.Snapshot.FrameID != "" {
			return s.guiResponse(*response)
		}
		code, path, message := contract.AXCodeRuntimeError, "", FirstNonEmpty(response.Error, "zoom failed")
		failureCode := ""
		if response.Failure != nil {
			failureCode = response.Failure.Code
		}
		switch {
		case len(response.ValidationErrors) > 0:
			code, path, message = contract.AXCodeInvalidArgument, "actions[0].region", guiValidationErrorText(*response)
		case failureCode == "invalid_gui_action" || failureCode == "invalid_zoom_region":
			// macOS and Windows report a region the live desktop rejects
			// with a failure code instead of validation errors.
			code, path = contract.AXCodeInvalidArgument, "actions[0].region"
		case failureCode == contract.GUICodePermissionDenied:
			return guiError(failureCode, "", message, contract.GUIPermissionHint)
		}
		return guiError(code, path, message, GUIScreenshotCall)
	}
	zoom, image := response.GUIResult.Zoom, response.Snapshot
	if zoom == nil || image == nil || len(zoom.Region) != 4 || zoom.Width <= 0 || zoom.Height <= 0 ||
		zoom.Region[2] <= zoom.Region[0] || zoom.Region[3] <= zoom.Region[1] {
		return guiError(contract.AXCodeRuntimeError, "", "the GUI runtime returned no zoom geometry", GUIScreenshotCall)
	}
	if err := validateGUISnapshotImage(image); err != nil {
		return guiError(contract.AXCodeRuntimeError, "", "unusable zoom image: "+err.Error(), GUIScreenshotCall)
	}
	mapping := zoomMapping(*zoom, frame)
	result := ToolCallResult{Content: []ContentItem{
		{Type: "text", Text: mapping.text()},
		{Type: "image", Data: image.ScreenshotPNGBase64, MimeType: image.screenshotMimeType()},
	}}
	result.StructuredContent = map[string]any{"zoom": mapping.evidence()}
	return result
}

// zoomRegionProblem checks a region against the frame it names before any
// capture. The provider repeats the check against the live desktop.
func zoomRegionProblem(region []float64, frame *AppSnapshot) (string, string) {
	if len(region) != 4 {
		return "actions[0].region", "zoom region must be [x0,y0,x1,y1]"
	}
	x0, y0, x1, y1 := region[0], region[1], region[2], region[3]
	if !(0 <= x0 && x0 < x1 && x1 <= float64(frame.Width) &&
		0 <= y0 && y0 < y1 && y1 <= float64(frame.Height)) {
		return "actions[0].region", fmt.Sprintf(
			"zoom region must lie inside the current %dx%d frame with x0 < x1 and y0 < y1; "+
				"nothing was captured", frame.Width, frame.Height)
	}
	if x1-x0 < contract.GUIZoomMinSide || y1-y0 < contract.GUIZoomMinSide {
		return "actions[0].region", fmt.Sprintf(
			"zoom region sides must be at least %d frame pixels, so the mapping stays "+
				"within %g px; nothing was captured", contract.GUIZoomMinSide, guiZoomMaxError)
	}
	return "", ""
}

func zoomRegionHint(frame *AppSnapshot) string {
	return fmt.Sprintf(`{"actions":[{"type":"zoom","region":[0,0,%d,%d]}]}`,
		min(frame.Width, 400), min(frame.Height, 200))
}

// zoomMap is the exact mapping from zoom image pixels to frame pixels:
// x = X0 + u/ScaleX and y = Y0 + v/ScaleY, per axis.
type zoomMap struct {
	FrameID              string
	FrameWidth           int
	FrameHeight          int
	X0, Y0, X1, Y1       float64
	Width, Height        int
	ScaleX, ScaleY       float64
	PrintedX0, PrintedY0 string
	PrintedSX, PrintedSY string
}

func zoomMapping(zoom ProviderGUIZoom, frame *AppSnapshot) zoomMap {
	m := zoomMap{
		FrameID: frame.FrameID, FrameWidth: frame.Width, FrameHeight: frame.Height,
		X0: zoom.Region[0], Y0: zoom.Region[1], X1: zoom.Region[2], Y1: zoom.Region[3],
		Width: zoom.Width, Height: zoom.Height,
	}
	m.ScaleX = float64(zoom.Width) / (m.X1 - m.X0)
	m.ScaleY = float64(zoom.Height) / (m.Y1 - m.Y0)
	m.PrintedX0, m.PrintedY0 = zoomNumber(m.X0), zoomNumber(m.Y0)
	m.PrintedSX, m.PrintedSY = zoomNumber(m.ScaleX), zoomNumber(m.ScaleY)
	return m
}

// zoomNumber prints a mapping value with four decimals: on a 4096 px zoom
// image, the rounding error stays below 0.01 frame pixels.
func zoomNumber(value float64) string {
	return strconv.FormatFloat(math.Round(value*1e4)/1e4, 'f', -1, 64)
}

// frameShort is the short name of a frame id the model sees: frames are
// private provenance, and the model needs only to tell frames apart.
func frameShort(frameID string) string {
	if len(frameID) > 8 {
		return frameID[len(frameID)-8:]
	}
	return frameID
}

func (m zoomMap) text() string {
	return fmt.Sprintf(
		"zoom: region [%s,%s,%s,%s] of the current %dx%d GUI frame (frame %s), shown at %dx%d. "+
			"Zoom pixel (u,v) is frame pixel x = %s + u/%s, y = %s + v/%s. "+
			"Look only: GUI x/y stay frame pixels and are checked against that frame.",
		m.PrintedX0, m.PrintedY0, zoomNumber(m.X1), zoomNumber(m.Y1),
		m.FrameWidth, m.FrameHeight, frameShort(m.FrameID), m.Width, m.Height,
		m.PrintedX0, m.PrintedSX, m.PrintedY0, m.PrintedSY)
}

func (m zoomMap) evidence() map[string]any {
	return map[string]any{
		"frame":   frameShort(m.FrameID),
		"region":  []float64{roundZoom(m.X0), roundZoom(m.Y0), roundZoom(m.X1), roundZoom(m.Y1)},
		"width":   m.Width,
		"height":  m.Height,
		"scale_x": roundZoom(m.ScaleX),
		"scale_y": roundZoom(m.ScaleY),
	}
}

func roundZoom(value float64) float64 { return math.Round(value*1e4) / 1e4 }

// frameFromZoom applies the printed mapping, as a reader of the text would.
func (m zoomMap) frameFromZoom(u, v float64) (float64, float64) {
	parse := func(text string) float64 {
		value, _ := strconv.ParseFloat(text, 64)
		return value
	}
	return parse(m.PrintedX0) + u/parse(m.PrintedSX), parse(m.PrintedY0) + v/parse(m.PrintedSY)
}
