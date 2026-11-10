//go:build windows

package transport

import (
	"bufio"
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"
	"syscall"
	"testing"
	"time"
)

// helper 通过真实 Windows 子进程覆盖文件锁和父进程退出，不改公共 TestMain。
func TestWindowsDaemonSpawnHelper(t *testing.T) {
	mode := os.Getenv("HCU_WINDOWS_SPAWN_HELPER")
	if mode == "" {
		return
	}
	socket := os.Args[len(os.Args)-1]
	switch mode {
	case "serve":
		fmt.Fprintln(os.Stdout, "helper stdout")
		fmt.Fprintln(os.Stderr, "helper stderr")
		os.Exit(runTestDaemon(socket))
	case "exit":
		fmt.Fprintln(os.Stderr, "helper failed intentionally")
		os.Exit(23)
	case "exit-zero":
		os.Exit(0)
	case "stall":
		<-time.After(time.Minute)
		os.Exit(24)
	case "lock":
		unlock, err := lockFile(socket+".lock", time.Now().Add(5*time.Second))
		if err != nil {
			fmt.Fprintln(os.Stderr, err)
			os.Exit(1)
		}
		fmt.Fprintln(os.Stdout, "ready")
		_, _ = bufio.NewReader(os.Stdin).ReadString('\n')
		unlock()
		os.Exit(0)
	case "client":
		daemonServeCommand = func(socket string) (*exec.Cmd, error) {
			return windowsSpawnHelperCommand("serve", socket), nil
		}
		// 每个进程先到达屏障，父测试再同时放行冷启动。
		fmt.Fprintln(os.Stdout, "ready")
		if _, err := bufio.NewReader(os.Stdin).ReadString('\n'); err != nil {
			os.Exit(1)
		}
		client, err := connectDaemon(socket, "", true)
		if err != nil {
			fmt.Fprintln(os.Stderr, err)
			os.Exit(1)
		}
		_ = json.NewEncoder(os.Stdout).Encode(client.hello)
		client.close()
		os.Exit(0)
	default:
		os.Exit(2)
	}
}

func windowsSpawnHelperCommand(mode, socket string) *exec.Cmd {
	command := exec.Command(os.Args[0], "-test.run=^TestWindowsDaemonSpawnHelper$", "--", socket)
	command.Env = append(os.Environ(), "HCU_TEST_DAEMON=", "HCU_WINDOWS_SPAWN_HELPER="+mode,
		"HCU_TEST_IDLE=30s", "COMPUTER_USE_ERROR_CONTEXT=off")
	return command
}

func windowsSpawnSocket(t *testing.T) string {
	t.Helper()
	// Windows AF_UNIX 仍有 sun_path 限制，避免 t.TempDir 的长测试名称。
	dir, err := os.MkdirTemp("", "hcu")
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		if err := os.RemoveAll(dir); err != nil {
			t.Error(err)
		}
	})
	return filepath.Join(dir, "中文 空格", "d.sock")
}

func windowsUseSpawnHelper(t *testing.T, mode string) {
	t.Helper()
	original := daemonServeCommand
	t.Cleanup(func() { daemonServeCommand = original })
	daemonServeCommand = func(socket string) (*exec.Cmd, error) {
		return windowsSpawnHelperCommand(mode, socket), nil
	}
}

// 在删除临时文件前等待进程退出，避免 Windows 的打开文件句柄阻止删除。
func windowsStopTestDaemon(t *testing.T, socket string, pid int) {
	t.Helper()
	handle, err := syscall.OpenProcess(syscall.SYNCHRONIZE|syscall.PROCESS_TERMINATE, false, uint32(pid))
	if err != nil {
		return
	}
	defer syscall.CloseHandle(handle)
	client, err := dialDaemon(socket)
	if err == nil {
		_ = client.conn.SetDeadline(time.Now().Add(2 * time.Second))
		var stopped map[string]any
		_ = client.request("stop", map[string]any{}, &stopped)
		client.close()
	}
	status, err := syscall.WaitForSingleObject(handle, 5000)
	if err != nil || status != syscall.WAIT_OBJECT_0 {
		_ = syscall.TerminateProcess(handle, 1)
		_, _ = syscall.WaitForSingleObject(handle, 5000)
		t.Errorf("daemon did not stop: status=%d err=%v", status, err)
	}
}

