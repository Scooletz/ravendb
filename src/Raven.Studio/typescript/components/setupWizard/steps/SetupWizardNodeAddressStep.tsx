import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import { Icon } from "components/common/Icon";
import { Button } from "react-bootstrap";

export function SetupWizardNodeAddressStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const { nodeAddressStep } = useWatch({ control });

    return <h1>Node address</h1>;
}

export function SetupWizardNodeAddressStepFooter() {
    const { setValue } = useFormContext<SetupWizardFormData>();

    const handleContinue = () => {
        setValue("currentStep", "Additional settings");
    };

    return (
        <div className="hstack justify-content-end">
            <Button variant="primary" className="rounded-pill" onClick={handleContinue}>
                Continue <Icon icon="arrow-right" margin="m-0" />
            </Button>
        </div>
    );
}
