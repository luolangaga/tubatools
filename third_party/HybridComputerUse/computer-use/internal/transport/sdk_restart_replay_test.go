package transport

import (
	"bufio"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"net"
	"os"
	"os/exec"
	"path/filepath"
	"sync/atomic"
	"testing"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

func TestDaemonRejectsWrongInstanceBeforeInput(t *testing.T) {
	server, backend, socket := startInProcessDaemon(t, time.Minute)
	client := testClient(t, socket, "instance-binding")
	refs := refTestObserve(t, client, "call")
	args := map[string]any{"mode": "act", "actions": []any{map[string]any{"type": "click", "ref": refs["r"]}}}
	for _, binding := range []any{"another-instance", "", 12, nil} {
		var response core.Record
		err := client.request("call", map[string]any{
			"tool": "AX", "args": args, "request_id": "binding-test", "daemon_instance": binding,
		}, &response)
		var rpcError *daemonRPCError
		if !errors.As(err, &rpcError) {
			t.Fatalf("binding %v must reject before input, got %v", binding, err)
		}
		if binding == "another-instance" && rpcError.code != daemonErrorInstance {
			t.Fatalf("wrong instance returned code %d", rpcError.code)
		}
	}
	if backend.inputCount() != 0 {
		t.Fatal("invalid instance binding emitted input")
	}
	response := callRecordOver(t, client, "AX", args, map[string]any{"daemon_instance": server.instanceID})
	if !response.OK || backend.inputCount() != 1 {
		t.Fatalf("matching instance rejected: %+v", response)
	}
}

func TestReplayPruningPreservesInflightRequest(t *testing.T) {
	shared := &core.DesktopShared{RequestResults: map[string]core.StoredRequest{}}
	started, release := make(chan struct{}), make(chan struct{})
	finished := make(chan error, 1)
	go func() {
		_, _, err := shared.ReplayOnce("inflight", "input", false, func() any {
			close(started)
			<-release
			return "original result"
		})
		finished <- err
	}()
	<-started
	shared.RequestMu.Lock()
	entry := shared.RequestResults["inflight"]
	entry.Created = time.Now().Add(-2 * contract.RequestResultTTL)
	shared.RequestResults["inflight"] = entry
	for index := 0; index <= contract.MaxRequestResults; index++ {
		done := make(chan struct{})
		close(done)
		shared.RequestResults[fmt.Sprintf("completed-%d", index)] = core.StoredRequest{Done: done, Created: time.Now()}
	}
	shared.PruneRequestResults(time.Now())
	_, retained := shared.RequestResults["inflight"]
	shared.RequestMu.Unlock()
	close(release)
	if err := <-finished; err != nil {
		t.Fatal(err)
	}
	if !retained {
		t.Fatal("cache evicted an in-flight input")
	}
	result, replayed, err := shared.ReplayOnce("inflight", "input", true, func() any {
		t.Error("completed retry emitted input")
		return nil
	})
	if err != nil || !replayed || result != "original result" {
		t.Fatalf("original in-flight result lost: %v %v %v", result, replayed, err)
	}
}

// A proxy drops one completed call's reply. The test then replaces the real
// daemon at the same socket, in the same PID, before allowing reconnection.
// Provider input counts remain independent of SDK exceptions and responses.
func TestPythonSDKReplayDaemonInstance(t *testing.T) {
	python, err := exec.LookPath("python3")
	if err != nil {
		t.Skip("python3 is not installed")
	}
	for _, scenario := range []string{"same", "restart", "read", "no-id", "blank-id", "invalid-id", "legacy", "expired", "evicted", "unsent"} {
		for _, style := range []string{"sync", "async"} {
			t.Run(scenario+"/"+style, func(t *testing.T) {
				t.Setenv("COMPUTER_USE_ERROR_CONTEXT", "off")
				t.Setenv("COMPUTER_USE_OUTPUT_DIR", t.TempDir())
				socket := shortSocket(t)
				first, backend, stop := startRefTestDaemon(t, socket)
				defer func() { stop() }()
				proxySocket := shortSocket(t)
				listener, err := net.Listen("unix", proxySocket)
				if err != nil {
					t.Fatal(err)
				}
				defer listener.Close()
				dropped, resume := make(chan struct{}), make(chan struct{})
				var callsAfterDrop atomic.Int32
				proxyDone := make(chan error, 1)
				go func() {
					didDrop := false
					for {
						client, err := listener.Accept()
						if err != nil {
							proxyDone <- nil
							return
						}
						upstream, err := net.Dial("unix", socket)
						if err != nil {
							client.Close()
							proxyDone <- err
							return
						}
						reader, replies := bufio.NewReader(client), bufio.NewReader(upstream)
						for {
							line, err := reader.ReadBytes('\n')
							if err != nil {
								break
							}
							var request struct {
								Method string `json:"method"`
								Params struct {
									Args map[string]any `json:"args"`
								} `json:"params"`
							}
							if err := json.Unmarshal(line, &request); err != nil {
								proxyDone <- err
								return
							}
							input := request.Method == "call" && request.Params.Args["mode"] == "act"
							if didDrop && input {
								callsAfterDrop.Add(1)
							}
							if !didDrop && input && scenario == "unsent" {
								didDrop = true
								upstream.Close()
								close(dropped)
								<-resume
								break
							}
							_, err = upstream.Write(line)
							if err != nil {
								proxyDone <- err
								return
							}
							response, err := replies.ReadBytes('\n')
							if err != nil {
								proxyDone <- err
								return
							}
							if scenario == "legacy" && request.Method == "hello" {
								var envelope map[string]any
								if err := json.Unmarshal(response, &envelope); err != nil {
									proxyDone <- err
									return
								}
								delete(envelope["result"].(map[string]any), "instance_id")
								response, _ = json.Marshal(envelope)
								response = append(response, '\n')
							}
							if !didDrop && (input || scenario == "read" && request.Method == "call") {
								didDrop = true
								upstream.Close()
								close(dropped)
								<-resume
								break
							}
							if _, err = client.Write(response); err != nil {
								break
							}
						}
						upstream.Close()
						client.Close()
					}
				}()
				ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
				defer cancel()
				script, _ := filepath.Abs(filepath.Join("..", "..", "testdata", "sdk_restart_replay.py"))
				command := exec.CommandContext(ctx, python, "-u", script, scenario, style)
				command.Env = append(os.Environ(), "HCU_SOCKET="+proxySocket, "DISPLAY=:9", "WAYLAND_DISPLAY=")
				type outcome struct {
					output []byte
					err    error
				}
				finished := make(chan outcome, 1)
				go func() { output, err := command.CombinedOutput(); finished <- outcome{output, err} }()
				select {
				case <-dropped:
				case result := <-finished:
					t.Fatalf("script exited before reply drop: %v\n%s", result.err, result.output)
				case err := <-proxyDone:
					t.Fatalf("proxy stopped early: %v", err)
				case <-ctx.Done():
					t.Fatal(ctx.Err())
				}
				var nextBackend *fakeDesktopBackend
				if scenario == "expired" {
					shared := first.desktop.Shared
					shared.RequestMu.Lock()
					for id, entry := range shared.RequestResults {
						entry.Created = time.Now().Add(-2 * contract.RequestResultTTL)
						shared.RequestResults[id] = entry
					}
					shared.RequestMu.Unlock()
				}
				if scenario == "evicted" {
					filler := testClient(t, socket, "eviction")
					for index := 0; index < contract.MaxRequestResults+2; index++ {
						callRecordOver(t, filler, "AX", map[string]any{"mode": "apps"}, map[string]any{"request_id": fmt.Sprintf("filler-%d", index)})
					}
					filler.close()
				}
				if scenario == "restart" || scenario == "read" {
					stop()
					stop = func() {}
					var next *daemon
					next, nextBackend, stop = startRefTestDaemon(t, socket)
					if first.instanceID == next.instanceID {
						t.Fatal("new daemon reused instance identity in same PID")
					}
				}
				close(resume)
				result := <-finished
				t.Logf("%s", result.output)
				if result.err != nil {
					t.Errorf("SDK replay: %v", result.err)
				}
				wantInputs, wantRetries := 1, int32(0)
				if scenario == "read" || scenario == "unsent" {
					wantInputs = 0
				}
				if scenario == "same" || scenario == "expired" || scenario == "evicted" || scenario == "unsent" {
					wantRetries = 1
				}
				if backend.inputCount() != wantInputs || callsAfterDrop.Load() != wantRetries {
					t.Errorf("first inputs=%d want=%d; replayed calls=%d want=%d", backend.inputCount(), wantInputs, callsAfterDrop.Load(), wantRetries)
				}
				if nextBackend != nil && nextBackend.inputCount() != 0 {
					t.Error("new daemon emitted repeated input")
				}
				listener.Close()
				if err := <-proxyDone; err != nil {
					t.Fatal(fmt.Errorf("proxy: %w", err))
				}
			})
		}
	}
}
