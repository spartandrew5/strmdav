import styles from "./empty-queue.module.css"

export function EmptyQueue() {
    return (
        <div className={styles.emptyState}>
            <div className={styles.emptyIcon}>🪅🎉🥳</div>
            <div className={styles.emptyTitle}>Empty Queue!</div>
            <div className={styles.emptyDescription}>
                Add a direct URL above to mount media into WebDAV and make it available to Arr.
            </div>
        </div>
    );
}