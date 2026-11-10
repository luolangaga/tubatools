//go:build windows

package transport

import (
	"bufio"
	"errors"
	"fmt"
	"net"
	"os"
	"os/exec"
	"strings"
	"syscall"
	"time"
	"unsafe"
)

const daemonStartTimeout = 15 * time.Second

var daemonLockFileEx = syscall.NewLazyDLL("kernel32.dll").NewProc("LockFileEx")

// detachProcess 不连接父进程控制台，也不继承其标准输入。
// Windows 普通子进程不会随父进程退出；此处不尝试逃离宿主的 Job Object。
func detachProcess(command *exec.Cmd) {
	const createNoWindow = 0x08000000
	command.SysProcAttr = &syscall.SysProcAttr{
		HideWindow:    true,
		CreationFlags: createNoWindow | syscall.CREATE_NEW_PROCESS_GROUP,
	}
	command.Stdin = nil
}

// lockFile 锁住同一 endpoint 的第一个字节；文件不能删除，否则会产生两把锁。
// 非阻塞 LockFileEx 让锁等待和 socket 就绪共用一个截止时间。
func lockFile(path string, deadline time.Time) (func(), error) {
	file, err := os.OpenFile(path, os.O_CREATE|os.O_RDWR, 0o600)
	if err != nil {
		return nil, fmt.Errorf("open daemon lock %s: %w", path, err)
	}
	const lockExclusiveFailImmediately = 0x00000002 | 0x00000001
	const errorLockViolation syscall.Errno = 33
	var overlapped syscall.Overlapped
	for time.Now().Before(deadline) {
		result, _, callErr := daemonLockFileEx.Call(file.Fd(), lockExclusiveFailImmediately,
			0, 1, 0, uintptr(unsafe.Pointer(&overlapped)))
		if result != 0 {
			// 关闭句柄会释放字节锁；os.OpenFile 的句柄不会传给 daemon。
			return func() { _ = file.Close() }, nil
		}
		if !errors.Is(callErr, errorLockViolation) {
			file.Close()
			return nil, fmt.Errorf("lock daemon endpoint %s: %w", path, callErr)
		}
		time.Sleep(min(20*time.Millisecond, max(0, time.Until(deadline))))
	}
	file.Close()
	return nil, fmt.Errorf("waiting for daemon lock %s: %w", path, os.ErrDeadlineExceeded)
}

// daemonServeCommand 使用参数列表，交给 Go 保留中文、空格和引号。
var daemonServeCommand = func(socket string) (*exec.Cmd, error) {
	executable, err := os.Executable()
	if err != nil {
		return nil, err
	}
	command := exec.Command(executable, "serve", "--socket", socket)
	if idle := strings.TrimSpace(os.Getenv("HCU_IDLE_TIMEOUT")); idle != "" {
		command.Args = append(command.Args, "--idle-timeout", idle)
	}
	return command, nil
}

func startDaemon(socket string) (*daemonClient, error) {
	return startDaemonBefore(socket, time.Now().Add(daemonStartTimeout))
}

// startDaemonBefore 只重试连接，不发送或重发任何业务请求。
// 截止时间从入口计算，等待别的启动者不能另获一轮 15 秒预算。
func startDaemonBefore(socket string, deadline time.Time) (*daemonClient, error) {
	if !time.Now().Before(deadline) {
		return nil, fmt.Errorf("starting daemon %s: %w", socket, os.ErrDeadlineExceeded)
	}
	if err := prepareDaemonDirectory(socket); err != nil {
		return nil, err
	}
	unlock, err := lockFile(socket+".lock", deadline)
	if err != nil {
		return nil, err
	}
	defer unlock()
	if client, err := dialDaemonBefore(socket, deadline); err == nil {
		return client, nil
	}
	if !time.Now().Before(deadline) {
		return nil, fmt.Errorf("waiting for daemon %s: %w", socket, os.ErrDeadlineExceeded)
	}
	command, err := daemonServeCommand(socket)
	if err != nil {
		return nil, fmt.Errorf("prepare daemon command: %w", err)
	}
	logPath := socket + ".log"
	logFile, err := os.OpenFile(logPath, os.O_CREATE|os.O_WRONLY|os.O_APPEND, 0o600)
	if err != nil {
		return nil, fmt.Errorf("open daemon log %s: %w", logPath, err)
	}
	defer logFile.Close()
	// 使用文件而非父进程管道，父进程退出不会关闭 daemon 的输出。
	command.Stdout = logFile
	command.Stderr = logFile
	detachProcess(command)
	if !time.Now().Before(deadline) {
		return nil, fmt.Errorf("starting daemon %s: %w", socket, os.ErrDeadlineExceeded)
	}
	if err := command.Start(); err != nil {
		return nil, fmt.Errorf("start daemon: %w; see %s", err, logPath)
	}
	exited := make(chan error, 1)
	go func() { exited <- command.Wait() }()
	exitError := func(err error) error {
		if err == nil {
			err = errors.New("exit status 0")
		}
		return fmt.Errorf("daemon exited during start: %w; see %s", err, logPath)
	}
	for time.Now().Before(deadline) {
		select {
		case err := <-exited:
			return nil, exitError(err)
		default:
		}
		if client, err := dialDaemonBefore(socket, deadline); err == nil {
			select {
			case err := <-exited:
				client.close()
				return nil, exitError(err)
			default:
				return client, nil
			}
		}
		select {
		case err := <-exited:
			return nil, exitError(err)
		case <-time.After(min(20*time.Millisecond, max(0, time.Until(deadline)))):
		}
	}
	// 超时的启动者不能稍后抢占 endpoint；Wait 协程回收句柄，不阻塞预算。
	killErr := command.Process.Kill()
	if killErr != nil && !errors.Is(killErr, os.ErrProcessDone) {
		return nil, fmt.Errorf("daemon did not open %s: %w; stop failed: %v; see %s",
			socket, os.ErrDeadlineExceeded, killErr, logPath)
	}
	return nil, fmt.Errorf("daemon did not open %s before startup deadline: %w; see %s",
		socket, os.ErrDeadlineExceeded, logPath)
}

// dialDaemonBefore 的每次探测也受总预算约束，不沿用固定两秒超时。
func dialDaemonBefore(socket string, deadline time.Time) (*daemonClient, error) {
	dialer := net.Dialer{Timeout: 200 * time.Millisecond, Deadline: deadline}
	conn, err := dialer.Dial("unix", socket)
	if err != nil {
		return nil, err
	}
	return &daemonClient{conn: conn, reader: bufio.NewReaderSize(conn, 64*1024)}, nil
}
