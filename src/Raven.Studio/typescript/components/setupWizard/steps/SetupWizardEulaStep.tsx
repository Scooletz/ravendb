import Code from "components/common/Code";
import { Icon } from "components/common/Icon";
import { useServices } from "components/hooks/useServices";
import { useAsync } from "react-async-hook";
import { Button } from "react-bootstrap";
import { useFormContext } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import useBoolean from "components/hooks/useBoolean";
import genUtils from "common/generalUtils";
import { ConditionalPopover } from "components/common/ConditionalPopover";
import { LazyLoad } from "components/common/LazyLoad";

export function SetupWizardEulaStep({ eulaRef }: { eulaRef: React.RefObject<HTMLDivElement> }) {
    const { setupWizardService } = useServices();

    const asyncGetEula = useAsync(setupWizardService.getEula, []);

    // TODO enable Continue button when EULA is scrolled to the bottom

    return (
        <div>
            <h2>Read the EULA (End-User License Agreement)</h2>
            <p>The following license agreement must be accepted in order to use this software.</p>
            <div ref={eulaRef} className="overflow-y-auto" style={{ height: 300 }} data-testid="eula">
                <LazyLoad active={asyncGetEula.loading}>
                    <Code language="plaintext" code={asyncGetEula.result ?? "Loading"} />
                </LazyLoad>
                <div data-testid="eula-bottom" id="eula-bottom" />
            </div>
        </div>
    );
}

export function SetupWizardEulaStepFooter({ eulaRef }: { eulaRef: React.RefObject<HTMLDivElement> }) {
    const { setValue } = useFormContext<SetupWizardFormData>();

    const { value: isScrolledToBottom, setValue: setIsScrolledToBottom } = useBoolean(false);

    const { useEffect } = require("react");

    // Set isScrolledToBottom when the user scrolls to the bottom of the EULA
    useEffect(() => {
        const element = eulaRef?.current;
        if (!element) {
            return;
        }

        const handleScroll = () => {
            if (genUtils.isScrolledToBottom(eulaRef.current)) {
                setIsScrolledToBottom(true);
            }
        };

        element.addEventListener("scroll", handleScroll);

        return () => {
            element.removeEventListener("scroll", handleScroll);
        };
    }, [eulaRef, isScrolledToBottom, setIsScrolledToBottom]);

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