func TestWindowsDaemonAutoStart(t *testing.T) {
	socket := windowsSpawnSocket(t)
	windowsUseSpawnHelper(t, "serve")
	client, err := connectDaemon(socket, "windows", true)
	if err != nil {
		t.Fatal(err)
	}
	defer client.close()
	pid, _ := strconv.Atoi(fmt.Sprint(client.hello["pid"]))
	t.Cleanup(func() { windowsStopTestDaemon(t, socket, pid) })
	if id, ok := client.hello["instance_id"].(string); !ok || id == "" || pid == 0 {
		t.Fatalf("missing daemon identity: %v", client.hello)
	}
	data, err := os.ReadFile(socket + ".log")
	if err != nil || !bytes.Contains(data, []byte("helper stdout")) || !bytes.Contains(data, []byte("helper stderr")) {
		t.Fatalf("daemon output was not redirected: %q, %v", data, err)
	}
}

type windowsSpawnChild struct {
	command *exec.Cmd
	input   io.WriteCloser
	output  *bufio.Reader
	stderr  bytes.Buffer
}

func windowsStartSpawnChild(t *testing.T, mode, socket string) *windowsSpawnChild {
	t.Helper()
	base := windowsSpawnHelperCommand(mode, socket)
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	t.Cleanup(cancel)
	command := exec.CommandContext(ctx, base.Path, base.Args[1:]...)
	command.Env = base.Env
	child := &windowsSpawnChild{command: command}
	command.Stderr = &child.stderr
	input, err := command.StdinPipe()
	if err != nil {
		t.Fatal(err)
	}
	output, err := command.StdoutPipe()
	if err != nil {
		t.Fatal(err)
	}
	child.input, child.output = input, bufio.NewReader(output)
	if err := command.Start(); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		input.Close()
		if command.ProcessState == nil {
			_ = command.Process.Kill()
			_ = command.Wait()
		}
	})
	if ready, err := child.output.ReadString('\n'); err != nil || ready != "ready\n" {
		t.Fatalf("helper barrier: %q %v", ready, err)
	}
	return child
}

func TestWindowsDaemonConcurrentProcessesShareInstance(t *testing.T) {
	socket := windowsSpawnSocket(t)
	children := make([]*windowsSpawnChild, 4)
	for index := range children {
		children[index] = windowsStartSpawnChild(t, "client", socket)
	}
	for _, child := range children {
		if _, err := io.WriteString(child.input, "go\n"); err != nil {
			t.Fatal(err)
		}
		child.input.Close()
	}
	instance := ""
	pid := 0
	t.Cleanup(func() {
		if pid != 0 {
			windowsStopTestDaemon(t, socket, pid)
		}
	})
	for _, child := range children {
		var hello map[string]any
		decodeErr := json.NewDecoder(child.output).Decode(&hello)
		waitErr := child.command.Wait()
		if decodeErr != nil || waitErr != nil {
			t.Fatalf("helper client: decode=%v wait=%v stderr=%s", decodeErr, waitErr, child.stderr.String())
		}
		id, ok := hello["instance_id"].(string)
		if !ok || id == "" {
			t.Fatalf("missing instance identity: %v", hello)
		}
		if instance == "" {
			instance = id
			pid = int(hello["pid"].(float64))
		} else if id != instance {
			t.Fatalf("concurrent processes started distinct instances: %s, %s", instance, id)
		}
	}
	// 所有启动者已经退出，再次连接必须仍然到达原 daemon。
	client, err := connectDaemon(socket, "after-parent-exit", false)
	if err != nil {
		t.Fatalf("daemon did not survive its parent: %v", err)
	}
	defer client.close()
	if client.hello["instance_id"] != instance {
		t.Fatalf("daemon changed after parent exit: %v", client.hello)
	}
}

func TestWindowsDaemonStartupExit(t *testing.T) {
	for _, test := range []struct{ mode, status string }{{"exit", "23"}, {"exit-zero", "0"}} {
		t.Run(test.mode, func(t *testing.T) {
			socket := windowsSpawnSocket(t)
			windowsUseSpawnHelper(t, test.mode)
			_, err := startDaemon(socket)
			if err == nil || !strings.Contains(err.Error(), "exited during start") ||
				!strings.Contains(err.Error(), "exit status "+test.status) || !strings.Contains(err.Error(), socket+".log") {
				t.Fatalf("missing startup exit diagnostic: %v", err)
			}
		})
	}
}

