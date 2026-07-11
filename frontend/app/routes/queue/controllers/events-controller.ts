import { useCallback, useMemo } from "react";
import type { HistorySlot, QueueSlot } from "~/clients/backend-client.server";
import type { PresentationHistorySlot, PresentationQueueSlot } from "../route";

export type QueueEvents = {
    onAddQueueSlot: (queueSlot: QueueSlot) => void,
    onSelectQueueSlots: (ids: Set<string>, isSelected: boolean) => void,
    onRemovingQueueSlots: (ids: Set<string>, isRemoving: boolean) => void,
    onRemoveQueueSlots: (ids: Set<string>) => void,
    onChangeQueueSlotStatus: (message: string) => void,
    onChangeQueueSlotPercentage: (message: string) => void
};

export type HistoryEvents = {
    onAddHistorySlot: (historySlot: HistorySlot) => void,
    onSelectHistorySlots: (ids: Set<string>, isSelected: boolean) => void,
    onRemovingHistorySlots: (ids: Set<string>, isRemoving: boolean) => void,
    onRemoveHistorySlots: (ids: Set<string>) => void
};

export function useQueueEvents(
    setQueueSlots: (value: React.SetStateAction<PresentationQueueSlot[]>) => void,
) {
    const onAddQueueSlot = useCallback((queueSlot: QueueSlot) => {
        setQueueSlots(slots => [...slots, queueSlot]);
    }, [setQueueSlots]);

    const onSelectQueueSlots = useCallback((ids: Set<string>, isSelected: boolean) => {
        setQueueSlots(slots => slots.map(x => ids.has(x.nzo_id) ? { ...x, isSelected } : x));
    }, [setQueueSlots]);

    const onRemovingQueueSlots = useCallback((ids: Set<string>, isRemoving: boolean) => {
        setQueueSlots(slots => slots.map(x => ids.has(x.nzo_id) ? { ...x, isRemoving } : x));
    }, [setQueueSlots]);

    const onRemoveQueueSlots = useCallback((ids: Set<string>) => {
        setQueueSlots(slots => slots.filter(x => !ids.has(x.nzo_id)));
    }, [setQueueSlots]);

    const onChangeQueueSlotStatus = useCallback((message: string) => {
        const [nzo_id, status] = message.split('|');
        setQueueSlots(slots => slots.map(x => x.nzo_id === nzo_id ? { ...x, status } : x));
    }, [setQueueSlots]);

    const onChangeQueueSlotPercentage = useCallback((message: string) => {
        const [nzo_id, true_percentage] = message.split('|');
        setQueueSlots(slots => slots.map(x => x.nzo_id === nzo_id ? { ...x, true_percentage } : x));
    }, [setQueueSlots]);

    return memoize({
        onAddQueueSlot,
        onSelectQueueSlots,
        onRemovingQueueSlots,
        onRemoveQueueSlots,
        onChangeQueueSlotStatus,
        onChangeQueueSlotPercentage
    });
}

export function useHistoryEvents(
    setHistorySlots: (value: React.SetStateAction<PresentationHistorySlot[]>) => void
) {
    const onAddHistorySlot = useCallback((historySlot: HistorySlot) => {
        setHistorySlots(slots => [historySlot, ...slots]);
    }, [setHistorySlots]);

    const onSelectHistorySlots = useCallback((ids: Set<string>, isSelected: boolean) => {
        setHistorySlots(slots => slots.map(x => ids.has(x.nzo_id) ? { ...x, isSelected } : x));
    }, [setHistorySlots]);

    const onRemovingHistorySlots = useCallback((ids: Set<string>, isRemoving: boolean) => {
        setHistorySlots(slots => slots.map(x => ids.has(x.nzo_id) ? { ...x, isRemoving } : x));
    }, [setHistorySlots]);

    const onRemoveHistorySlots = useCallback((ids: Set<string>) => {
        setHistorySlots(slots => slots.filter(x => !ids.has(x.nzo_id)));
    }, [setHistorySlots]);

    return memoize({
        onAddHistorySlot,
        onSelectHistorySlots,
        onRemovingHistorySlots,
        onRemoveHistorySlots
    });
}

function memoize<T extends Record<string, unknown>>(object: T): T {
    // eslint-disable-next-line react-hooks/exhaustive-deps
    return useMemo(() => object, Object.values(object));
}