package core

import (
	"errors"
	"fmt"
	"time"

	"github.com/microsoft/Hybrid-Computer-Use/computer-use/internal/contract"
)

// StoredRequest is the response to a call that carried a request_id.
type StoredRequest struct {
	key      string
	response any
	Done     chan struct{} `json:"-"`
	Created  time.Time     `json:"-"`
}

// errRequestIDReused reports a request_id sent again with a different call.
var errRequestIDReused = errors.New("request_id was already used for a different call; use a new request_id for a new call")

// replayOnce runs a call at most once per request_id. A repeated id with the
// same key waits for the first call and returns its stored response, with
// replayed=true and no new input; the same id with another key is an error.
// The store is desktop state, so a client that lost its connection and
// reconnects on a new session still collects its result. It must be called
// without holding the desktop lock: run takes that lock itself, and a
// duplicate waits for the first call outside it. replayOnly refuses an
// absent result instead of treating an evicted request as fresh input.
func (d *DesktopShared) ReplayOnce(id, key string, replayOnly bool, run func() any) (any, bool, error) {
	if len(id) > contract.RequestIDMaxLength {
		return nil, false, fmt.Errorf("request_id is limited to %d characters", contract.RequestIDMaxLength)
	}
	d.RequestMu.Lock()
	d.PruneRequestResults(time.Now())
	if stored, found := d.RequestResults[id]; found {
		d.RequestMu.Unlock()
		if stored.key != key {
			return nil, false, errRequestIDReused
		}
		<-stored.Done
		d.RequestMu.Lock()
		completed, available := d.RequestResults[id]
		d.RequestMu.Unlock()
		if !available || completed.Done != stored.Done {
			return nil, false, contract.ErrReplayUnavailable
		}
		return completed.response, true, nil
	}
	if replayOnly {
		d.RequestMu.Unlock()
		return nil, false, contract.ErrReplayUnavailable
	}
	entry := StoredRequest{key: key, Done: make(chan struct{}), Created: time.Now()}
	d.RequestResults[id] = entry
	d.RequestMu.Unlock()

	response := run()

	d.RequestMu.Lock()
	entry.response = response
	d.RequestResults[id] = entry
	d.RequestMu.Unlock()
	close(entry.Done)
	return response, false, nil
}

func (d *DesktopShared) PruneRequestResults(now time.Time) {
	for id, stored := range d.RequestResults {
		select {
		case <-stored.Done:
		default:
			continue
		}
		if now.Sub(stored.Created) > contract.RequestResultTTL {
			delete(d.RequestResults, id)
		}
	}
	for len(d.RequestResults) > contract.MaxRequestResults {
		oldestID := ""
		var oldest time.Time
		for id, stored := range d.RequestResults {
			select {
			case <-stored.Done:
			default:
				continue
			}
			if oldestID == "" || stored.Created.Before(oldest) {
				oldestID, oldest = id, stored.Created
			}
		}
		if oldestID == "" {
			break
		}
		delete(d.RequestResults, oldestID)
	}
}
