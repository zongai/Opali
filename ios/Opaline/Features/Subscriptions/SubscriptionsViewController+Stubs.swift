import Foundation

extension SubscriptionsViewController {
    func loadFeed() {
        AppLog.log("Subs", "stub loadFeed")
    }

    func loadSubscribedChannels(force: Bool = false) {
        AppLog.log("Subs", "stub loadSubscribedChannels force=\(force)")
    }

    func loadChannelVideos(_ channel: SubscribedChannel) {
        AppLog.log("Subs", "stub loadChannelVideos")
    }

    func refreshNewContentDots() {}
}

extension AppCache {
    func clearSubscriptionsFeed() {}
    func clearSubscribedChannels() {}
}
