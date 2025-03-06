import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import Button from "react-bootstrap/Button";
import { Icon } from "components/common/Icon";

export function SetupWizardUsePackageStep() {
    const { control } = useFormContext<SetupWizardFormData>();

    const { usePackageStep } = useWatch({ control });

    return <h1>Use setup package</h1>;
}

export function SetupWizardUsePackageStepFooter() {
    const { setValue } = useFormContext<SetupWizardFormData>();

    const handleContinue = () => {
        setValue("currentStep", "Summary");
    };

    return (
        <div className="hstack justify-content-end">
            <Button variant="primary" className="rounded-pill" onClick={handleContinue}>
                Continue <Icon icon="arrow-right" margin="m-0" />
            </Button>
        </div>
    );
}
