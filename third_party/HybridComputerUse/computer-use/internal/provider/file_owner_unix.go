//go:build linux || darwin

package provider

import (
	"os"
	"syscall"
)

func FileOwnedByUID(info os.FileInfo, uid int) bool {
	stat, ok := info.Sys().(*syscall.Stat_t)
	return ok && int(stat.Uid) == uid
}
