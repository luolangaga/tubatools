import Foundation

func computerUseNoWindowFoundMessage(appName: String) -> String {
    "\(appName) has no window that accessibility can read. Observation does not unhide, activate or open applications; show one of its windows with a GUI action, then observe again."
}

public enum ComputerUseError: Error, LocalizedError {
    case message(String)
    case unsupportedTool(String)
    case invalidArguments(String)
    case capabilityUnsupported(String)
    case appNotFound(String)
    case permissionDenied(String)
    case staleAppRef(String)
    case staleWindowRef(String)
    case staleElementRef(String)
    case stateUnavailable(String)
    case providerResetRequired(String)

    public var errorDescription: String? {
        switch self {
        case .message(let value):
            return value
        case .unsupportedTool(let name):
            return "unsupportedTool(\"\(name)\")"
        case .invalidArguments(let message):
            return "invalidArguments(\"\(message)\")"
        case .capabilityUnsupported(let message):
            return message
        case .appNotFound(let app):
            return "appNotFound(\"\(app)\"): no running application matched this query. The provider does not launch applications; start it first, for example from the Dock, Finder or Spotlight, then observe again."
        case .permissionDenied(let message):
            return message
        case .staleAppRef(let message), .staleWindowRef(let message):
            return message
        case .staleElementRef(let message):
            return message
        case .stateUnavailable(let message):
            return message
        case .providerResetRequired(let message):
            return message
        }
    }
}

extension ComputerUseError {
    var staleIdentityFailure: (code: String, subject: String, reason: String)? {
        switch self {
        case .staleAppRef:
            return ("stale_app_ref", "app", "provider_process_changed")
        case .staleWindowRef:
            return ("stale_window_ref", "window", "provider_window_changed")
        default:
            return nil
        }
    }

    static func missingArgument(_ name: String) -> ComputerUseError {
        .message("Missing required argument: \(name)")
    }
}

func requiresProviderReset(_ error: Error) -> Bool {
    guard let computerError = error as? ComputerUseError else {
        return false
    }
    if case .providerResetRequired = computerError {
        return true
    }
    return false
}
