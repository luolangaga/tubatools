//go:build darwin || darwin_sidecar_test

package provider

import (
	"fmt"
	"net"
	"os"
	"strings"
	"time"
)

func startDarwinProviderWorkerWithRetry(
	artifact darwinProviderArtifact,
) (*darwinProviderWorker, error) {
	delays := []time.Duration{
		250 * time.Millisecond,
		750 * time.Millisecond,
		1500 * time.Millisecond,
		3 * time.Second,
	}
	failures := make([]string, 0, len(delays)+1)
	for attempt := 0; attempt <= len(delays); attempt++ {
		worker, err := darwinProviderWorkerStarter(artifact)
		if err == nil {
			return worker, nil
		}
		failures = append(
			failures,
			fmt.Sprintf("attempt %d: %v", attempt+1, err),
		)
		if attempt < len(delays) {
			darwinProviderRetrySleep(delays[attempt])
		}
	}
	return nil, fmt.Errorf(
		"macOS provider startup failed after %d attempts: %s",
		len(failures),
		strings.Join(failures, "; "),
	)
}

type darwinProviderAcceptResult struct {
	connection net.Conn
	err        error
}

func waitForDarwinProviderConnection(
	listener net.Listener,
	worker *darwinProviderWorker,
	pidPath string,
	deadline time.Time,
	launchPIDGrace time.Duration,
) (net.Conn, error) {
	acceptResult := make(chan darwinProviderAcceptResult)
	acceptDone := make(chan struct{})
	defer close(acceptDone)
	go func() {
		connection, err := listener.Accept()
		result := darwinProviderAcceptResult{
			connection: connection,
			err:        err,
		}
		select {
		case acceptResult <- result:
		case <-acceptDone:
			// Startup can fail while Accept finishes. Only the receiver owns
			// a delivered connection; reclaim one that startup abandoned.
			if connection != nil {
				_ = connection.Close()
			}
		}
	}()

	startupTimer := time.NewTimer(time.Until(deadline))
	defer startupTimer.Stop()
	pidPoll := time.NewTicker(20 * time.Millisecond)
	defer pidPoll.Stop()

	launcherDone := worker.done
	var launchPIDTimer *time.Timer
	var launchPIDDeadline <-chan time.Time
	defer func() {
		if launchPIDTimer != nil {
			launchPIDTimer.Stop()
		}
	}()

	updateProviderPID := func() {
		if worker.providerPID > 0 {
			return
		}
		if providerPID, err := readDarwinProviderPID(pidPath); err == nil {
			worker.providerPID = providerPID
		}
	}

	for {
		select {
		case result := <-acceptResult:
			updateProviderPID()
			return result.connection, result.err
		case <-pidPoll.C:
			updateProviderPID()
			if worker.providerPID > 0 &&
				!darwinProviderProcessExists(worker.providerPID) {
				return nil, fmt.Errorf(
					"provider pid %d exited before connecting",
					worker.providerPID,
				)
			}
		case <-launcherDone:
			launcherDone = nil
			updateProviderPID()
			if worker.waitErr != nil {
				return nil, fmt.Errorf(
					"LaunchServices command failed: %w",
					worker.waitErr,
				)
			}
			if worker.providerPID == 0 {
				wait := launchPIDGrace
				if remaining := time.Until(deadline); wait > remaining {
					wait = remaining
				}
				if wait <= 0 {
					return nil, fmt.Errorf(
						"LaunchServices command exited but provider pid was not published",
					)
				}
				launchPIDTimer = time.NewTimer(wait)
				launchPIDDeadline = launchPIDTimer.C
			}
		case <-launchPIDDeadline:
			updateProviderPID()
			if worker.providerPID == 0 {
				return nil, fmt.Errorf(
					"LaunchServices command exited successfully but provider pid "+
						"was not published within %s",
					launchPIDGrace,
				)
			}
			launchPIDDeadline = nil
		case <-startupTimer.C:
			updateProviderPID()
			return nil, os.ErrDeadlineExceeded
		}
	}
}
