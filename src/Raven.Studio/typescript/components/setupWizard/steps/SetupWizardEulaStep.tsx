import Code from "components/common/Code";
import { Icon } from "components/common/Icon";
import { useServices } from "components/hooks/useServices";
import { useAsync } from "react-async-hook";
import Button from "react-bootstrap/Button";
import { useFormContext } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import genUtils from "common/generalUtils";
import { ConditionalPopover } from "components/common/ConditionalPopover";
import { LazyLoad } from "components/common/LazyLoad";
import { useDispatch } from "react-redux";
import { setupWizardActions, setupWizardSelectors } from "../store/setupWizardSlice";
import { useAppSelector } from "components/store";
import { useCallback } from "react";

export function SetupWizardEulaStep() {
    const dispatch = useDispatch();
    const { setupWizardService } = useServices();

    const asyncGetEula = useAsync(setupWizardService.getEula, []);

    const handleScroll = useCallback(
        (node: React.UIEvent<HTMLDivElement, UIEvent>) => {
            if (genUtils.isScrolledToBottom(node.target as HTMLElement)) {
                dispatch(setupWizardActions.isEulaScrolledToBottomSet(true));
            }
        },
        [dispatch]
    );

    return (
        <div className="vstack flex-grow h-75 eula-step">
            <h2>Read the EULA (End-User License Agreement)</h2>
            <p>The following license agreement must be accepted in order to use this software.</p>
            <div className="code-container" onScroll={handleScroll}>
                <LazyLoad active={asyncGetEula.loading}>
                    <Code language="plaintext" code={asyncGetEula.result ?? ""} />
                    <div data-testid="eula-bottom" />
                </LazyLoad>
            </div>
        </div>
    );
}

export function SetupWizardEulaStepFooter() {
    const { setValue } = useFormContext<SetupWizardFormData>();

    const isScrolledToBottom = useAppSelector(setupWizardSelectors.isEulaScrolledToBottom);

    const handleContinue = () => {
        setValue("currentStep", "Setup method");
    };

    return (
        <div className="d-flex justify-content-end">
            <ConditionalPopover
                conditions={{
                    isActive: !isScrolledToBottom,
                    message: "Review the EULA to enable the button.",
                }}
                popoverPlacement="top"
            >
                <Button
                    variant="primary"
                    className="rounded-pill"
                    onClick={handleContinue}
                    disabled={!isScrolledToBottom}
                >
                    Continue <Icon icon="arrow-right" margin="m-0" />
                </Button>
            </ConditionalPopover>
        </div>
    );
}
