import React, { useState } from "react";
import { Icon } from "components/common/Icon";
import Button from "react-bootstrap/Button";
import Form from "react-bootstrap/Form";
import Modal from "components/common/Modal";
import ButtonWithSpinner from "components/common/ButtonWithSpinner";
import RichAlert from "components/common/RichAlert";
import { Switch } from "components/common/Checkbox";
import { FormGroup, FormLabel } from "components/common/Form";
import { useServices } from "hooks/useServices";
import { useAppSelector } from "components/store";
import { databaseSelectors } from "components/common/shell/databaseSliceSelectors";
import { useAsync, useAsyncCallback } from "react-async-hook";
import studioSettings from "common/settings/studioSettings";
import messagePublisher from "common/messagePublisher";
import DatabaseUtils from "components/utils/DatabaseUtils";
import { tryHandleSubmit } from "components/utils/common";
import { EtlTaskWithErrors, EtlTransformationWithErrors } from "../utils/tasksErrorsUtils";
import footer from "common/shell/footer";

function useDeleteConfirmation(isRequireTypedConfirm: boolean) {
    const [confirmText, setConfirmText] = useState("");

    const handleTextChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        setConfirmText(e.target.value.trim());
    };

    return {
        confirmText,
        handleTextChange,
        isConfirmed: isRequireTypedConfirm ? confirmText === "DELETE" : true,
    };
}

interface DeleteTaskErrorsModalProps {
    toggle: () => void;
    onRefresh: () => void;
    etlName: string;
    transformations: EtlTransformationWithErrors[];
    errorsCount: number;
}

export function DeleteTaskErrorsModal({
    toggle,
    onRefresh,
    etlName,
    transformations,
    errorsCount,
}: DeleteTaskErrorsModalProps) {
    const db = useAppSelector(databaseSelectors.activeDatabase);
    const { tasksService } = useServices();

    const asyncGlobalSettings = useAsync(async () => await studioSettings.default.globalSettings(), []);

    const isRequireTypedConfirm =
        asyncGlobalSettings.result?.isRequireTypedConfirmationToDeleteEtlErrors.getValue() ?? true;

    const { confirmText, handleTextChange, isConfirmed } = useDeleteConfirmation(isRequireTypedConfirm);

    const asyncDeleteErrors = useAsyncCallback(async () => {
        try {
            const processNames = transformations.map((t) => `${etlName}/${t.transformationName}`);
            const locations = DatabaseUtils.getLocations(db);
            await Promise.all(
                locations.map((location) =>
                    tasksService.deleteEtlErrors(db.name, {
                        name: processNames,
                        nodeTag: location.nodeTag,
                        shardNumber: location.shardNumber,
                    })
                )
            );
            footer.default.refreshStats();
            toggle();
            onRefresh();
        } catch (e) {
            console.error(e);
        }
    });

    const toggleIsRequireTypedConfirm = async () => {
        if (!asyncGlobalSettings.result) {
            messagePublisher.reportError("Failed to load studio global settings");
            return;
        }

        asyncGlobalSettings.result.isRequireTypedConfirmationToDeleteEtlErrors.setValue(!isRequireTypedConfirm);
        await asyncGlobalSettings.execute();
    };

    return (
        <Modal show contentClassName="modal-border bulge-danger">
            <Modal.Header closeButton onCloseClick={toggle} className="pb-0">
                <h3>
                    <Icon icon="trash" color="danger" />
                    <span>Delete all errors for {etlName} task?</span>
                </h3>
            </Modal.Header>
            <Modal.Body className="pt-0">
                <p>
                    You are about to delete all <b>{errorsCount} errors</b> from <b>{etlName}</b> task.
                </p>
                <RichAlert variant="info" icon="info">
                    While the current task errors will be deleted, a task in an <b>Error state</b> will not set back to
                    the <b>Normal</b> state.
                </RichAlert>
                {isRequireTypedConfirm && (
                    <FormGroup className="mt-3">
                        <FormLabel className="fw-bold">Type DELETE to confirm</FormLabel>
                        <Form.Control placeholder="DELETE" value={confirmText} onChange={handleTextChange} />
                    </FormGroup>
                )}
            </Modal.Body>
            <Modal.Footer className="hstack justify-content-between">
                <Switch selected={isRequireTypedConfirm} toggleSelection={toggleIsRequireTypedConfirm} color="primary">
                    Require typed confirmation
                </Switch>
                <div className="hstack gap-2 flex-grow-1 justify-content-end">
                    <Button variant="link" onClick={toggle} className="link-muted">
                        Cancel
                    </Button>
                    <ButtonWithSpinner
                        isSpinning={asyncDeleteErrors.loading}
                        variant="danger"
                        onClick={asyncDeleteErrors.execute}
                        className="rounded-pill"
                        disabled={!isConfirmed || asyncDeleteErrors.loading}
                    >
                        Delete
                    </ButtonWithSpinner>
                </div>
            </Modal.Footer>
        </Modal>
    );
}

