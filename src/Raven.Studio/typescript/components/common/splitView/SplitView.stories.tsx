import { Meta, StoryObj } from "@storybook/react-webpack5";
import { withStorybookContexts, withBootstrap5 } from "test/storybookTestUtils";
import { useViewSheet, ViewSheet } from "./ViewSheet";
import Button from "react-bootstrap/Button";
import { ViewSheetWidth } from "./ViewSheet";
import { Icon } from "components/common/Icon";
import { Checkbox } from "components/common/Checkbox";
import {
    closestCenter,
    DndContext,
    DragOverlay,
    PointerSensor,
    useSensor,
    useSensors,
} from "@dnd-kit/core";
import { arrayMove, SortableContext, useSortable, verticalListSortingStrategy } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { CSSProperties, useState } from "react";
import classNames from "classnames";

export default {
    title: "Bits/SplitView",
    decorators: [withStorybookContexts, withBootstrap5],
} satisfies Meta;

interface DefaultStoryArgs {
    initialWidth: ViewSheetWidth;
    minWidth: ViewSheetWidth;
    maxWidth: ViewSheetWidth;
}

export const Default: StoryObj<DefaultStoryArgs> = {
    name: "SplitView",
    render: (args) => {
        const { open, close } = useViewSheet();

        const handleOpenSheet = () => {
            open({
                component: (
                    <ViewSheet>
                        <ViewSheet.Header>
                            <h5 className="mb-0">
                                <Icon icon="document" />
                                Sheet header
                            </h5>
                        </ViewSheet.Header>
                        <ViewSheet.Body>
                            <span>Sheet body</span>
                        </ViewSheet.Body>
                        <ViewSheet.Footer>
                            <span>Sheet footer</span>
                        </ViewSheet.Footer>
                    </ViewSheet>
                ),
                ...args,
            });
        };

        return (
            <div className="vstack gap-2">
                <div>Main component</div>
                <p>
                    Some long text Some long text Some long text Some long text Some long text Some long text Some long
                    text Some long text Some long text Some long text Some long text Some long text Some long text Some
                    long text Some long text
                </p>
                <div>
                    <Button variant="primary" onClick={handleOpenSheet}>
                        Open Sheet
                    </Button>
                </div>
                <div>
                    <Button variant="secondary" onClick={close}>
                        Close Sheet
                    </Button>
                </div>
            </div>
        );
    },
    args: {
        initialWidth: "50%",
        minWidth: "30%",
        maxWidth: "75%",
    },
};

// Sample columns for the Column Layout Settings story
const sampleColumns = ["Name", "Collection", "Size", "Last Modified", "Flags", "ID", "Tags", "Score"];

interface ColumnLayoutStoryArgs {
    initialWidth: ViewSheetWidth;
}

export const ColumnLayoutSettings: StoryObj<ColumnLayoutStoryArgs> = {
    render: (args) => {
        const { open, close } = useViewSheet();

        const handleOpenSheet = () => {
            open({
                component: <ColumnLayoutSettingsSheet onClose={close} />,
                initialWidth: args.initialWidth,
                minWidth: "20%",
                maxWidth: "50%",
                isPinned: false,
            });
        };

        return (
            <div className="vstack gap-2">
                <div>Main content area</div>
                <div>
                    <Button variant="secondary" onClick={handleOpenSheet}>
                        <Icon icon="table" />
                        Column layout settings
                    </Button>
                </div>
            </div>
        );
    },
    args: {
        initialWidth: "30%",
    },
};

