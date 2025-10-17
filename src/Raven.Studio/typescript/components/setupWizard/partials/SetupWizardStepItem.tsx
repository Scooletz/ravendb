import "./SetupWizardStepItem.scss";
import classNames from "classnames";
import { HTMLAttributes, ReactNode } from "react";
import { Icon } from "components/common/Icon";
import IconName from "typings/server/icons";

interface SetupWizardStepItemProps extends HTMLAttributes<HTMLLIElement> {
    children: ReactNode;
    isCurrent?: boolean;
    isChecked?: boolean;
    isInactive?: boolean;
    className?: string;
    stepIndicator?: ReactNode;
}

export function SetupWizardStepItem(props: SetupWizardStepItemProps) {
    const { children, isCurrent, isChecked, isInactive, className, stepIndicator, ...rest } = props;

    const dotIcon = ((): IconName => {
        if (isChecked) {
            return "check";
        }
        if (isCurrent) {
            return "arrow-thin-right";
        }

        return null;
    })();

    return (
        <li title="Go to this step" className={classNames("setup-wizard-step-item lh-1", className)} {...rest}>
            <span
                className={classNames(
                    "dot",
                    { inactive: isInactive },
                    { "bg-light": isChecked },
                    { "border-primary text-primary": isCurrent }
                )}
            >
                {stepIndicator && (
                    <span className="d-flex align-items-center justify-content-center">{stepIndicator}</span>
                )}
                {!stepIndicator && dotIcon && <Icon icon={dotIcon} margin="m-0" />}
            </span>
            {children}
        </li>
    );
}
