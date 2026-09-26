import UIKit

// MARK: - Harbor-style translation (title / description / comments / captions)

extension WatchViewController {
    /// Settings → translate title + description, all loaded comments, active captions.
    func showTranslationMenu() {
        let items: [PlayerMenuItem] = [
            PlayerMenuItem(title: "player.translate.titleDesc".localized) { [weak self] in
                self?.translateTitleAndDescription()
            },
            PlayerMenuItem(title: "player.translate.comments".localized) { [weak self] in
                self?.translateVisibleComments()
            },
            PlayerMenuItem(title: "player.translate.captions".localized) { [weak self] in
                self?.translateActiveCaptions()
            }
        ]
        presentPlayerMenu(
            title: "player.translate.menu".localized,
            items: items
        )
    }

    func translateTitleAndDescription() {
        guard !isTranslating else { return }
        isTranslating = true
        let title = originalTitleText ?? titleLabel.text ?? initialVideo.title
        if originalTitleText == nil {
            originalTitleText = title
        }
        let desc = descriptionText
        let group = DispatchGroup()
        var newTitle: String?
        var newDesc: String?
        group.enter()
        TranslationService.shared.translate(title) { result in
            if case .success(let s) = result { newTitle = s }
            group.leave()
        }
        if !desc.isEmpty {
            group.enter()
            TranslationService.shared.translate(desc) { result in
                if case .success(let s) = result {
                    newDesc = s
                }
                group.leave()
            }
        }
        group.notify(queue: .main) { [weak self] in
            guard let self else { return }
            self.isTranslating = false
            if let newTitle {
                self.titleLabel.text = newTitle
            }
            if let newDesc {
                self.translatedDescriptionText = newDesc
                self.applyTranslatedDescription()
            }
            if newTitle == nil && newDesc == nil {
                self.showTranslationToast("player.translate.failed".localized)
            } else {
                self.showTranslationToast("player.translate.done".localized)
            }
        }
    }

    func applyTranslatedDescription() {
        guard let translated = translatedDescriptionText, !translated.isEmpty else {
            return
        }
        let theme = ThemeManager.shared
        let base = NSMutableAttributedString()
        if let existing = descriptionLabel.attributedText, existing.length > 0 {
            base.append(existing)
            base.append(NSAttributedString(string: "\n\n"))
        }
        let header = NSAttributedString(
            string: "player.translate.caption".localized + "\n",
            attributes: [
                .font: UIFont.systemFont(ofSize: 12, weight: .semibold),
                .foregroundColor: theme.secondaryText
            ]
        )
        base.append(header)
        base.append(NSAttributedString(
            string: translated,
            attributes: [
                .font: UIFont.systemFont(ofSize: 14),
                .foregroundColor: theme.primaryText
            ]
        ))
        descriptionLabel.attributedText = base
        descriptionButton.isHidden = false
    }

    func translateVisibleComments() {
        guard !isTranslating else { return }
        let comments = commentThreads.map(\.comment) + commentThreads.flatMap(\.replies)
        guard !comments.isEmpty else {
            showTranslationToast("player.translate.noComments".localized)
            return
        }
        isTranslating = true
        let bodies = comments.map(\.content)
        TranslationService.shared.translateMany(bodies) { [weak self] result in
            DispatchQueue.main.async {
                guard let self else { return }
                self.isTranslating = false
                switch result {
                case .success(let texts):
                    for (i, c) in comments.enumerated() where i < texts.count {
                        self.commentTranslations[c.id] = texts[i]
                    }
                    self.renderComments()
                    self.showTranslationToast("player.translate.done".localized)
                case .failure:
                    self.showTranslationToast("player.translate.failed".localized)
                }
            }
        }
    }

    func translateActiveCaptions() {
        guard let lang = activeSubtitleLanguage,
              let track = captionTracks.first(where: { $0.languageCode == lang }) else {
            showTranslationToast("player.translate.noCaptions".localized)
            return
        }
        guard !isTranslating else { return }
        isTranslating = true
        SubtitleService.shared.load(track: track) { [weak self] cues in
            guard let self else { return }
            let texts = cues.map(\.text)
            TranslationService.shared.translateMany(texts) { result in
                DispatchQueue.main.async {
                    self.isTranslating = false
                    switch result {
                    case .success(let translated):
                        let newCues = zip(cues, translated).map { cue, t in
                            SubtitleCue(start: cue.start, end: cue.end, text: t.isEmpty ? cue.text : t)
                        }
                        self.videoPlayerView?.setSubtitleCues(newCues)
                        self.showTranslationToast("player.translate.done".localized)
                    case .failure:
                        self.showTranslationToast("player.translate.failed".localized)
                    }
                }
            }
        }
    }

    func showTranslationToast(_ message: String) {
        let alert = UIAlertController(title: nil, message: message, preferredStyle: .alert)
        present(alert, animated: true)
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) {
            alert.dismiss(animated: true)
        }
    }
}