func TestWindowsDaemonStartFailure(t *testing.T) {
	socket := windowsSpawnSocket(t)
	original := daemonServeCommand
	t.Cleanup(func() { daemonServeCommand = original })
	daemonServeCommand = func(string) (*exec.Cmd, error) {
		return exec.Command(filepath.Join(filepath.Dir(socket), "missing.exe")), nil
	}
	_, err := startDaemon(socket)
	if err == nil || !strings.Contains(err.Error(), "start daemon:") || !strings.Contains(err.Error(), socket+".log") {
		t.Fatalf("missing process creation diagnostic: %v", err)
	}
}

func TestWindowsDaemonLockBudget(t *testing.T) {
	socket := windowsSpawnSocket(t)
	if err := prepareDaemonDirectory(socket); err != nil {
		t.Fatal(err)
	}
	child := windowsStartSpawnChild(t, "lock", socket)
	original := daemonServeCommand
	t.Cleanup(func() { daemonServeCommand = original })
	daemonServeCommand = func(string) (*exec.Cmd, error) {
		t.Error("started a daemon while another process owned the lock")
		return nil, errors.New("unexpected spawn")
	}
	started := time.Now()
	_, err := startDaemonBefore(socket, started.Add(200*time.Millisecond))
	if !errors.Is(err, os.ErrDeadlineExceeded) || !strings.Contains(err.Error(), ".lock") {
		t.Fatalf("expected bounded lock wait: %v", err)
	}
	if elapsed := time.Since(started); elapsed > 2*time.Second {
		t.Fatalf("lock exceeded startup budget: %v", elapsed)
	}
	child.input.Close()
	if err := child.command.Wait(); err != nil {
		t.Fatalf("lock helper: %v %s", err, child.stderr.String())
	}
	unlock, err := lockFile(socket+".lock", time.Now().Add(time.Second))
	if err != nil {
		t.Fatalf("lock not released: %v", err)
	}
	unlock()
}

func TestWindowsDaemonExpiredBudgetDoesNotSpawn(t *testing.T) {
	socket := windowsSpawnSocket(t)
	original := daemonServeCommand
	t.Cleanup(func() { daemonServeCommand = original })
	daemonServeCommand = func(string) (*exec.Cmd, error) {
		t.Error("expired startup budget must not create a process")
		return nil, errors.New("unexpected spawn")
	}
	_, err := startDaemonBefore(socket, time.Now().Add(-time.Second))
	if !errors.Is(err, os.ErrDeadlineExceeded) {
		t.Fatalf("expired startup deadline was reset: %v", err)
	}
}

func TestWindowsDaemonReadinessBudget(t *testing.T) {
	socket := windowsSpawnSocket(t)
	original := daemonServeCommand
	t.Cleanup(func() { daemonServeCommand = original })
	var command *exec.Cmd
	daemonServeCommand = func(socket string) (*exec.Cmd, error) {
		command = windowsSpawnHelperCommand("stall", socket)
		return command, nil
	}
	started := time.Now()
	_, err := startDaemonBefore(socket, started.Add(500*time.Millisecond))
	if !errors.Is(err, os.ErrDeadlineExceeded) || !strings.Contains(err.Error(), socket+".log") {
		t.Fatalf("expected bounded readiness wait: %v", err)
	}
	if elapsed := time.Since(started); elapsed > 2*time.Second {
		t.Fatalf("readiness exceeded startup budget: %v", elapsed)
	}
	if command == nil || command.Process == nil {
		t.Fatal("readiness timeout did not start the helper")
	}
	windowsStopTestDaemon(t, socket, command.Process.Pid)
}

func TestWindowsDaemonCommandArguments(t *testing.T) {
	t.Setenv("HCU_IDLE_TIMEOUT", " 12s ")
	socket := `C:\用户 空格\桌面.sock`
	command, err := daemonServeCommand(socket)
	if err != nil {
		t.Fatal(err)
	}
	if len(command.Args) != 6 || command.Args[1] != "serve" || command.Args[2] != "--socket" ||
		command.Args[3] != socket || command.Args[4] != "--idle-timeout" || command.Args[5] != "12s" {
		t.Fatalf("arguments were not preserved: %q", command.Args)
	}
	detachProcess(command)
	if !command.SysProcAttr.HideWindow || command.SysProcAttr.CreationFlags&0x08000000 == 0 || command.SysProcAttr.CmdLine != "" {
		t.Fatalf("unexpected Windows process attributes: %+v", command.SysProcAttr)
	}
}
