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

    func makeDefaultSpeedCell() -> UITableViewCell {
        makeDisclosureCell(
            "settings.row.defaultSpeed".localized,
            value: PlaybackSpeedPreference.displayName
        )
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
                : String(format: "%.2gx", step)
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
            message: nil,
            preferredStyle: .actionSheet
        )
        for opt in options {
            let title: String
            if let code = opt.code {
                title = opt.titleKey.localized
            } else {
                title = "settings.language.system".localized
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
