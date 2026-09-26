import UIKit

extension NotificationsViewController {
    func setupEditToolbar() {
        editToolbar.items = [
            markAllReadToolbarItem,
            UIBarButtonItem(barButtonSystemItem: .flexibleSpace, target: nil, action: nil),
            deleteToolbarItem
        ]
        editToolbar.isHidden = true
        view.addSubview(editToolbar)
        editToolbar.translatesAutoresizingMaskIntoConstraints = false
        NSLayoutConstraint.activate([
            editToolbar.leadingAnchor.constraint(equalTo: view.leadingAnchor),
            editToolbar.trailingAnchor.constraint(equalTo: view.trailingAnchor),
            editToolbar.bottomAnchor.constraint(equalTo: view.safeAreaLayoutGuide.bottomAnchor),
            editToolbar.heightAnchor.constraint(equalToConstant: Self.editToolbarHeight)
        ])
    }

    func applyEditToolbarTheme() {}

    func updateToolbarButtonsState() {
        let selected = tableView.indexPathsForSelectedRows?.count ?? 0
        deleteToolbarItem.isEnabled = selected > 0
        markAllReadToolbarItem.isEnabled = !items.isEmpty
    }

    @objc func editTapped() {
        let editing = !tableView.isEditing
        tableView.setEditing(editing, animated: true)
        editToolbar.isHidden = !editing
        editButton.title = editing ? "common.done".localized : "common.edit".localized
        updateToolbarButtonsState()
    }

    @objc func markAllReadTapped() {
        AppNotificationStore.shared.markAllRead()
        tableView.reloadData()
        updateToolbarButtonsState()
    }

    @objc func deleteSelectedTapped() {
        guard let paths = tableView.indexPathsForSelectedRows, !paths.isEmpty else { return }
        suppressNextReload = true
        for path in paths {
            AppNotificationStore.shared.remove(id: items[path.row].id)
        }
        tableView.deleteRows(at: paths, with: .automatic)
        updateEmptyState()
        updateToolbarButtonsState()
    }
}
