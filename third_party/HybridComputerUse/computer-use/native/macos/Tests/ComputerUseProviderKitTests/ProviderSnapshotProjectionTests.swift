import AppKit
import ApplicationServices
@testable import ComputerUseProviderKit
import Testing

@Test
func providerProjectionIgnoresDuplicateIndexesFromDisplayOnlyRowText() throws {
    let root = AXUIElementCreateSystemWide()
    let app = RunningAppDescriptor(
        name: "Projection Test",
        bundleIdentifier: "org.computer-use.projection-test",
        pid: getpid(),
        runningApplication: NSRunningApplication.current
    )
    let snapshot = AppSnapshot(
        app: app,
        windowTitle: "Projection Test",
        windowBounds: nil,
        rootWindowElement: root,
        targetWindowID: nil,
        targetWindowHandle: nil,
        targetWindowLayer: nil,
        treeLines: [
            "0 window Projection Test",
            "\t1 row sample.dat",
            "1 KB",
        ],
        focusedSummary: nil,
        focusedElement: nil,
        selectedText: nil,
        elements: [
            0: ElementRecord(
                index: 0,
                identifier: nil,
                element: root,
                localFrame: nil,
                rawActions: [],
                prettyActions: [],
                providerRole: "window",
                localizedRole: "window",
                name: "Projection Test"
            ),
            1: ElementRecord(
                index: 1,
                runtimeID: [0, 1],
                identifier: "sample-row",
                element: root,
                localFrame: nil,
                rawActions: [],
                prettyActions: [],
                providerRole: "listitem",
                localizedRole: "row",
                name: "sample.dat",
                setValueSupported: false
            ),
        ]
    )

    try bindMacProviderObjectIdentities(Array(snapshot.elements.values), scope: "projection")
    let projected = try snapshot.providerElementDictionaries
    #expect(projected.count == 2)
    #expect(projected[1]["name"] as? String == "sample.dat")
    #expect(projected[1]["controlType"] as? String == "listitem")
    #expect(projected[1]["setValue"] as? Bool == false)
    #expect(projected[1]["runtimeId"] as? [Int] == [0, 1])
    #expect(projected[1]["automationId"] as? String == "sample-row")
}

@Test
func providerProjectionPublishesExplicitSetValueSupport() throws {
    let root = AXUIElementCreateSystemWide()
    let app = RunningAppDescriptor(
        name: "Projection Test",
        bundleIdentifier: "org.computer-use.projection-test",
        pid: getpid(),
        runningApplication: NSRunningApplication.current
    )
    let snapshot = AppSnapshot(
        app: app,
        windowTitle: "Projection Test",
        windowBounds: nil,
        rootWindowElement: root,
        targetWindowID: nil,
        targetWindowHandle: nil,
        targetWindowLayer: nil,
        treeLines: ["0 combobox Sound"],
        focusedSummary: nil,
        focusedElement: nil,
        selectedText: nil,
        elements: [
            0: ElementRecord(
                index: 0,
                identifier: nil,
                element: root,
                localFrame: nil,
                rawActions: [],
                prettyActions: [],
                providerRole: "combobox",
                localizedRole: "pop up button",
                name: "Sound",
                setValueSupported: true
            ),
        ]
    )

    try bindMacProviderObjectIdentities(Array(snapshot.elements.values), scope: "projection")
    let projected = try snapshot.providerElementDictionaries
    #expect(projected.count == 1)
    #expect(projected[0]["setValue"] as? Bool == true)
}