interface DeleteAllErrorsModalProps {
    toggle: () => void;
    onRefresh: () => void;
    tasksWithErrors: EtlTaskWithErrors[];
}

export function DeleteAllErrorsModal({ toggle, onRefresh, tasksWithErrors }: DeleteAllErrorsModalProps) {
    const db = useAppSelector(databaseSelectors.activeDatabase);
    const { tasksService } = useServices();

    const asyncGlobalSettings = useAsync(async () => await studioSettings.default.globalSettings(), []);

    const isRequireTypedConfirm =
        asyncGlobalSettings.result?.isRequireTypedConfirmationToDeleteEtlErrors.getValue() ?? true;

    const { confirmText, handleTextChange, isConfirmed } = useDeleteConfirmation(isRequireTypedConfirm);

    const asyncDeleteAllErrors = useAsyncCallback(() => {
        const processNames = tasksWithErrors.flatMap((task) =>
            task.transformations.map((t) => `${task.etlName}/${t.transformationName}`)
        );

        return tryHandleSubmit(async () => {
            const locations = DatabaseUtils.getLocations(db);
            await Promise.all(
                locations.map((location) =>
                    tasksService.deleteEtlErrors(db.name, {
                        name: processNames,
                        ...location,
                    })
                )
            );
            footer.default.refreshStats();
            toggle();
            onRefresh();
        });
    });

    const toggleIsRequireTypedConfirm = async () => {
        if (!asyncGlobalSettings.result) {
            messagePublisher.reportError("Failed to load studio global settings");
            return;
        }

        asyncGlobalSettings.result.isRequireTypedConfirmationToDeleteEtlErrors.setValue(!isRequireTypedConfirm);
        await asyncGlobalSettings.execute();
    };

    return (
        <Modal show contentClassName="modal-border bulge-danger">
            <Modal.Header closeButton onCloseClick={toggle} className="pb-0">
                <h3>
                    <Icon icon="trash" color="danger" />
                    <span>Delete all errors?</span>
                </h3>
            </Modal.Header>
            <Modal.Body className="pt-0">
                <p>
                    You are about to delete errors from <b>{tasksWithErrors.length}</b>{" "}
                    {tasksWithErrors.length === 1 ? "task" : "tasks"}.
                </p>
                {isRequireTypedConfirm && (
                    <Form.Group>
                        <Form.Label className="fw-bold">Type DELETE to confirm</Form.Label>
                        <Form.Control placeholder="DELETE" value={confirmText} onChange={handleTextChange} />
                    </Form.Group>
                )}
            </Modal.Body>
            <Modal.Footer className="hstack justify-content-between">
                <Switch selected={isRequireTypedConfirm} toggleSelection={toggleIsRequireTypedConfirm} color="primary">
                    Require typed confirmation
                </Switch>
                <div className="hstack gap-2 flex-grow-1 justify-content-end">
                    <Button variant="link" onClick={toggle} className="link-muted">
                        Cancel
                    </Button>
                    <ButtonWithSpinner
                        isSpinning={asyncDeleteAllErrors.loading}
                        variant="danger"
                        onClick={asyncDeleteAllErrors.execute}
                        className="rounded-pill"
                        disabled={!isConfirmed || asyncDeleteAllErrors.loading}
                    >
                        Delete
                    </ButtonWithSpinner>
                </div>
            </Modal.Footer>
        </Modal>
    );
}
