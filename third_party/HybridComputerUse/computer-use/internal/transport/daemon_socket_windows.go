//go:build windows

package transport

import (
	"errors"
	"fmt"
	"net"
	"os"
	"path/filepath"
	"strings"
	"syscall"
	"time"
	"unsafe"
)

var (
	daemonAdvapi          = syscall.NewLazyDLL("advapi32.dll")
	daemonGetSecurityInfo = daemonAdvapi.NewProc("GetSecurityInfo")
	daemonConvertSD       = daemonAdvapi.NewProc("ConvertStringSecurityDescriptorToSecurityDescriptorW")
	daemonGetAce          = daemonAdvapi.NewProc("GetAce")
	daemonIsValidACL      = daemonAdvapi.NewProc("IsValidAcl")
	daemonGetFileTag      = syscall.NewLazyDLL("kernel32.dll").NewProc("GetFileInformationByHandleEx")
)

const (
	daemonReadAttributes = 0x00000080
	daemonReadControl    = 0x00020000
	daemonFileAllAccess  = 0x001f01ff
	daemonSocketTag      = 0x80000023 // IO_REPARSE_TAG_AF_UNIX 是 socket 本身，并不重定向路径。
)

// defaultDaemonSocket 与 Python 使用同一 Windows 临时目录和固定桌面名称。
func defaultDaemonSocket() string {
	if path := strings.TrimSpace(os.Getenv("HCU_SOCKET")); path != "" {
		return path
	}
	return windowsDaemonSocket(os.TempDir(), daemonShortPath)
}

func windowsDaemonSocket(temp string, shortPath func(string) (string, error)) string {
	path := filepath.Join(temp, "hcu--1", "desktop-default.sock")
	// sun_path 的 108 字节包含末尾 NUL，Go 使用 UTF-8 字节。
	if len(path) >= 108 {
		if short, err := shortPath(temp); err == nil {
			candidate := filepath.Join(short, "hcu--1", "desktop-default.sock")
			if len(candidate) < len(path) {
				path = candidate
			}
		}
	}
	return path
}

func daemonShortPath(path string) (string, error) {
	input, err := syscall.UTF16PtrFromString(path)
	if err != nil {
		return "", err
	}
	buffer := make([]uint16, 32768)
	n, err := syscall.GetShortPathName(input, &buffer[0], uint32(len(buffer)))
	if err != nil {
		return "", err
	}
	if n == 0 || n >= uint32(len(buffer)) {
		return "", fmt.Errorf("GetShortPathNameW returned invalid length %d", n)
	}
	return syscall.UTF16ToString(buffer[:n]), nil
}

func daemonUserSID() (string, error) {
	token, err := syscall.OpenCurrentProcessToken()
	if err != nil {
		return "", err
	}
	defer token.Close()
	user, err := token.GetTokenUser()
	if err != nil {
		return "", err
	}
	return user.User.Sid.String()
}

// prepareDaemonDirectory 也供启动器使用，必须先于打开 lock 和 log。
// 不修改已有目录的 ACL；提升进程创建目录时也显式指定当前用户为 owner。
func prepareDaemonDirectory(socket string) error {
	if socket == "" || len(socket) >= 108 || strings.IndexByte(socket, 0) >= 0 {
		return fmt.Errorf("Windows AF_UNIX socket path must contain 1 to 107 UTF-8 bytes; use a shorter private --socket path")
	}
	absolute, err := filepath.Abs(socket)
	if err != nil {
		return err
	}
	volume := filepath.VolumeName(absolute)
	if len(volume) != 2 || volume[1] != ':' || strings.Contains(absolute[len(volume):], ":") {
		return fmt.Errorf("Windows daemon socket requires a local drive path without alternate data streams")
	}
	user, err := daemonUserSID()
	if err != nil {
		return err
	}
	dir := filepath.Dir(absolute)
	if err := daemonMakeDirectory(dir, user); err != nil {
		return err
	}
	if err := daemonCheckFile(dir, user, "directory"); err != nil {
		return err
	}
	for _, suffix := range []string{".lock", ".log", ".refs"} {
		if err := daemonCheckFile(absolute+suffix, user, "file"); err != nil && !errors.Is(err, os.ErrNotExist) {
			return err
		}
	}
	return nil
}

// daemonCheckAncestors 拒绝 junction、symlink 和其他重解析目录，包括祖先。
func daemonCheckAncestors(path string) error {
	parent := filepath.Dir(path)
	if parent != path {
		if err := daemonCheckAncestors(parent); err != nil {
			return err
		}
	}
	name, err := syscall.UTF16PtrFromString(path)
	if err != nil {
		return err
	}
	attributes, err := syscall.GetFileAttributes(name)
	if err != nil {
		return err
	}
	if attributes&syscall.FILE_ATTRIBUTE_REPARSE_POINT != 0 || attributes&syscall.FILE_ATTRIBUTE_DIRECTORY == 0 {
		return fmt.Errorf("daemon directory %s is not a plain directory", path)
	}
	return nil
}

