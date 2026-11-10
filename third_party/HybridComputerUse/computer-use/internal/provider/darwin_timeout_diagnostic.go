package provider

func darwinTimeoutWindowForPID(
	pid int,
	processState string,
) (known, alive bool) {
	if pid <= 0 {
		return true, false
	}
	switch processState {
	case "missing", "zombie", "exited":
		return true, false
	default:
		// A timeout diagnostic must not re-enter the same native provider
		// whose failure triggered the diagnostic. Keep window state unknown.
		return false, false
	}
}
