//go:build linux

package transport

import (
	"net"
	"os"
	"syscall"
)

// peerIsSameUser checks SO_PEERCRED: only processes of the daemon's own user
// may connect. The socket mode already restricts this; the check also
// covers a socket path that was moved or shared.
func peerIsSameUser(conn net.Conn) bool {
	unix, ok := conn.(*net.UnixConn)
	if !ok {
		return false
	}
	raw, err := unix.SyscallConn()
	if err != nil {
		return false
	}
	same := false
	_ = raw.Control(func(fd uintptr) {
		cred, credErr := syscall.GetsockoptUcred(int(fd), syscall.SOL_SOCKET, syscall.SO_PEERCRED)
		same = credErr == nil && int(cred.Uid) == os.Getuid()
	})
	return same
}
