import UIKit

// MARK: - Harbor-style translation (title / description / comments / captions)

extension WatchViewController {

    @objc
    func translateTitleTapped() {
        translateTitleAndDescription()
    }

    @objc
    func translateCaptionsTapped() {
        translateActiveCaptions()
    }

    @objc
    func translateCommentsTapped() {
        translateVisibleComments()
    }

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
        var lastError: Error?
        group.enter()
        TranslationService.shared.translate(title) { result in
            switch result {
            case .success(let s): newTitle = s
            case .failure(let e): lastError = e
            }
            group.leave()
        }
        if !desc.isEmpty {
            group.enter()
            TranslationService.shared.translate(desc) { result in
                switch result {
                case .success(let s): newDesc = s
                case .failure(let e): lastError = e
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
                self.presentTranslationFailure(error: lastError)
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
            presentTranslationFailure(message: "player.translate.noComments".localized)
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
                case .failure(let error):
                    self.presentTranslationFailure(error: error)
                }
            }
        }
    }

    func translateActiveCaptions() {
        guard let lang = activeSubtitleLanguage,
              let track = captionTracks.first(where: { $0.languageCode == lang }) else {
            presentTranslationFailure(message: "player.translate.noCaptions".localized)
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
                            SubtitleCue(
                                start: cue.start,
                                end: cue.end,
                                text: t.isEmpty ? cue.text : t
                            )
                        }
                        self.videoPlayerView?.setSubtitleCues(newCues)
                    case .failure(let error):
                        self.presentTranslationFailure(error: error)
                    }
                }
            }
        }
    }

    /// Failure alert with full message (engine list, DeepL key hint, etc.).
    func presentTranslationFailure(message: String) {
        let alert = UIAlertController(
            title: "player.translate.failed".localized,
            message: message,
            preferredStyle: .alert
        )
        alert.addAction(UIAlertAction(title: "common.ok".localized, style: .default))
        present(alert, animated: true)
    }

    func presentTranslationFailure(error: Error?) {
        let msg = (error as? LocalizedError)?.errorDescription
            ?? error?.localizedDescription
            ?? "player.translate.failed".localized
        presentTranslationFailure(message: msg)
    }
}
