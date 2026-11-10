@testable import ComputerUseProviderKit
import Carbon.HIToolbox
import CoreGraphics
import Testing

@Test(arguments: [
    (",", CGKeyCode(kVK_ANSI_Comma)),
    ("comma", CGKeyCode(kVK_ANSI_Comma)),
    (".", CGKeyCode(kVK_ANSI_Period)),
    ("period", CGKeyCode(kVK_ANSI_Period)),
    ("/", CGKeyCode(kVK_ANSI_Slash)),
    ("semicolon", CGKeyCode(kVK_ANSI_Semicolon)),
    ("apostrophe", CGKeyCode(kVK_ANSI_Quote)),
    ("left_bracket", CGKeyCode(kVK_ANSI_LeftBracket)),
    ("right_bracket", CGKeyCode(kVK_ANSI_RightBracket)),
    ("backslash", CGKeyCode(kVK_ANSI_Backslash)),
    ("minus", CGKeyCode(kVK_ANSI_Minus)),
    ("equal", CGKeyCode(kVK_ANSI_Equal)),
    ("backtick", CGKeyCode(kVK_ANSI_Grave)),
])
func keypressParserAcceptsOrdinaryANSIPunctuation(
    key: String,
    expectedCode: CGKeyCode
) throws {
    let parsed = try KeyPressParser.parse("SUPER+\(key)")

    #expect(parsed.keyCode == expectedCode)
    #expect(parsed.modifiers.count == 1)
    #expect(parsed.modifiers[0].flag == .maskCommand)
}

@Test
func keypressParserRejectsUnknownKey() {
    #expect(throws: ComputerUseError.self) {
        try KeyPressParser.parse("SUPER+NOT_A_REAL_KEY")
    }
}