function ColumnLayoutSettingsSheet({ onClose }: { onClose: () => void }) {
    const [columns, setColumns] = useState(sampleColumns);
    const [selectedIds, setSelectedIds] = useState<string[]>(sampleColumns.slice());
    const [pinnedIds, setPinnedIds] = useState<string[]>([]);
    const [activeDragId, setActiveDragId] = useState<string | null>(null);

    const sensors = useSensors(useSensor(PointerSensor));

    const allSelected = selectedIds.length === sampleColumns.length;
    const someSelected = selectedIds.length > 0 && selectedIds.length < sampleColumns.length;

    const handleToggleAll = () => {
        setSelectedIds(allSelected ? [] : sampleColumns.slice());
    };

    const handleToggle = (id: string) => {
        setSelectedIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
    };

    const handleTogglePin = (id: string) => {
        setPinnedIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
    };

    const handleDragEnd = (event: any) => {
        const { active, over } = event;
        if (over && active.id !== over.id) {
            setColumns((items) => {
                const oldIndex = items.indexOf(active.id);
                const newIndex = items.indexOf(over.id);
                return arrayMove(items, oldIndex, newIndex);
            });
        }
        setActiveDragId(null);
    };

    const handleReset = () => {
        setColumns(sampleColumns);
        setSelectedIds(sampleColumns.slice());
        setPinnedIds([]);
    };

    const handleApply = () => {
        onClose();
    };

    const activeColumn = activeDragId ?? null;

    return (
        <ViewSheet>
            <ViewSheet.Header>
                <h5 className="mb-0">
                    <Icon icon="table" />
                    Column layout settings
                </h5>
            </ViewSheet.Header>
            <ViewSheet.Body className="p-0">
                <div className="px-3 py-2 d-flex flex-row align-items-center gap-1 border-bottom border-secondary">
                    <Checkbox
                        selected={allSelected}
                        toggleSelection={handleToggleAll}
                        indeterminate={someSelected}
                        color="primary"
                        title="Select all"
                    >
                        Select all
                    </Checkbox>
                </div>
                <div>
                    <DndContext
                        sensors={sensors}
                        collisionDetection={closestCenter}
                        onDragStart={(e) => setActiveDragId(String(e.active.id))}
                        onDragEnd={handleDragEnd}
                        onDragCancel={() => setActiveDragId(null)}
                    >
                        <SortableContext items={columns} strategy={verticalListSortingStrategy}>
                            {columns.map((col) => (
                                <StorySortableRow
                                    key={col}
                                    id={col}
                                    isSelected={selectedIds.includes(col)}
                                    isPinned={pinnedIds.includes(col)}
                                    onToggle={() => handleToggle(col)}
                                    onTogglePin={() => handleTogglePin(col)}
                                />
                            ))}
                        </SortableContext>
                        <DragOverlay>
                            {activeColumn ? (
                                <StoryColumnRowPreview
                                    id={activeColumn}
                                    isSelected={selectedIds.includes(activeColumn)}
                                    isPinned={pinnedIds.includes(activeColumn)}
                                />
                            ) : null}
                        </DragOverlay>
                    </DndContext>
                </div>
            </ViewSheet.Body>
            <ViewSheet.Footer>
                <div className="d-flex justify-content-between w-100">
                    <Button variant="secondary" title="Reset to default" onClick={handleReset}>
                        <Icon icon="reset" />
                        Reset to default
                    </Button>
                    <Button variant="success" title="Apply changes" onClick={handleApply}>
                        <Icon icon="save" />
                        Apply
                    </Button>
                </div>
            </ViewSheet.Footer>
        </ViewSheet>
    );
}

interface StoryRowProps {
    id: string;
    isSelected: boolean;
    isPinned: boolean;
    onToggle?: () => void;
    onTogglePin?: () => void;
}

function StorySortableRow({ id, isSelected, isPinned, onToggle, onTogglePin }: StoryRowProps) {
    const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id });

    const style: CSSProperties = {
        transform: CSS.Transform.toString(transform),
        transition,
        opacity: isDragging ? 0.5 : 1,
        padding: "6px 12px",
        backgroundColor: "var(--well-bg)",
        display: "flex",
        alignItems: "center",
        gap: "4px",
        userSelect: "none",
    };

    return (
        <div ref={setNodeRef} style={style}>
            <Checkbox selected={isSelected} toggleSelection={onToggle} title={`Toggle ${id}`}>
                <span style={{ flex: 1 }}>{id}</span>
            </Checkbox>
            <Button
                variant="link"
                size="sm"
                className={classNames("ms-auto p-0", isPinned ? "text-primary" : "text-reset")}
                title={isPinned ? "Unpin column" : "Pin column to left"}
                onClick={onTogglePin}
            >
                <Icon icon={isPinned ? "pinned" : "pin"} margin="m-0" />
            </Button>
            <span
                style={{ cursor: isDragging ? "grabbing" : "grab", display: "flex", alignItems: "center", padding: "0 4px" }}
                {...attributes}
                {...listeners}
            >
                <Icon icon="reorder" margin="m-0" />
            </span>
        </div>
    );
}

function StoryColumnRowPreview({ id, isSelected, isPinned }: Omit<StoryRowProps, "onToggle" | "onTogglePin">) {
    return (
        <div
            style={{
                padding: "6px 12px",
                backgroundColor: "var(--well-bg)",
                display: "flex",
                alignItems: "center",
                gap: "4px",
            }}
        >
            <Checkbox selected={isSelected} toggleSelection={() => {}} title={`Toggle ${id}`}>
                <span style={{ flex: 1 }}>{id}</span>
            </Checkbox>
            <Button
                variant="link"
                size="sm"
                className={classNames("ms-auto p-0", isPinned ? "text-primary" : "text-reset")}
                title={isPinned ? "Unpin column" : "Pin column to left"}
            >
                <Icon icon={isPinned ? "pinned" : "pin"} margin="m-0" />
            </Button>
            <span style={{ cursor: "grab", display: "flex", alignItems: "center", padding: "0 4px" }}>
                <Icon icon="reorder" margin="m-0" />
            </span>
        </div>
    );
}
