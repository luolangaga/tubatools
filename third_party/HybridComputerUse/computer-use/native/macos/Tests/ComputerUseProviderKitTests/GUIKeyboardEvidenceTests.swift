import CoreGraphics
@testable import ComputerUseProviderKit
import Testing

private let desktopBounds = CGRect(x: 0, y: 0, width: 1920, height: 1080)

@Test
func guiKeypressValidationAcceptsCanonicalModifierChord() throws {
    try validateGUIAction(
        ["type": "keypress", "keys": ["CMD", "A"]],
        desktopBounds: desktopBounds
    )
}

@Test
func guiKeypressValidationRejectsUnsupportedKeyBeforeInput() {
    #expect(throws: Error.self) {
        try validateGUIAction(
            ["type": "keypress", "keys": ["NOT_A_REAL_KEY"]],
            desktopBounds: desktopBounds
        )
    }
}

@Test
func guiMouseModifierFlagsAcceptCanonicalAndAliasNames() throws {
    let flags = try guiMouseModifierFlags([
        "keys": ["SUPER", "SHIFT", "ALT", "CTRL"],
    ])

    #expect(flags.contains(.maskCommand))
    #expect(flags.contains(.maskShift))
    #expect(flags.contains(.maskAlternate))
    #expect(flags.contains(.maskControl))
}

@Test
func guiMouseModifierValidationCoversEveryAdvertisedMouseAction() throws {
    let actions: [[String: Any]] = [
        ["type": "click", "x": 10, "y": 20, "keys": ["SUPER"]],
        ["type": "double_click", "x": 10, "y": 20, "keys": ["SHIFT"]],
        ["type": "move", "x": 10, "y": 20, "keys": ["ALT"]],
        [
            "type": "drag",
            "path": [["x": 10, "y": 20], ["x": 30, "y": 40]],
            "keys": ["CTRL"],
        ],
        [
            "type": "scroll",
            "x": 10,
            "y": 20,
            "scroll_y": 1,
            "keys": ["SUPER"],
        ],
    ]

    for action in actions {
        try validateGUIAction(action, desktopBounds: desktopBounds)
    }
}

@Test
func guiMouseModifierValidationRejectsUnsupportedKeyBeforeInput() {
    #expect(throws: Error.self) {
        try validateGUIAction(
            [
                "type": "click",
                "x": 10,
                "y": 20,
                "keys": ["NOT_A_MODIFIER"],
            ],
            desktopBounds: desktopBounds
        )
    }
}

@Test
func guiTypeValidationRequiresText() {
    #expect(throws: Error.self) {
        try validateGUIAction(
            ["type": "type"],
            desktopBounds: desktopBounds
        )
    }
}

@Test
func guiExecutionFailurePreservesPriorEmissionFacts() {
    let result = guiExecutionFailure(
        error: "failed",
        text: "stopped",
        code: "gui_action_failed",
        phase: "execution",
        failedActionIndex: 1,
        inputActionsDispatched: 1,
        inputEventsEmitted: true,
        partialInputPossible: true,
        mutationUnknown: true,
        snapshot: nil
    )
    let gui = result["guiResult"] as? [String: Any]

    #expect(result["input_events_emitted"] as? Bool == true)
    #expect(result["partial_input_possible"] as? Bool == true)
    #expect(result["mutation_unknown"] as? Bool == true)
    #expect(gui?["input_actions_dispatched"] as? Int == 1)
    #expect(gui?["mutation_unknown"] as? Bool == true)
}

@Test
func guiAddsProcessingBarrierFromPointerActivationToKeyboardInput() {
    #expect(
        guiNeedsProcessingBarrier(
            previousActionType: "click",
            currentActionType: "type"
        )
    )
    #expect(
        guiNeedsProcessingBarrier(
            previousActionType: "double_click",
            currentActionType: "keypress"
        )
    )
    #expect(
        !guiNeedsProcessingBarrier(
            previousActionType: "type",
            currentActionType: "keypress"
        )
    )
}
