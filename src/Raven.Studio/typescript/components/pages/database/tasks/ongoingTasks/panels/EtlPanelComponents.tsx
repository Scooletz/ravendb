import React from "react";
import { RichPanelDetailItem } from "components/common/RichPanel";
import { Icon } from "components/common/Icon";
import Button from "react-bootstrap/Button";
import Badge from "react-bootstrap/Badge";
import PopoverWithHoverWrapper from "components/common/PopoverWithHoverWrapper";
import { ProgressCircle } from "components/common/ProgressCircle";
import {
    EtlHealthStatus,
    EtlPanelProgress,
    getPopoverMessageForTaskHealth,
    healthStatusToBadge,
} from "./etlPanelUtils";

interface EtlPanelToggleButtonProps {
    detailsVisible: boolean;
    toggleDetails: () => void;
}

export function EtlPanelToggleButton({ detailsVisible, toggleDetails }: EtlPanelToggleButtonProps) {
    return (
        <RichPanelDetailItem>
            <Button
                variant="secondary"
                className="btn-toggle-panel rounded-pill"
                onClick={toggleDetails}
                title="Click for details"
            >
                <Icon icon={detailsVisible ? "fold" : "unfold"} margin="m-0" />
            </Button>
        </RichPanelDetailItem>
    );
}

interface EtlPanelHealthBadgeProps {
    taskHealth: EtlHealthStatus;
}

export function EtlPanelHealthBadge({ taskHealth }: EtlPanelHealthBadgeProps) {
    const { bg, icon, label } = healthStatusToBadge(taskHealth);

    return (
        <RichPanelDetailItem label="Health status">
            <PopoverWithHoverWrapper
                wrapperClassName="d-flex align-items-center"
                message={getPopoverMessageForTaskHealth(taskHealth)}
            >
                <Badge bg={bg} className="rounded-pill">
                    <Icon icon={icon} />
                    {label}
                </Badge>
            </PopoverWithHoverWrapper>
        </RichPanelDetailItem>
    );
}

interface EtlPanelErrorsProps {
    errorCount: number;
    goToTaskErrors: string;
}

export function EtlPanelErrors({ errorCount, goToTaskErrors }: EtlPanelErrorsProps) {
    if (errorCount === 0) {
        return null;
    }

    return (
        <RichPanelDetailItem>
            <a href={goToTaskErrors} className="d-flex gap-1 align-items-center">
                <Icon icon="warning" color="danger" margin="m-0" />
                <span className="text-danger">Errors</span>
                <b>{errorCount}</b>
            </a>
        </RichPanelDetailItem>
    );
}

interface EtlPanelProgressItemProps {
    etlProgress: EtlPanelProgress;
}

export function EtlPanelProgressItem({ etlProgress }: EtlPanelProgressItemProps) {
    return (
        <RichPanelDetailItem>
            <ProgressCircle state={etlProgress.state} icon={etlProgress.icon} progress={etlProgress.progress} inline>
                {etlProgress.label}
            </ProgressCircle>
        </RichPanelDetailItem>
    );
}
