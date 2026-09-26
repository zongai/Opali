import UIKit

// MARK: - Incomplete SearchViewController body (source snapshot)

extension SearchViewController {
    func search(query: String) {
        lastQuery = query
        AppLog.log("Search", "stub search: \(query)")
        // Full search pipeline not present in this tree.
        refreshControl.endRefreshing()
    }

    func updatePanel(for text: String) {
        // Suggestions / history panel not present.
    }

    func showHistoryIfIdle() {
        // History UI not present.
    }

    func clearSearchResults() {
        // Results table clear not present.
    }
}
