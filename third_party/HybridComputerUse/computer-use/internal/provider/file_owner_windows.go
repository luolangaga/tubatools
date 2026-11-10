//go:build windows

package provider

import "os"

func FileOwnedByUID(_ os.FileInfo, _ int) bool {
	return false
}
