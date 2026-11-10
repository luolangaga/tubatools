package core

// publicTextDelivery excludes private routing and native message identifiers.
// Completion counts never establish that the application accepted the text.
func publicTextDelivery(value any) map[string]any {
	var evidence *RuntimeTextDeliveryEvidence
	switch value := value.(type) {
	case RuntimeActionEvidence:
		evidence = value.RuntimeTextDeliveryEvidence
	case *RuntimeActionEvidence:
		if value != nil {
			evidence = value.RuntimeTextDeliveryEvidence
		}
	}
	if evidence == nil {
		return nil
	}
	public := map[string]any{
		"source_utf16_index":    evidence.SourceUTF16Index,
		"source_utf16_length":   evidence.SourceUTF16Length,
		"attempted_messages":    evidence.AttemptedMessages,
		"returned_messages":     evidence.ReturnedMessages,
		"unknown_message_index": evidence.UnknownMessageIndex,
		"sequence_phase":        evidence.SequencePhase,
		"effect_verified":       evidence.EffectVerified,
	}
	if evidence.SourceUTF16Count != nil {
		public["source_utf16_count"] = *evidence.SourceUTF16Count
	}
	return public
}