func daemonMakeDirectory(path, user string) error {
	if err := daemonCheckAncestors(path); err == nil {
		return nil
	} else if !errors.Is(err, os.ErrNotExist) {
		return err
	}
	parent := filepath.Dir(path)
	if parent == path {
		return fmt.Errorf("daemon directory root does not exist: %s", path)
	}
	if err := daemonMakeDirectory(parent, user); err != nil {
		return err
	}
	// OICI 将权限传给 socket 及旁文件；P 禁止从 Temp 继承其他用户的权限。
	sddl, err := syscall.UTF16PtrFromString("O:" + user + "D:P(A;OICI;FA;;;" + user + ")(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)")
	if err != nil {
		return err
	}
	var descriptor unsafe.Pointer
	ok, _, callErr := daemonConvertSD.Call(uintptr(unsafe.Pointer(sddl)), 1, uintptr(unsafe.Pointer(&descriptor)), 0)
	if ok == 0 {
		return fmt.Errorf("create daemon security descriptor: %w", callErr)
	}
	defer syscall.LocalFree(syscall.Handle(uintptr(descriptor)))
	attributes := syscall.SecurityAttributes{Length: uint32(unsafe.Sizeof(syscall.SecurityAttributes{})), SecurityDescriptor: uintptr(descriptor)}
	name, err := syscall.UTF16PtrFromString(path)
	if err != nil {
		return err
	}
	if err := syscall.CreateDirectory(name, &attributes); err != nil && !errors.Is(err, syscall.ERROR_ALREADY_EXISTS) {
		return err
	}
	return daemonCheckFile(path, user, "directory")
}

// daemonCheckFile 用不跟随 reparse 的句柄检查类型、owner 和 DACL。
// 旁文件和 socket 可保留提升进程生成的 Administrators owner，目录必须属于用户。
func daemonCheckFile(path, user, kind string) error {
	name, err := syscall.UTF16PtrFromString(path)
	if err != nil {
		return err
	}
	handle, err := syscall.CreateFile(name, daemonReadControl|daemonReadAttributes,
		syscall.FILE_SHARE_READ|syscall.FILE_SHARE_WRITE|syscall.FILE_SHARE_DELETE, nil, syscall.OPEN_EXISTING,
		syscall.FILE_FLAG_BACKUP_SEMANTICS|syscall.FILE_FLAG_OPEN_REPARSE_POINT, 0)
	if err != nil {
		return err
	}
	defer syscall.CloseHandle(handle)
	var info syscall.ByHandleFileInformation
	if err := syscall.GetFileInformationByHandle(handle, &info); err != nil {
		return err
	}
	var tag struct{ Attributes, Tag uint32 }
	ok, _, callErr := daemonGetFileTag.Call(uintptr(handle), 9, uintptr(unsafe.Pointer(&tag)), unsafe.Sizeof(tag))
	if ok == 0 {
		return fmt.Errorf("inspect daemon path %s: %w", path, callErr)
	}
	switch kind {
	case "directory":
		if tag.Attributes&syscall.FILE_ATTRIBUTE_DIRECTORY == 0 || tag.Attributes&syscall.FILE_ATTRIBUTE_REPARSE_POINT != 0 {
			return fmt.Errorf("daemon path %s is not a plain directory", path)
		}
	case "socket":
		if tag.Attributes&syscall.FILE_ATTRIBUTE_DIRECTORY != 0 || tag.Attributes&syscall.FILE_ATTRIBUTE_REPARSE_POINT == 0 || tag.Tag != daemonSocketTag || info.NumberOfLinks != 1 {
			return fmt.Errorf("%s exists and is not a single-link AF_UNIX socket", path)
		}
	case "file":
		if tag.Attributes&(syscall.FILE_ATTRIBUTE_DIRECTORY|syscall.FILE_ATTRIBUTE_REPARSE_POINT) != 0 || info.NumberOfLinks != 1 {
			return fmt.Errorf("daemon side file %s is not a plain single-link file", path)
		}
	}
	var owner *syscall.SID
	var acl, descriptor unsafe.Pointer
	status, _, _ := daemonGetSecurityInfo.Call(uintptr(handle), 1, 1|4,
		uintptr(unsafe.Pointer(&owner)), 0, uintptr(unsafe.Pointer(&acl)), 0, uintptr(unsafe.Pointer(&descriptor)))
	if status != 0 {
		return fmt.Errorf("read daemon path security %s: %w", path, syscall.Errno(status))
	}
	defer syscall.LocalFree(syscall.Handle(uintptr(descriptor)))
	if kind == "directory" {
		var control uint16
		var revision uint32
		ok, _, callErr := daemonAdvapi.NewProc("GetSecurityDescriptorControl").Call(uintptr(descriptor), uintptr(unsafe.Pointer(&control)), uintptr(unsafe.Pointer(&revision)))
		if ok == 0 {
			return fmt.Errorf("read directory DACL protection: %w", callErr)
		}
		if control&0x1000 == 0 { // SE_DACL_PROTECTED 防止父目录后续扩大继承权限。
			return fmt.Errorf("daemon directory %s must have a protected DACL", path)
		}
	}
	if owner == nil {
		return fmt.Errorf("daemon path %s has no owner", path)
	}
	ownerSID, err := owner.String()
	if err != nil {
		return err
	}
	if ownerSID != user && !(kind != "directory" && ownerSID == "S-1-5-32-544") {
		return fmt.Errorf("daemon path %s is owned by another user (%s)", path, ownerSID)
	}
	if err := daemonCheckDACL(acl, user, kind == "directory"); err != nil {
		return fmt.Errorf("unsafe daemon path %s: %w", path, err)
	}
	return nil
}

