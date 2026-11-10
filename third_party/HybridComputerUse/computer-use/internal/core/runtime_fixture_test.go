package core

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"reflect"
	"testing"
	"time"
)

type runtimeTimeoutFixture struct {
	Platform string `json:"platform"`
	Duration string `json:"duration"`
	Error    string `json:"error"`
}

func strictFixtureDecode(data []byte, target any) error {
	decoder := json.NewDecoder(bytes.NewReader(data))
	decoder.DisallowUnknownFields()
	if err := decoder.Decode(target); err != nil {
		return err
	}
	var trailing any
	if err := decoder.Decode(&trailing); err != io.EOF {
		if err == nil {
			return fmt.Errorf("fixture contains trailing JSON")
		}
		return err
	}
	return nil
}

func assertFixtureJSONEquivalent(t *testing.T, original, encoded []byte) {
	t.Helper()
	var left any
	var right any
	if err := json.Unmarshal(original, &left); err != nil {
		t.Fatal(err)
	}
	if err := json.Unmarshal(encoded, &right); err != nil {
		t.Fatal(err)
	}
	if !reflect.DeepEqual(left, right) {
		t.Fatalf("provider wire JSON changed:\nfixture=%s\nencoded=%s",
			original, encoded)
	}
}

func TestRuntimeTimeoutErrorRemainsPlatformSpecific(t *testing.T) {
	fixture := runtimeTimeoutFixture{
		Platform: "Linux",
		Duration: "30s",
		Error:    "Linux runtime timed out after 30s",
	}
	duration, err := time.ParseDuration(fixture.Duration)
	if err != nil {
		t.Fatal(err)
	}
	if got := (&RuntimeTimeoutError{Duration: duration}).Error(); got != fixture.Error {
		t.Fatalf("timeout error = %q, want %q", got, fixture.Error)
	}
}
