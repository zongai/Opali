import Foundation

// MARK: - Feedback actions
//
// The endpoint behind "Remove from watch history", "Not interested" and
// "Don't recommend channel". Each of those is one opaque token that came
// with the renderer it belongs to (see `FeedbackActionParser`), so sending
// one is the same request every time (#105, #106).
//
// The response says nothing about what the action did: a processed token
// comes back as `isProcessed` and, at most, a `dismissalFollowUpRenderer` —
// the "Tell us why" survey, not a confirmation. So the toast wording is the
// app's own, chosen by the screen the menu was opened from (device-checked
// 2026-08-26).

extension InnertubeClient {
    func sendFeedback(
        token feedbackToken: String,
        completion: @escaping (Result<Void, Error>) -> Void
    ) {
        OAuthClient.shared.validToken { [weak self] result in
            switch result {
            case .failure(let error):
                completion(.failure(error))
            case .success(let authToken):
                self?.executeFeedback(
                    feedbackToken: feedbackToken,
                    token: authToken,
                    completion: completion
                )
            }
        }
    }

    private func executeFeedback(
        feedbackToken: String,
        token: String,
        completion: @escaping (Result<Void, Error>) -> Void
    ) {
        // Tokens from WEB home/search menus are bound to the WEB client;
        // TV context rejects them with isProcessed=false.
        var body = webContext
        body["feedbackTokens"] = [feedbackToken]
        body["isFeedbackTokenUnencrypted"] = false
        body["shouldMerge"] = false
        execute(
            urlString: "\(baseURL)\(InnertubeEndpoint.feedback)",
            body: body,
            headers: authHeaders(token: token),
            logTag: "feedback"
        ) { json -> Void? in
            let responses = json["feedbackResponses"] as? [[String: Any]]
            let isProcessed = responses?.first?["isProcessed"] as? Bool
            AppLog.innertube("feedback isProcessed=\(isProcessed ?? false)")
            // Accept explicit success; also accept empty/missing flag when
            // the HTTP call already succeeded (some clients omit the field).
            if isProcessed == false {
                return nil
            }
            return ()
        } completion: { completion($0) }
    }
}
