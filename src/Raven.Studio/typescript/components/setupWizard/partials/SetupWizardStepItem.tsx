import "./SetupWizardStepItem.scss";
import classNames from "classnames";
import { ReactNode } from "react";
import { Icon } from "components/common/Icon";
import IconName from "typings/server/icons";

interface SetupWizardStepItemProps {
    children: ReactNode;
    isCurrent?: boolean;
    isChecked?: boolean;
    isInactive?: boolean;
    className?: string;
}

export function SetupWizardStepItem(props: SetupWizardStepItemProps) {
    const { children, isCurrent, isChecked, isInactive, className } = props;

    const dotIcon = ((): IconName => {
        if (isChecked) {
            return "check";
        }
        if (isCurrent) {
            return "arrow-right";
        }

        return null;
    })();

    return (
        <li className={classNames("setup-wizard-step-item", className)}>
            <span className={classNames("dot", { inactive: isInactive })}>
                {dotIcon && <Icon icon={dotIcon} margin="m-0" />}
            </span>
            {children}
        </li>
    );
}