@Test
func providerProjectionDoesNotPromoteRenderedValueIntoIdentityName() throws {
    let root = AXUIElementCreateSystemWide()
    let app = RunningAppDescriptor(
        name: "Projection Test",
        bundleIdentifier: "org.computer-use.projection-test",
        pid: getpid(),
        runningApplication: NSRunningApplication.current
    )
    let snapshot = AppSnapshot(
        app: app,
        windowTitle: "Projection Test",
        windowBounds: nil,
        rootWindowElement: root,
        targetWindowID: nil,
        targetWindowHandle: nil,
        targetWindowLayer: nil,
        treeLines: ["0 switch Value: off"],
        focusedSummary: nil,
        focusedElement: nil,
        selectedText: nil,
        elements: [
            0: ElementRecord(
                index: 0,
                identifier: nil,
                element: root,
                localFrame: nil,
                rawActions: [],
                prettyActions: [],
                providerRole: "checkbox",
                localizedRole: "switch",
                name: nil,
                description: "Wallpaper tint",
                placeholder: "Optional label",
                value: "off",
                setValueSupported: false
            ),
        ]
    )

    try bindMacProviderObjectIdentities(Array(snapshot.elements.values), scope: "projection")
    let projected = try snapshot.providerElementDictionaries
    #expect(projected[0]["name"] as? String == "")
    #expect(projected[0]["description"] as? String == "Wallpaper tint")
    #expect(projected[0]["placeholder"] as? String == "Optional label")
    #expect(projected[0]["value"] as? String == "off")
}

@Test
func providerProjectionPreservesMultilineSemanticValue() throws {
    let root = AXUIElementCreateSystemWide()
    let app = RunningAppDescriptor(
        name: "Projection Test",
        bundleIdentifier: "org.computer-use.projection-test",
        pid: getpid(),
        runningApplication: NSRunningApplication.current
    )
    let semanticText = "homework_1\nMath exercises:\nSubmit by Friday"
    let snapshot = AppSnapshot(
        app: app,
        windowTitle: "Projection Test",
        windowBounds: nil,
        rootWindowElement: root,
        targetWindowID: nil,
        targetWindowHandle: nil,
        targetWindowLayer: nil,
        treeLines: [
            "0 textbox homework_1\\nMath exercises:\\nSubmit by Friday",
        ],
        focusedSummary: nil,
        focusedElement: nil,
        selectedText: nil,
        elements: [
            0: ElementRecord(
                index: 0,
                identifier: nil,
                element: root,
                localFrame: nil,
                rawActions: [],
                prettyActions: [],
                providerRole: "textbox",
                localizedRole: "entry area",
                name: "homework_1\\nMath exercises:\\nSubmit by Friday",
                value: semanticText,
                setValueSupported: true
            ),
        ]
    )

    try bindMacProviderObjectIdentities(Array(snapshot.elements.values), scope: "projection")
    let projected = try snapshot.providerElementDictionaries
    #expect(projected[0]["value"] as? String == semanticText)
    #expect(snapshot.treeLines[0].contains("\\n"))
    #expect(!snapshot.treeLines[0].contains("\n"))
}

@Test
func boundedSemanticTextPreservesAndNormalizesLineBreaks() {
    let raw = "  homework_1\r\nMath exercises:\rSubmit by Friday  "

    #expect(
        boundedSemanticText(raw)
            == "homework_1\nMath exercises:\nSubmit by Friday"
    )
    #expect(
        sanitizeText(boundedSemanticText(raw))
            == "homework_1\\nMath exercises:\\nSubmit by Friday"
    )
}

@Test
func accessibilityFramesRejectNonFiniteProviderGeometry() {
    let valid = CGRect(x: 10, y: 20, width: 300, height: 200)
    #expect(finiteAccessibilityFrame(valid) == valid)

    let invalidFrames = [
        CGRect(x: CGFloat.nan, y: 20, width: 300, height: 200),
        CGRect(x: 10, y: CGFloat.infinity, width: 300, height: 200),
        CGRect(x: 10, y: 20, width: -CGFloat.infinity, height: 200),
        CGRect(x: 10, y: 20, width: 300, height: CGFloat.nan),
    ]
    for frame in invalidFrames {
        #expect(finiteAccessibilityFrame(frame) == nil)
    }
}
