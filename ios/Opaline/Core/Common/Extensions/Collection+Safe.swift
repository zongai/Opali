import Foundation

extension Collection {
    /// Safe subscript — returns nil when index is out of bounds.
    subscript(safe index: Index) -> Element? {
        indices.contains(index) ? self[index] : nil
    }
}
