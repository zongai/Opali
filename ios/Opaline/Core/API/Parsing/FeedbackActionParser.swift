import Foundation

/// Pulls the feedback actions a renderer offers out of its menu.
///
/// YouTube sends each action with a translated label and an opaque
/// `feedbackToken`. The app shows the server label when present, otherwise
/// falls back to a local string keyed by `iconType` so "Not interested" /
/// "Don't recommend channel" still appear when the text node is missing.
enum FeedbackActionParser {
    /// Walks the whole renderer: menu paths differ by client (WEB / TV /
    /// lockup) and surface (home, search, history).
    static func actions(
        in value: Any,
        label: String? = nil,
        icon: String? = nil
    ) -> [FeedbackAction] {
        var found: [FeedbackAction] = []
        collect(in: value, label: label, icon: icon, into: &found)
        // Dedupe by token; keep first label/icon seen.
        var seen = Set<String>()
        return found.filter { seen.insert($0.token).inserted }
    }

    private static func collect(
        in value: Any,
        label: String?,
        icon: String?,
        into out: inout [FeedbackAction]
    ) {
        if let array = value as? [Any] {
            for element in array {
                collect(in: element, label: label, icon: icon, into: &out)
            }
            return
        }
        guard let dict = value as? [String: Any] else {
            return
        }

        let itemLabel = resolvedLabel(in: dict) ?? label
        let itemIcon = dict.digString("icon", "iconType")
            ?? dict.digString("iconType")
            ?? icon

        if let token = feedbackToken(in: dict) {
            let resolved = itemLabel
                ?? fallbackLabel(forIcon: itemIcon)
            if let resolved {
                out.append(FeedbackAction(
                    label: resolved,
                    icon: itemIcon,
                    token: token
                ))
            }
        }

        for child in dict.values {
            collect(in: child, label: itemLabel, icon: itemIcon, into: &out)
        }
    }

    /// Official paths used by WEB, ANDROID, and newer lockup menus.
    private static func feedbackToken(in dict: [String: Any]) -> String? {
        if let t = dict.digString("feedbackEndpoint", "feedbackToken") {
            return t
        }
        if let t = dict.digString(
            "serviceEndpoint", "feedbackEndpoint", "feedbackToken"
        ) {
            return t
        }
        if let t = dict.digString(
            "command", "feedbackEndpoint", "feedbackToken"
        ) {
            return t
        }
        if let t = dict.digString(
            "innertubeCommand", "feedbackEndpoint", "feedbackToken"
        ) {
            return t
        }
        if let t = dict.digString(
            "onTap", "innertubeCommand", "feedbackEndpoint", "feedbackToken"
        ) {
            return t
        }
        // commandExecutorCommand.commands[].feedbackEndpoint
        if let commands = dict.digArray("commandExecutorCommand", "commands")
            ?? dict["commands"] as? [[String: Any]] {
            for cmd in commands {
                if let t = feedbackToken(in: cmd) {
                    return t
                }
            }
        }
        return nil
    }

    private static func resolvedLabel(in dict: [String: Any]) -> String? {
        if let s = InnertubeClient.simpleText(from: dict["text"]), !s.isEmpty {
            return s
        }
        if let s = InnertubeClient.simpleText(from: dict["title"]), !s.isEmpty {
            return s
        }
        if let s = dict.digString(
            "accessibility", "accessibilityData", "label"
        ), !s.isEmpty {
            return s
        }
        if let s = dict.digString("accessibilityText"), !s.isEmpty {
            return s
        }
        return nil
    }

    /// When YouTube omits the text node, iconType still identifies the action.
    private static func fallbackLabel(forIcon icon: String?) -> String? {
        guard let icon = icon?.uppercased() else {
            return nil
        }
        if icon.contains("NOT_INTERESTED") || icon == "FEEDBACK" {
            return "video.menu.notInterested".localized
        }
        if icon.contains("NOT_RECOMMENDED")
            || icon.contains("REMOVE_FROM_HISTORY")
            || icon.contains("NO_RECOMMEND")
            || (icon.contains("CHANNEL") && icon.contains("FEEDBACK")) {
            return "video.menu.dontRecommendChannel".localized
        }
        if icon.contains("SNOOZE") {
            return "video.menu.snooze".localized
        }
        // Unknown feedback still usable — generic label.
        if icon.contains("FEEDBACK") || icon.contains("REMOVE") {
            return "video.menu.feedbackGeneric".localized
        }
        return nil
    }
}
