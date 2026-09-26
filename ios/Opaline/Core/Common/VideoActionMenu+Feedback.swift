import UIKit

// MARK: - Feedback actions

/// "Remove from watch history", "Not interested", "Don't recommend
/// channel", "Hide" — whatever the response this card came from offered.
/// Labels and tokens are server-supplied; the official `/feedback` endpoint
/// consumes the opaque token as-is.
extension VideoActionMenu {
    /// Toast after a successful feedback call, lifted from the official app
    /// wording rather than inventing copy here.
    enum FeedbackOutcome {
        case tunedRecommendations, removed

        var message: String {
            switch self {
            case .tunedRecommendations:
                "video.menu.feedbackTuned".localized
            case .removed:
                "video.menu.feedbackRemoved".localized
            }
        }
    }

    static func feedbackItems(
        video: Video,
        from presenter: UIViewController,
        outcome: FeedbackOutcome,
        onRemoved: (() -> Void)?,
        engagement: EngagementService = ServiceContainer.engagement
    ) -> [PlayerMenuItem] {
        let described = video.feedbackActions
            .map { "\($0.label)[\($0.icon ?? "-")]" }
            .joined(separator: ", ")
        AppLog.innertube("feedback actions for \(video.id): \(described)")

        // Prefer not-interested / don't-recommend first when present.
        let ordered = video.feedbackActions.sorted { a, b in
            rank(a) < rank(b)
        }

        return ordered.map { action in
            PlayerMenuItem(
                title: action.label,
                isDestructive: true,
                iconName: iconName(for: action)
            ) {
                engagement.sendFeedback(token: action.token) { result in
                    DispatchQueue.main.async {
                        switch result {
                        case .success:
                            onRemoved?()
                            ToastView.show(outcome.message, in: presenter.view)
                        case .failure:
                            showFailed(in: presenter.view)
                        }
                    }
                }
            }
        }
    }

    private static func rank(_ action: FeedbackAction) -> Int {
        let icon = action.icon?.uppercased() ?? ""
        if icon.contains("NOT_INTERESTED") { return 0 }
        if icon.contains("NOT_RECOMMENDED") || icon.contains("CHANNEL") {
            return 1
        }
        if icon.contains("SNOOZE") { return 2 }
        return 3
    }

    private static func iconName(for action: FeedbackAction) -> String {
        // Asset catalog only ships a generic minus; keep one icon for all
        // feedback rows so missing-asset gaps do not leave blank leading space.
        _ = action
        return "icon_minus_circle"
    }

}

