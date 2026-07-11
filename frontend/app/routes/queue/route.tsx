import { Link, Form, redirect, useActionData, useNavigation } from "react-router";
import type { Route } from "./+types/route";
import styles from "./route.module.css"
import { Alert, Button, Form as BootstrapForm } from 'react-bootstrap';
import { backendClient, type HistorySlot, type QueueSlot } from "~/clients/backend-client.server";
import { HistoryTable } from "./components/history-table/history-table";
import { QueueTable } from "./components/queue-table/queue-table";
import { useState } from "react";
import { useHistoryEvents, useQueueEvents } from "./controllers/events-controller";
import { initializeQueueHistoryWebsocket } from "./controllers/websocket-controller";

const maxItems = 100;
export async function loader({ request }: Route.LoaderArgs) {
    const queuePromise = backendClient.getQueue(maxItems);
    const historyPromise = backendClient.getHistory(maxItems);
    const configPromise = backendClient.getConfig(["api.categories", "api.manual-category"])
    const queue = await queuePromise;
    const history = await historyPromise;
    const config = await configPromise;
    const categoriesValue = config
        .find(x => x.configName === "api.categories")
        ?.configValue ?? "uncategorized,audio,software,tv,movies";
    const manualCategory = config
        .find(x => x.configName === "api.manual-category")
        ?.configValue ?? "uncategorized";
    let categories = categoriesValue.split(',').map(x => x.trim());
    if (!categories.includes(manualCategory)) {
        categories = [manualCategory, ...categories];
    }

    return {
        queueSlots: queue?.slots || [],
        historySlots: history?.slots || [],
        totalQueueCount: queue?.noofslots || 0,
        totalHistoryCount: history?.noofslots || 0,
        categories: categories,
        manualCategory: manualCategory,
    }
}

export async function action({ request }: Route.ActionArgs) {
    try {
        const formData = await request.formData();
        const directUrl = formData.get("directUrl")?.toString().trim();
        const filenameHint = formData.get("filenameHint")?.toString().trim();
        const category = formData.get("cat")?.toString().trim() || "uncategorized";

        if (!directUrl) {
            return { error: "A direct URL is required." };
        }

        await backendClient.addDirectUrl(directUrl, category, filenameHint || undefined);
        return redirect("/queue");
    } catch (error) {
        if (error instanceof Error) {
            return { error: error.message };
        }
        throw error;
    }
}

export default function Queue(props: Route.ComponentProps) {
    const actionData = useActionData<typeof action>() as { error?: string } | undefined;
    const navigation = useNavigation();
    const [queueSlots, setQueueSlots] = useState<PresentationQueueSlot[]>(props.loaderData.queueSlots);
    const [historySlots, setHistorySlots] = useState<PresentationHistorySlot[]>(props.loaderData.historySlots);
    const [manualCategory, setManualCategory] = useState<string>(props.loaderData.manualCategory);
    const [directUrl, setDirectUrl] = useState("");
    const [filenameHint, setFilenameHint] = useState("");

    const isSubmitting = navigation.state === "submitting";
    const disableLiveView = queueSlots.length == maxItems || historySlots.length == maxItems;
    const submitDisabled = isSubmitting || directUrl.trim().length === 0;

    // queue/history events
    const queueEvents = useQueueEvents(setQueueSlots);
    const historyEvents = useHistoryEvents(setHistorySlots);

    // websocket
    initializeQueueHistoryWebsocket(queueEvents, historyEvents, disableLiveView);

    // view
    return (
        <div className={styles.container}>

            <Form method="post" className={styles.submissionCard}>
                <div className={styles.submissionHeader}>
                    <div>
                        <div className={styles.submissionTitle}>Add a direct URL</div>
                        <div className={styles.submissionDescription}>
                            Paste a media URL and mount it into WebDAV immediately.
                        </div>
                    </div>
                    <Button type="submit" variant="primary" disabled={submitDisabled}>
                        {isSubmitting ? "Adding..." : "Add to WebDAV"}
                    </Button>
                </div>
                {actionData?.error &&
                    <Alert className={styles.submissionAlert} variant="danger">
                        {actionData.error}
                    </Alert>
                }
                <div className={styles.submissionFields}>
                    <BootstrapForm.Control
                        name="directUrl"
                        type="url"
                        placeholder="https://example.com/video.mp4"
                        value={directUrl}
                        onChange={e => setDirectUrl(e.currentTarget.value)}
                        autoFocus
                    />
                    <BootstrapForm.Control
                        name="filenameHint"
                        type="text"
                        placeholder="Optional filename hint"
                        value={filenameHint}
                        onChange={e => setFilenameHint(e.currentTarget.value)}
                    />
                    <input type="hidden" name="cat" value={manualCategory} />
                </div>
            </Form>

            {/* warning */}
            {disableLiveView &&
                <Alert className={styles.alert} variant="warning">
                    <b>Attention</b>
                    <ul className={styles.list}>
                        <li className={styles.listItem}>
                            Displaying the first {queueSlots.length} of {props.loaderData.totalQueueCount} queue items
                        </li>
                        <li className={styles.listItem}>
                            Displaying the first {historySlots.length} of {props.loaderData.totalHistoryCount} history items
                        </li>
                        <li className={styles.listItem}>
                            Live view is disabled. Manually <Link to={'/queue'}>refresh</Link> the page for updates.
                        </li>
                        <li className={styles.listItem}>
                            (This is a bandaid — Proper pagination will be added soon)
                        </li>
                    </ul>
                </Alert>
            }

            {/* queue */}
            <div className={styles.queueContainer}>
                <QueueTable
                    queueSlots={queueSlots}
                    totalQueueCount={props.loaderData.totalQueueCount}
                    categories={props.loaderData.categories}
                    manualCategory={manualCategory}
                    onManualCategoryChanged={setManualCategory}
                    onIsSelectedChanged={queueEvents.onSelectQueueSlots}
                    onIsRemovingChanged={queueEvents.onRemovingQueueSlots}
                    onRemoved={queueEvents.onRemoveQueueSlots}
                />
            </div>

            {/* history */}
            {historySlots.length > 0 &&
                <HistoryTable
                    historySlots={historySlots}
                    totalHistoryCount={props.loaderData.totalHistoryCount}
                    onIsSelectedChanged={historyEvents.onSelectHistorySlots}
                    onIsRemovingChanged={historyEvents.onRemovingHistorySlots}
                    onRemoved={historyEvents.onRemoveHistorySlots}
                />
            }
        </div >
    );
}

export type PresentationHistorySlot = HistorySlot & {
    isSelected?: boolean,
    isRemoving?: boolean,
}

export type PresentationQueueSlot = QueueSlot & {
    isUploading?: boolean,
    isSelected?: boolean,
    isRemoving?: boolean,
    error?: string,
}