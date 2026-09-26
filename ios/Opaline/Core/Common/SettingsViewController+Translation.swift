import UIKit

// MARK: - Translation + default playback speed settings

extension SettingsViewController {
    func makeTranslationTargetCell() -> UITableViewCell {
        makeDisclosureCell(
            "settings.row.translationTarget".localized,
            value: TranslationPreferences.targetDisplayName
        )
    }

    func makeTranslationEngineCell() -> UITableViewCell {
        makeDisclosureCell(
            "settings.row.translationEngine".localized,
            value: TranslationPreferences.preferredEngine.displayName
        )
    }

    /// Inline speed control for common rates; full list via the accessory tap area.
    func makeDefaultSpeedCell() -> UITableViewCell {
        let theme = ThemeManager.shared
        let cell = UITableViewCell(style: .default, reuseIdentifier: nil)
        cell.backgroundColor = theme.surface
        cell.selectionStyle = .none
        cell.textLabel?.text = "settings.row.defaultSpeed".localized
        cell.textLabel?.textColor = theme.primaryText
        cell.textLabel?.numberOfLines = 1
        cell.textLabel?.adjustsFontSizeToFitWidth = true
        cell.textLabel?.minimumScaleFactor = 0.85

        // Common rates shown inline; other steps still available from picker.
        let labels = ["0.75×", "1×", "1.25×", "1.5×", "2×"]
        let values: [Float] = [0.75, 1.0, 1.25, 1.5, 2.0]
        let seg = UISegmentedControl(items: labels)
        seg.selectedSegmentIndex = values.firstIndex {
            abs($0 - PlaybackSpeedPreference.defaultSpeed) < 0.01
        } ?? 1
        seg.addTarget(self, action: #selector(defaultSpeedSegmentChanged(_:)), for: .valueChanged)
        seg.setContentHuggingPriority(.required, for: .horizontal)
        cell.accessoryView = seg

        // Long-press / secondary path: full step list
        let long = UILongPressGestureRecognizer(
            target: self,
            action: #selector(defaultSpeedLongPress(_:))
        )
        cell.addGestureRecognizer(long)
        return cell
    }

    @objc
    private func defaultSpeedSegmentChanged(_ seg: UISegmentedControl) {
        let values: [Float] = [0.75, 1.0, 1.25, 1.5, 2.0]
        guard seg.selectedSegmentIndex >= 0,
              seg.selectedSegmentIndex < values.count else { return }
        PlaybackSpeedPreference.defaultSpeed = values[seg.selectedSegmentIndex]
        // Keep selection accurate if preference snaps
        if let idx = values.firstIndex(where: {
            abs($0 - PlaybackSpeedPreference.defaultSpeed) < 0.01
        }) {
            seg.selectedSegmentIndex = idx
        }
    }

    @objc
    private func defaultSpeedLongPress(_ gr: UILongPressGestureRecognizer) {
        guard gr.state == .began else { return }
        showDefaultSpeedPicker()
    }

    func handleTranslationSelection(_ row: Row) -> Bool {
        switch row {
        case .translationTarget:
            showTranslationTargetPicker()
        case .translationEngine:
            showTranslationEnginePicker()
        default:
            return false
        }
        return true
    }

    func showDefaultSpeedPicker() {
        let steps = PlaybackSpeedPreference.steps
        let sheet = UIAlertController(
            title: "settings.row.defaultSpeed".localized,
            message: "settings.footer.defaultSpeed".localized,
            preferredStyle: .actionSheet
        )
        for step in steps {
            let title = step == 1.0
                ? "player.speed.normal".localized
                : String(format: "%.2g×", step)
            let action = UIAlertAction(title: title, style: .default) { [weak self] _ in
                PlaybackSpeedPreference.defaultSpeed = step
                self?.reloadAllSettings()
            }
            if abs(step - PlaybackSpeedPreference.defaultSpeed) < 0.01 {
                action.setValue(true, forKey: "checked")
            }
            sheet.addAction(action)
        }
        sheet.addAction(UIAlertAction(title: "common.cancel".localized, style: .cancel))
        configureCenteredPopover(sheet)
        present(sheet, animated: true)
    }

    private func showTranslationTargetPicker() {
        let options = TranslationPreferences.targetOptions
        let sheet = UIAlertController(
            title: "settings.row.translationTarget".localized,
            message: "settings.footer.translationTarget".localized,
            preferredStyle: .actionSheet
        )
        for opt in options {
            let title: String
            if opt.code != nil {
                title = opt.titleKey.localized
            } else {
                title = "settings.language.system".localized
                    + " (\(TranslationPreferences.effectiveTargetLanguage))"
            }
            let action = UIAlertAction(title: title, style: .default) { [weak self] _ in
                TranslationPreferences.targetLanguageOverride = opt.code
                self?.reloadAllSettings()
            }
            let isSelected: Bool
            if let code = opt.code {
                isSelected = TranslationPreferences.targetLanguageOverride == code
            } else {
                isSelected = TranslationPreferences.targetLanguageOverride == nil
            }
            if isSelected {
                action.setValue(true, forKey: "checked")
            }
            sheet.addAction(action)
        }
        sheet.addAction(UIAlertAction(title: "common.cancel".localized, style: .cancel))
        configureCenteredPopover(sheet)
        present(sheet, animated: true)
    }

    private func showTranslationEnginePicker() {
        let engines = TranslationPreferences.Engine.allCases
        let sheet = UIAlertController(
            title: "settings.row.translationEngine".localized,
            message: "settings.footer.translationEngine".localized,
            preferredStyle: .actionSheet
        )
        for engine in engines {
            let action = UIAlertAction(
                title: engine.displayName,
                style: .default
            ) { [weak self] _ in
                TranslationPreferences.preferredEngine = engine
                self?.reloadAllSettings()
            }
            if engine == TranslationPreferences.preferredEngine {
                action.setValue(true, forKey: "checked")
            }
            sheet.addAction(action)
        }
        sheet.addAction(UIAlertAction(title: "common.cancel".localized, style: .cancel))
        configureCenteredPopover(sheet)
        present(sheet, animated: true)
    }
}
