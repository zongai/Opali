import UIKit

// MARK: - Subtitle Display (supports bilingual: original + translation)

extension VideoPlayerView {
    func setSubtitleCues(_ cues: [SubtitleCue]) {
        subtitleCues = cues
    }

    func clearSubtitles() {
        subtitleCues = []
        subtitleLabel.isHidden = true
        subtitleLabel.attributedText = nil
        subtitleLabel.text = nil
        ccButton.isSelected = false
    }

    func updateSubtitle(at time: Double) {
        guard !subtitleCues.isEmpty else {
            if !subtitleLabel.isHidden {
                subtitleLabel.isHidden = true
            }
            return
        }
        let cue = activeCue(at: time)
        if let cue {
            let attr = Self.attributedSubtitle(for: cue)
            if subtitleLabel.attributedText?.string != attr.string {
                subtitleLabel.attributedText = attr
            }
            if subtitleLabel.isHidden {
                subtitleLabel.isHidden = false
            }
        } else {
            if !subtitleLabel.isHidden {
                subtitleLabel.isHidden = true
            }
        }
    }

    /// Bilingual: smaller original above, primary translation below (kiss-style).
    private static func attributedSubtitle(for cue: SubtitleCue) -> NSAttributedString {
        let paragraph = NSMutableParagraphStyle()
        paragraph.alignment = .center
        paragraph.lineSpacing = 2
        let base: [NSAttributedString.Key: Any] = [
            .foregroundColor: UIColor.white,
            .paragraphStyle: paragraph
        ]
        if let tr = cue.translation?.trimmingCharacters(in: .whitespacesAndNewlines),
           !tr.isEmpty {
            let result = NSMutableAttributedString()
            let origFont = UIFont.systemFont(ofSize: 13, weight: .regular)
            let trFont = UIFont.systemFont(ofSize: 16, weight: .semibold)
            result.append(NSAttributedString(
                string: cue.text + "\n",
                attributes: base.merging([
                    .font: origFont,
                    .foregroundColor: UIColor.white.withAlphaComponent(0.75)
                ]) { $1 }
            ))
            result.append(NSAttributedString(
                string: tr,
                attributes: base.merging([.font: trFont]) { $1 }
            ))
            return result
        }
        return NSAttributedString(
            string: cue.text,
            attributes: base.merging([
                .font: UIFont.systemFont(ofSize: 16, weight: .semibold)
            ]) { $1 }
        )
    }

    /// Cue covering `time`, or nil in a gap between cues.
    private func activeCue(at time: Double) -> SubtitleCue? {
        var low = 0
        var high = subtitleCues.count - 1
        var found = -1
        while low <= high {
            let mid = (low + high) / 2
            if subtitleCues[mid].start <= time {
                found = mid
                low = mid + 1
            } else {
                high = mid - 1
            }
        }
        guard found >= 0, time < subtitleCues[found].end else {
            return nil
        }
        return subtitleCues[found]
    }

    func setCaptionTracks(
        _ tracks: [SubtitleTrack],
        activeLanguage: String?
    ) {
        setControlAvailability(ccButton, available: !tracks.isEmpty)
        ccButton.isSelected = activeLanguage != nil
    }

    @objc
    func ccTapped() {
        onCCTapped?()
    }
}
