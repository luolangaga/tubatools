//go:build darwin || darwin_sidecar_test

package provider

import (
	"bufio"
	"errors"
	"net"
	"testing"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/core"
)

func TestDarwinClosedConnectionReportsUnsentRequest(t *testing.T) {
	connection, peer := net.Pipe()
	defer connection.Close()
	if err := peer.Close(); err != nil {
		t.Fatal(err)
	}
	worker := &darwinProviderWorker{connection: connection, stdout: bufio.NewReader(connection)}
	_, err := worker.call(validDarwinObserveAppsRequest("closed-before-write"), time.Second)
	var transport *core.RuntimeTransportError
	if !errors.As(err, &transport) || transport.RequestSent || transport.Phase != "write_request" {
		t.Fatalf("closed connection must report zero bytes sent: %#v", err)
	}
}
