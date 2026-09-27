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
        if captionTracks.isEmpty {
            presentTranslationFailure(message: "player.translate.captions.noTracks".localized)
            return
        }
        guard let lang = activeSubtitleLanguage else {
            let available = captionTracks
                .map { $0.name.isEmpty ? $0.languageCode : $0.name }
                .joined(separator: ", ")
            let msg = "player.translate.captions.notEnabled".localized
                + "\n\n"
                + "player.translate.captions.available".localized(with: available)
            presentTranslationFailure(message: msg)
            return
        }
        guard let track = captionTracks.first(where: { $0.languageCode == lang }) else {
            presentTranslationFailure(
                message: "player.translate.captions.trackMissing".localized(with: lang)
            )
            return
        }
        guard !isTranslating else { return }
        isTranslating = true
        let target = TranslationService.preferredTarget
        let source = SubtitleTranslationPipeline.sourceLanguage(fromTrackCode: lang)
        SubtitleService.shared.load(track: track) { [weak self] cues in
            guard let self else { return }
            if cues.isEmpty {
                DispatchQueue.main.async {
                    self.isTranslating = false
                    self.presentTranslationFailure(
                        message: "player.translate.captions.loadEmpty".localized(with: lang)
                    )
                }
                return
            }
            // P0: merge ASR fragments → chunked translation (kiss-style).
            let segments = SubtitleTranslationPipeline.mergeCues(cues)
            let speechCount = segments.filter { !$0.isNonSpeech }.count
            if speechCount == 0 {
                DispatchQueue.main.async {
                    self.isTranslating = false
                    self.presentTranslationFailure(
                        message: "player.translate.captions.emptyText".localized
                    )
                }
                return
            }
            if TranslationLanguageNorm.isSameLanguage(source, target) {
                DispatchQueue.main.async {
                    self.isTranslating = false
                    self.presentTranslationFailure(
                        message: "player.translate.captions.alreadyTarget".localized(
                            with: source, target
                        )
                    )
                }
                return
            }
            SubtitleTranslationPipeline.translateSegments(
                segments, target: target, source: source
            ) { result in
                DispatchQueue.main.async {
                    self.isTranslating = false
                    switch result {
                    case .success(let bilingual):
                        let changed = bilingual.filter {
                            ($0.translation?.isEmpty == false)
                        }.count
                        self.videoPlayerView?.setSubtitleCues(bilingual)
                        if changed == 0 {
                            self.presentTranslationFailure(
                                message: "player.translate.captions.noChange".localized(
                                    with: bilingual.count, target
                                )
                            )
                        }
                    case .failure(let error):
                        let base = (error as? LocalizedError)?.errorDescription
                            ?? error.localizedDescription
                        let msg = "player.translate.captions.engineFailed".localized(
                            with: lang, target
                        ) + "\n\n" + base
                        self.presentTranslationFailure(message: msg)
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
