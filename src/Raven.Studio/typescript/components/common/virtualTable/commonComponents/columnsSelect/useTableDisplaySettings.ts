import { Table as TanstackTable } from "@tanstack/react-table";

export interface ColumnMeta {
    id: string;
    canHide: boolean;
    canPin: boolean;
}

export function useTableDisplaySettings<T>(table: TanstackTable<T>) {
    const allColumns = table.getAllColumns();

    const columnMetas: ColumnMeta[] = allColumns.map((column) => ({
        id: column.id,
        canHide: column.getCanHide(),
        canPin: column.getCanPin(),
    }));

    const allColumnIds = allColumns.map((x) => x.id);

    const getInitialColumnOrder = (): string[] => {
        const order = table.getState().columnOrder;
        if (order && order.length > 0) {
            // Fill in any columns not yet in the order (e.g. first open)
            const missing = allColumnIds.filter((id) => !order.includes(id));
            return [...order, ...missing];
        }
        return allColumnIds;
    };

    const getInitialPinnedIds = (): string[] => {
        const pinning = table.getState().columnPinning;
        return pinning?.left ?? [];
    };

    const getInitialSelectedIds = (): string[] =>
        allColumns.filter((column) => column.getIsVisible()).map((x) => x.id);

    const applySettings = (selectedIds: string[], columnOrder: string[], pinnedIds: string[]) => {
        allColumns.forEach((column) => {
            if (column.getCanHide()) {
                column.toggleVisibility(selectedIds.includes(column.id));
            }
        });
        table.setColumnOrder(columnOrder);
        table.setColumnPinning({ left: pinnedIds });
    };

    return {
        columnMetas,
        allColumnIds,
        getInitialColumnOrder,
        getInitialPinnedIds,
        getInitialSelectedIds,
        applySettings,
    };
}
