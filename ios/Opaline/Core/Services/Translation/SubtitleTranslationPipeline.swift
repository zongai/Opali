import Foundation

/// Kiss-translator–inspired caption pipeline: filter non-speech, merge short
/// ASR cues into sentences, chunk for batch translation, emit bilingual cues.
/// Reference: fishjar/kiss-translator `youtubeSubtitleProcessing.js`
enum SubtitleTranslationPipeline {

    struct Segment {
        var start: TimeInterval
        var end: TimeInterval
        var text: String
        var isNonSpeech: Bool
    }

    /// Music / sound-effect markers — do not send to translation engines.
    static func isNonSpeech(_ text: String) -> Bool {
        let t = text.trimmingCharacters(in: .whitespacesAndNewlines)
        if t.isEmpty { return true }
        let lower = t.lowercased()
        if lower.hasPrefix("[") && lower.hasSuffix("]") { return true }
        if lower.hasPrefix("(") && lower.hasSuffix(")") {
            let inner = String(lower.dropFirst().dropLast())
            let markers = ["music", "applause", "laughter", "cheers", "silence",
                           "inaudible", "speaking", "singing", "cough", "blank"]
            if markers.contains(where: { inner.contains($0) }) { return true }
        }
        if t.contains("♪") || t.contains("♫") { return true }
        return false
    }

    /// Map YouTube track language code → translation source hint.
    static func sourceLanguage(fromTrackCode code: String) -> String {
        TranslationLanguageNorm.canonical(code)
    }

    /// Merge consecutive speech cues into sentence-sized segments (kiss builtin rules).
    static func mergeCues(
        _ cues: [SubtitleCue],
        pauseThreshold: TimeInterval = 0.85,
        maxDuration: TimeInterval = 8.0,
        maxWords: Int = 28
    ) -> [Segment] {
        var out: [Segment] = []
        var buf: [SubtitleCue] = []
        var wordCount = 0

        func flush() {
            guard !buf.isEmpty else { return }
            let text = buf.map(\.text)
                .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
                .filter { !$0.isEmpty }
                .joined(separator: " ")
            out.append(Segment(
                start: buf.first!.start,
                end: buf.last!.end,
                text: text,
                isNonSpeech: false
            ))
            buf = []
            wordCount = 0
        }

        for cue in cues {
            let trimmed = cue.text.trimmingCharacters(in: .whitespacesAndNewlines)
            if isNonSpeech(trimmed) {
                flush()
                out.append(Segment(
                    start: cue.start, end: cue.end, text: trimmed, isNonSpeech: true
                ))
                continue
            }
            if let last = buf.last {
                let gap = cue.start - last.end
                let duration = cue.start - buf[0].start
                let endsSentence = last.text.range(of: #"[.?!…」』】］\)\]]\s*$"#, options: .regularExpression) != nil
                let words = trimmed.split { $0.isWhitespace || $0.isNewline }.count
                if endsSentence || gap > pauseThreshold || duration >= maxDuration
                    || (wordCount + words >= maxWords && gap > 0.25) {
                    flush()
                }
            }
            buf.append(cue)
            wordCount += trimmed.split { $0.isWhitespace || $0.isNewline }.count
        }
        flush()
        return out.filter { !$0.text.isEmpty }
    }

    /// Pack segment texts into ~maxChars batches (preserve order).
    static func chunkTexts(_ texts: [String], maxChars: Int = 900) -> [[String]] {
        var batches: [[String]] = []
        var cur: [String] = []
        var size = 0
        for t in texts {
            if !cur.isEmpty, size + t.count > maxChars {
                batches.append(cur)
                cur = []
                size = 0
            }
            cur.append(t)
            size += t.count + 1
        }
        if !cur.isEmpty { batches.append(cur) }
        return batches
    }

    /// Translate merged speech segments; non-speech kept as-is. Returns bilingual cues.
    static func translateSegments(
        _ segments: [Segment],
        target: String,
        source: String?,
        completion: @escaping (Result<[SubtitleCue], Error>) -> Void
    ) {
        let speechIdx = segments.indices.filter { !segments[$0].isNonSpeech }
        let speechTexts = speechIdx.map { segments[$0].text }
        guard !speechTexts.isEmpty else {
            let cues = segments.map {
                SubtitleCue(start: $0.start, end: $0.end, text: $0.text, translation: nil)
            }
            completion(.success(cues))
            return
        }

        let batches = chunkTexts(speechTexts)
        var translatedSpeech = Array(repeating: "", count: speechTexts.count)
        var firstError: Error?
        let group = DispatchGroup()
        let lock = NSLock()
        var offset = 0
        for batch in batches {
            let start = offset
            offset += batch.count
            group.enter()
            TranslationService.shared.translateMany(batch, target: target, source: source) { result in
                switch result {
                case .success(let parts):
                    for (i, p) in parts.enumerated() where start + i < translatedSpeech.count {
                        translatedSpeech[start + i] = p
                    }
                case .failure(let e):
                    lock.lock()
                    if firstError == nil { firstError = e }
                    lock.unlock()
                    // Keep originals for this batch
                    for i in 0..<batch.count where start + i < translatedSpeech.count {
                        translatedSpeech[start + i] = speechTexts[start + i]
                    }
                }
                group.leave()
            }
        }
        group.notify(queue: .main) {
            if let firstError, translatedSpeech.allSatisfy({ $0.isEmpty }) {
                completion(.failure(firstError))
                return
            }
            var speechCursor = 0
            var cues: [SubtitleCue] = []
            for seg in segments {
                if seg.isNonSpeech {
                    cues.append(SubtitleCue(
                        start: seg.start, end: seg.end, text: seg.text, translation: nil
                    ))
                } else {
                    let tr = speechCursor < translatedSpeech.count
                        ? translatedSpeech[speechCursor] : ""
                    speechCursor += 1
                    let same = tr.trimmingCharacters(in: .whitespacesAndNewlines)
                        .caseInsensitiveCompare(seg.text) == .orderedSame
                    cues.append(SubtitleCue(
                        start: seg.start,
                        end: seg.end,
                        text: seg.text,
                        translation: (tr.isEmpty || same) ? nil : tr
                    ))
                }
            }
            completion(.success(cues))
        }
    }
}
