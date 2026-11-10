package core

import (
	"fmt"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// attachDesktopFrame gives an AX read the same image a GUI screenshot
// returns: the full desktop, captured through the desktop backend at the
// size the model sees, with its frame id and layout provenance. The image
// replaces any window image the provider sent. callTool registers the frame
// as the GUI coordinate frame when the image is delivered, so GUI input can
// follow the AX read without another screenshot, and the provider's
// point-based stale check guards it like any GUI screenshot.
//
// The capture follows the tree read; a failure leaves the snapshot without
// an image and returns a note, as a provider capture failure did.
func (s *Service) attachDesktopFrame(snapshot *AppSnapshot) []string {
	if snapshot == nil {
		return nil
	}
	snapshot.ScreenshotPNGBase64 = ""
	snapshot.ScreenshotMimeType = ""
	snapshot.ScreenshotEncoding = ""
	snapshot.desktopFrame = nil
	frame, err := s.captureDesktopFrame()
	if err != nil {
		return []string{"[image] full-desktop capture failed: " + err.Error()}
	}
	snapshot.ScreenshotPNGBase64 = frame.ScreenshotPNGBase64
	snapshot.ScreenshotMimeType = frame.ScreenshotMimeType
	snapshot.ScreenshotEncoding = frame.ScreenshotEncoding
	snapshot.desktopFrame = frame
	return nil
}

// captureDesktopFrame runs the GUI observation batch (one screenshot action,
// which emits no input) and accepts its image under the rules guiResponse
// applies before a frame may become coordinate provenance.
func (s *Service) captureDesktopFrame() (*AppSnapshot, error) {
	response, err := s.callProviderDesktop(ProviderDesktopRequest{
		ProtocolVersion: ProviderProtocolVersion,
		Actions:         []ProviderDesktopAction{{Type: "screenshot"}},
	})
	if err != nil {
		return nil, err
	}
	frame := response.Snapshot
	switch {
	case !response.OK:
		return nil, fmt.Errorf("%s", FirstNonEmpty(response.Error, "the desktop runtime reported a failure"))
	case frame == nil || frame.ScreenshotPNGBase64 == "":
		return nil, fmt.Errorf("the desktop runtime returned no screenshot")
	case frame.CoordinateSpace == "" || frame.Width <= 0 || frame.Height <= 0:
		return nil, fmt.Errorf("the desktop screenshot has no coordinate_space/width/height")
	}
	if err := validateGUISnapshotImage(frame); err != nil {
		return nil, err
	}
	return frame, nil
}

// deliverAXImages applies the AX image policy at the shared output boundary:
// images the caller did not request are removed, and the remaining desktop
// frames are registered. An error result keeps and registers its image too:
// it is a fresh desktop capture, and the point rule still guards input
// planned from it.
func (s *Service) deliverAXImages(name string, args map[string]any, result ToolCallResult) ToolCallResult {
	result = enforceAXImageOutputPolicy(name, args, result)
	if name != contract.PublicToolAX {
		return result
	}
	return s.registerDeliveredDesktopFrames(result)
}

// registerDeliveredDesktopFrames makes the last delivered AX desktop image
// the GUI coordinate frame and says so before each image. It runs on the
// final AX result, after images the caller did not request are removed, so
// only an image the caller receives becomes provenance.
func (s *Service) registerDeliveredDesktopFrames(result ToolCallResult) ToolCallResult {
	var latest *AppSnapshot
	content := make([]ContentItem, 0, len(result.Content)+1)
	for _, item := range result.Content {
		if item.Type == "image" && item.frame != nil {
			latest = item.frame
			content = append(content, ContentItem{Type: "text", Text: desktopFrameLine(item.frame)})
		}
		content = append(content, item)
	}
	if latest == nil {
		return result
	}
	s.guiSnapshot = latest
	result.Content = content
	return result
}

func desktopFrameLine(frame *AppSnapshot) string {
	line := fmt.Sprintf("image: full desktop %dx%d", frame.Width, frame.Height)
	if !contract.ToolIsEnabled(contract.PublicToolGUI) {
		return line + "."
	}
	return line + ", the current GUI frame; GUI x/y may use its pixels."
}