func daemonCheckDACL(acl unsafe.Pointer, user string, directory bool) error {
	if acl == nil {
		return fmt.Errorf("missing DACL")
	}
	valid, _, _ := daemonIsValidACL.Call(uintptr(acl))
	if valid == 0 {
		return fmt.Errorf("invalid DACL")
	}
	header := (*struct {
		Revision, Reserved     byte
		Size, Count, Reserved2 uint16
	})(acl)
	userAccess, childAccess := uint32(0), uint32(0)
	for index := uint32(0); index < uint32(header.Count); index++ {
		var ace unsafe.Pointer
		ok, _, callErr := daemonGetAce.Call(uintptr(acl), uintptr(index), uintptr(unsafe.Pointer(&ace)))
		if ok == 0 {
			return fmt.Errorf("read DACL entry: %w", callErr)
		}
		entry := (*struct {
			Type, Flags byte
			Size        uint16
			Mask        uint32
		})(ace)
		if entry.Type == 1 { // 拒绝 ACE 只会收紧权限。
			continue
		}
		if entry.Type != 0 || entry.Size < 16 {
			return fmt.Errorf("unsupported DACL entry type %d", entry.Type)
		}
		sid, err := (*syscall.SID)(unsafe.Add(ace, 8)).String()
		if err != nil {
			return err
		}
		if sid != user && sid != "S-1-5-18" && sid != "S-1-5-32-544" {
			return fmt.Errorf("DACL grants access to %s", sid)
		}
		if sid == user && entry.Flags&0x08 == 0 { // INHERIT_ONLY 不授权当前对象。
			userAccess |= entry.Mask
		}
		if sid == user && entry.Flags&0x01 != 0 { // OBJECT_INHERIT 为新 socket 和旁文件授权。
			childAccess |= entry.Mask
		}
	}
	if userAccess&daemonFileAllAccess != daemonFileAllAccess && userAccess&0x10000000 == 0 {
		return fmt.Errorf("DACL does not grant the current user full control")
	}
	if directory && childAccess&daemonFileAllAccess != daemonFileAllAccess && childAccess&0x10000000 == 0 {
		return fmt.Errorf("directory DACL must pass current-user full control to new files")
	}
	return nil
}

func prepareSocketPath(path string) error {
	if err := prepareDaemonDirectory(path); err != nil {
		return err
	}
	user, err := daemonUserSID()
	if err != nil {
		return err
	}
	if err := daemonCheckFile(path, user, "socket"); err != nil {
		if errors.Is(err, os.ErrNotExist) {
			return nil
		}
		return err
	}
	conn, err := net.DialTimeout("unix", path, 500*time.Millisecond)
	if err == nil {
		conn.Close()
		return fmt.Errorf("a daemon is already serving %s", path)
	}
	// 只有明确拒绝连接才证明 socket 已失效；超时和访问拒绝都不允许删除。
	if !errors.Is(err, syscall.Errno(10061)) {
		return fmt.Errorf("cannot prove socket %s is stale; refusing to remove it: %w", path, err)
	}
	if err := daemonCheckFile(path, user, "socket"); err != nil {
		return err
	}
	return os.Remove(path)
}

// socket 继承私有目录的 DACL，不用无效的 POSIX chmod 模拟 Windows 权限。
func secureDaemonSocket(path string) error {
	user, err := daemonUserSID()
	if err != nil {
		return err
	}
	return daemonCheckFile(path, user, "socket")
}
