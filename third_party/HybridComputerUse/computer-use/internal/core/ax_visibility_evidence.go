package core

type AXVisibilityEvidence struct {
	State         string `json:"state"`
	Reason        string `json:"reason"`
	ExceptionType string `json:"exceptionType,omitempty"`
	HResult       string `json:"hresult,omitempty"`
}

func cloneVisibilityEvidence(evidence *AXVisibilityEvidence) *AXVisibilityEvidence {
	if evidence == nil {
		return nil
	}
	copy := *evidence
	exceptionType, hresult := []rune(copy.ExceptionType), []rune(copy.HResult)
	copy.ExceptionType = string(exceptionType[:min(len(exceptionType), 128)])
	copy.HResult = string(hresult[:min(len(hresult), 10)])
	return &copy
}

func mergeVisibilityEvidence(first, second *AXVisibilityEvidence) *AXVisibilityEvidence {
	first, second = cloneVisibilityEvidence(first), cloneVisibilityEvidence(second)
	rank := func(value *AXVisibilityEvidence) int {
		if value == nil {
			return 0
		}
		if value.State == "unknown" {
			if value.Reason == "read_error" {
				return 3
			}
			return 2
		}
		return 1
	}
	if rank(second) > rank(first) {
		return second
	}
	if first != nil && second != nil && first.State == second.State &&
		first.Reason == second.Reason &&
		(first.ExceptionType != second.ExceptionType || first.HResult != second.HResult) {
		// Conflicting aliases cannot establish one exact exception.
		first.ExceptionType, first.HResult = "", ""
	}
	return first
}
