//go:build darwin || darwin_sidecar_test

package provider

import (
	"errors"
	"io"
	"net"
	"path/filepath"
	"testing"
	"time"
)

// The listener can finish accepting after the launcher failure wins the select.
type darwinDelayedAcceptListener struct {
	connection net.Conn
	started    chan struct{}
	release    chan struct{}
}

func (listener *darwinDelayedAcceptListener) Accept() (net.Conn, error) {
	close(listener.started)
	<-listener.release
	return listener.connection, nil
}

func (*darwinDelayedAcceptListener) Close() error   { return nil }
func (*darwinDelayedAcceptListener) Addr() net.Addr { return &net.UnixAddr{} }

func TestDarwinProviderStartupClosesLateAcceptedConnection(t *testing.T) {
	server, client := net.Pipe()
	defer server.Close()
	defer client.Close()
	listener := &darwinDelayedAcceptListener{
		connection: server,
		started:    make(chan struct{}),
		release:    make(chan struct{}),
	}
	done := make(chan struct{})
	worker := &darwinProviderWorker{done: done, waitErr: errors.New("launcher failed")}
	result := make(chan error, 1)
	pidPath := filepath.Join(t.TempDir(), "provider.pid")
	go func() {
		connection, err := waitForDarwinProviderConnection(
			listener, worker, pidPath,
			time.Now().Add(5*time.Second), time.Second,
		)
		if connection != nil {
			connection.Close()
		}
		result <- err
	}()
	<-listener.started
	close(done)
	if err := <-result; err == nil {
		t.Fatal("launcher failure must abort startup")
	}
	close(listener.release)
	if err := client.SetReadDeadline(time.Now().Add(time.Second)); err != nil {
		t.Fatal(err)
	}
	if _, err := client.Read(make([]byte, 1)); !errors.Is(err, io.EOF) {
		t.Fatalf("abandoned provider connection must close, got %v", err)
	}
}

func TestDarwinProviderStartupKeepsDeliveredConnectionOpen(t *testing.T) {
	server, client := net.Pipe()
	defer server.Close()
	defer client.Close()
	listener := &darwinDelayedAcceptListener{
		connection: server,
		started:    make(chan struct{}),
		release:    make(chan struct{}),
	}
	close(listener.release)
	connection, err := waitForDarwinProviderConnection(
		listener, &darwinProviderWorker{done: make(chan struct{})},
		filepath.Join(t.TempDir(), "provider.pid"),
		time.Now().Add(time.Second), time.Second,
	)
	if err != nil {
		t.Fatal(err)
	}
	if connection != server {
		t.Fatal("startup must transfer the accepted connection to its caller")
	}
	if err := client.SetDeadline(time.Now().Add(time.Second)); err != nil {
		t.Fatal(err)
	}
	written := make(chan error, 1)
	go func() {
		_, err := client.Write([]byte("ok"))
		written <- err
	}()
	data := make([]byte, 2)
	if _, err := io.ReadFull(connection, data); err != nil {
		t.Fatal(err)
	}
	if err := <-written; err != nil {
		t.Fatal(err)
	}
	if string(data) != "ok" {
		t.Fatalf("provider response = %q", data)
	}
}
